using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Flamme.Authorization;
using Flamme.Identity.Permissions;
using Flamme.Identity.Services;
using Flamme.Permissions;
using FastEndpoints;
using System.Threading.Tasks;

namespace Flamme.Identity.UnitTests.Authorization;

/// <summary>
/// Pins what the three endpoints that used to carry the <c>SecurityRoot</c> policy require now that the
/// policy is gone, and checks that what they require is something the Identity catalog advertises.
/// </summary>
/// <remarks>
/// Two of the three (<c>Roles/Create</c>, <c>Applications/Create</c>) only lost a redundant policy line and
/// must keep the permission they already declared; <c>Secrets/Hash</c> gained one where it previously had
/// nothing but "any authenticated caller". The coverage gate only asks whether an endpoint declares
/// <em>something</em>, so without these rows either half of that could silently change.
/// </remarks>
public class EndpointPermissionTests
{
    private static readonly Assembly Module = typeof(RoleAuthorizationService).Assembly;

    public static IEnumerable<(string, string, string)> Declarations =>
    [
        ("Flamme.Identity.Endpoints.Secrets.Hash.Hash", IdentityPermissions.Users, CoreVerbs.Create),
        ("Flamme.Identity.Endpoints.Roles.Create.Create", IdentityPermissions.Roles, CoreVerbs.Create),
        ("Flamme.Identity.Endpoints.Applications.Create.Create", IdentityPermissions.Applications, CoreVerbs.Create)
    ];

    [Test]
    [MethodDataSource(nameof(Declarations))]
    public async Task EndpointDeclaresItsExpectedPermission(string endpointTypeName, string resource, string verb) =>
        await Assert.That(await Declare(endpointTypeName)).IsEqualTo(new Permission(resource, verb));

    [Test]
    [MethodDataSource(nameof(Declarations))]
    public async Task EveryDeclaredPermissionIsAdvertisedByTheCatalog(string endpointTypeName, string resource, string verb)
    {
        var declared = await Declare(endpointTypeName);
        var descriptor = new IdentityPermissionsDescriptorProvider().GetDescriptors().SingleOrDefault(x => x.Resource == declared.Resource);

        await Assert.That(descriptor is not null).IsTrue().Because($"{endpointTypeName} requires resource '{declared.Resource}', which the module contributes no descriptor for, so it cannot be granted through the role editor.");
        await Assert.That(descriptor!.Supports(declared.Verb)).IsTrue().Because($"{endpointTypeName} requires '{declared}', but '{declared.Resource}' advertises only [{string.Join(", ", descriptor.SupportedVerbs)}].");
        await Assert.That(declared).IsEqualTo(new Permission(resource, verb));
    }

    /// <summary>
    /// Runs one endpoint's <c>Configure()</c> and returns what it recorded. The requirement is attached as an
    /// inline policy, which cannot be read back off the definition, so the registry is the only way to observe
    /// a declaration without booting a host. The instance skips its constructor because <c>Configure()</c>
    /// touches none of the injected services, and substituting them would make the rows depend on which
    /// dependencies happen to be interfaces.
    /// </summary>
    // Constant assembly-qualified names let the trimmer preserve these internal endpoints.
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    private static Type ResolveEndpointType(string name) => name switch
    {
        "Flamme.Identity.Endpoints.Secrets.Hash.Hash" => Type.GetType("Flamme.Identity.Endpoints.Secrets.Hash.Hash, Flamme.Identity", throwOnError: true)!,
        "Flamme.Identity.Endpoints.Roles.Create.Create" => Type.GetType("Flamme.Identity.Endpoints.Roles.Create.Create, Flamme.Identity", throwOnError: true)!,
        "Flamme.Identity.Endpoints.Applications.Create.Create" => Type.GetType("Flamme.Identity.Endpoints.Applications.Create.Create, Flamme.Identity", throwOnError: true)!,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown endpoint.")
    };

    private static async Task<Permission> Declare(string endpointTypeName)
    {
        var endpointType = ResolveEndpointType(endpointTypeName);
        var endpoint = RuntimeHelpers.GetUninitializedObject(endpointType);
        var (requestType, responseType) = DtoTypes(endpointType);

        endpointType.GetProperty("Definition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(endpoint, new EndpointDefinition(endpointType, requestType, responseType));
        endpointType.GetMethod("Configure")!.Invoke(endpoint, null);

        var permission = EndpointPermissionRegistry.Find(endpointType);

        await Assert.That(permission.HasValue).IsTrue().Because($"{endpointTypeName} declares no permission.");
        return permission!.Value;
    }

    private static (Type Request, Type Response) DtoTypes(Type endpointType)
    {
        for (var type = endpointType.BaseType; type is not null; type = type.BaseType)
        {
            if (!type.IsGenericType)
                continue;

            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();

            if (definition == typeof(Flamme.Abstractions.FlammeEndpoint<,>))
                return (arguments[0], arguments[1]);
            if (definition == typeof(Flamme.Abstractions.FlammeEndpointWithoutRequest<>))
                return (typeof(EmptyRequest), arguments[0]);
            if (definition == typeof(Flamme.Abstractions.FlammeEndpoint<>))
                return (arguments[0], typeof(object));
        }

        throw new InvalidOperationException($"Unsupported endpoint type '{endpointType.FullName}'.");
    }
}