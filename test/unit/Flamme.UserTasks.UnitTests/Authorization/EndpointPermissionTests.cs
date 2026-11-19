using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Flamme.Authorization;
using Flamme.Common;
using Flamme.Permissions;
using Flamme.UserTasks.Contracts;
using Flamme.UserTasks.Options;
using Flamme.UserTasks.Permissions;
using Flamme.UserTasks.Services;
using FastEndpoints;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using System.Threading.Tasks;

namespace Flamme.UserTasks.UnitTests.Authorization;

/// <summary>
/// Pins what each User Tasks endpoint requires, and checks that what they require is something the catalog
/// advertises.
/// </summary>
/// <remarks>
/// The module reached production declaring access through the legacy string channel, which compared claims for
/// exact equality and appeared in no catalog. Nothing could see the gap: the coverage gate only asks whether an
/// endpoint declares <em>something</em>, and the migration guide's consistency check reads the guide, so a
/// permission absent from both the guide and the catalog was invisible from every direction. Asserting the
/// declarations against the descriptors closes that: an endpoint requiring a verb the module never advertises
/// cannot be granted through the role editor, and fails here rather than in a deployment.
/// </remarks>
public class EndpointPermissionTests
{
    private static readonly Assembly Module = typeof(DefaultUserTaskAccessPolicy).Assembly;

    /// <summary>What each endpoint is expected to require. The rows are the migration guide's table, in code.</summary>
    private static readonly (string Endpoint, string Resource, string Verb)[] Expected =
    [
        ("FeatureCapabilitiesEndpoint", UserTasksResourcePermissions.UserTasks, CoreVerbs.View),
        ("ListEndpoint", UserTasksResourcePermissions.UserTasks, CoreVerbs.View),
        ("GetEndpoint", UserTasksResourcePermissions.UserTasks, CoreVerbs.View),
        ("CapabilitiesEndpoint", UserTasksResourcePermissions.UserTasks, CoreVerbs.View),
        ("ListEventsEndpoint", UserTasksResourcePermissions.UserTasks, CoreVerbs.View),
        ("RevealFieldEndpoint", UserTasksResourcePermissions.UserTasks, CoreVerbs.View),
        ("ClaimEndpoint", UserTasksResourcePermissions.UserTasks, UserTaskVerbs.Claim),
        ("ReleaseEndpoint", UserTasksResourcePermissions.UserTasks, UserTaskVerbs.Claim),
        ("AssignEndpoint", UserTasksResourcePermissions.UserTasks, UserTaskVerbs.Assign),
        ("ScheduleEndpoint", UserTasksResourcePermissions.UserTasks, CoreVerbs.Update),
        ("CompleteEndpoint", UserTasksResourcePermissions.UserTasks, UserTaskVerbs.Complete),
        ("CancelEndpoint", UserTasksResourcePermissions.UserTasks, UserTaskVerbs.Cancel),
        ("RetryResolutionEndpoint", UserTasksResourcePermissions.UserTasks, UserTaskVerbs.Supervise),
        ("IssueInvitationEndpoint", UserTasksResourcePermissions.UserTasks, UserTaskVerbs.Invite),
        ("ListInvitationsEndpoint", UserTasksResourcePermissions.UserTasks, UserTaskVerbs.Invite),
        ("RevokeInvitationEndpoint", UserTasksResourcePermissions.UserTasks, UserTaskVerbs.Invite),
        ("ListParticipantsEndpoint", UserTasksResourcePermissions.Participants, CoreVerbs.View)
    ];

    public static IEnumerable<(string, string, string)> Declarations => Expected;

    [Test]
    [MethodDataSource(nameof(Declarations))]
    public async Task EndpointDeclaresItsExpectedPermission(string endpointName, string resource, string verb)
    {
        var declared = await Declare(endpointName);

        await Assert.That(declared).IsEqualTo(new Permission(resource, verb));
    }

    [Test]
    [MethodDataSource(nameof(Declarations))]
    public async Task EveryDeclaredPermissionIsAdvertisedByTheCatalog(string endpointName, string resource, string verb)
    {
        var declared = await Declare(endpointName);
        var descriptor = new UserTasksResourcePermissionsDescriptorProvider().GetDescriptors()
            .SingleOrDefault(x => x.Resource == declared.Resource);

        await Assert.That(descriptor is not null).IsTrue().Because($"{endpointName} requires resource '{declared.Resource}', which the module contributes no descriptor for, so it cannot be granted through the role editor.");
        await Assert.That(descriptor!.Supports(declared.Verb)).IsTrue().Because($"{endpointName} requires '{declared}', but '{declared.Resource}' advertises only [{string.Join(", ", descriptor.SupportedVerbs)}].");
        // The theory data is what the migration guide's rows are written against, so it has to agree too.
        await Assert.That(declared).IsEqualTo(new Permission(resource, verb));
    }

    [Test]
    public async Task EveryEndpointInTheModuleIsCovered()
    {
        // Without this, deleting a row would silently stop testing an endpoint rather than fail.
        var declaring = EndpointNames().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var asserted = Expected.Select(x => x.Endpoint).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        await Assert.That(declaring).IsEquivalentTo(asserted, StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task EveryAdvertisedVerbGuardsSomething()
    {
        // A verb nobody requires is one an administrator can grant to no effect.
        var required = Expected.Select(x => new Permission(x.Resource, x.Verb)).ToHashSet();
        var unused = new UserTasksResourcePermissionsDescriptorProvider().GetDescriptors()
            .SelectMany(x => x.SupportedVerbs.Select(verb => new Permission(x.Resource, verb)))
            .Where(x => !required.Contains(x))
            .Select(x => x.ToString())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(unused.Length == 0).IsTrue().Because($"The catalog advertises {unused.Length} permission(s) no endpoint requires: {string.Join(", ", unused)}.");
    }

    /// <summary>The Flamme endpoints this module declares, by simple name.</summary>
    private static IEnumerable<string> EndpointNames() =>
        Flamme.Testing.Shared.Authorization.EndpointCoverage.FindEndpoints(Module).Select(x => x.Name);

    /// <summary>
    /// Runs one endpoint's <c>Configure()</c> and returns what it recorded. The requirement is attached as an
    /// inline policy, which cannot be read back off the definition, so the registry is the only way to observe
    /// a declaration without booting a host.
    /// </summary>
    // Constant assembly-qualified names let the trimmer preserve these internal endpoints.
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    private static Type ResolveEndpointType(string name) => name switch
    {
        "FeatureCapabilitiesEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.FeatureCapabilitiesEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "ListEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.ListEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "GetEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.GetEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "CapabilitiesEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.CapabilitiesEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "ListEventsEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.ListEventsEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "RevealFieldEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.RevealFieldEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "ClaimEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.ClaimEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "ReleaseEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.ReleaseEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "AssignEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.AssignEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "ScheduleEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.ScheduleEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "CompleteEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.CompleteEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "CancelEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.CancelEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "RetryResolutionEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.RetryResolutionEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "IssueInvitationEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.IssueInvitationEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "ListInvitationsEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.ListInvitationsEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "RevokeInvitationEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.RevokeInvitationEndpoint, Flamme.UserTasks", throwOnError: true)!,
        "ListParticipantsEndpoint" => Type.GetType("Flamme.UserTasks.Endpoints.ListParticipantsEndpoint, Flamme.UserTasks", throwOnError: true)!,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown endpoint.")
    };

    private static async Task<Permission> Declare(string endpointName)
    {
        var endpointType = ResolveEndpointType(endpointName);
        var arguments = endpointType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single()
            .GetParameters()
            .Select(x => CreateDependency(x.ParameterType))
            .ToArray();
        var endpoint = Activator.CreateInstance(endpointType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null)!;
        var (requestType, responseType) = DtoTypes(endpointType);

        endpointType.GetProperty("Definition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(endpoint, new EndpointDefinition(endpointType, requestType, responseType));
        endpointType.GetMethod("Configure")!.Invoke(endpoint, null);

        var permission = EndpointPermissionRegistry.Find(endpointType);

        await Assert.That(permission.HasValue).IsTrue().Because($"{endpointName} declares no permission.");
        return permission!.Value;
    }

    private static object CreateDependency(Type type)
    {
        if (type == typeof(IUserTaskIdentityResolver)) return IUserTaskIdentityResolver.Mock().Object;
        if (type == typeof(IOptions<UserTasksOptions>)) return IOptions<UserTasksOptions>.Mock().Object;
        if (type == typeof(IUserTaskParticipantDirectory)) return IUserTaskParticipantDirectory.Mock().Object;
        if (type == typeof(IUserTaskManager)) return IUserTaskManager.Mock().Object;
        if (type == typeof(TimeProvider)) return new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        if (type == typeof(IUserTaskAccessPolicy)) return IUserTaskAccessPolicy.Mock().Object;
        if (type == typeof(IUserTaskInvitationService)) return IUserTaskInvitationService.Mock().Object;
        throw new InvalidOperationException($"No generated mock registered for endpoint dependency '{type}'.");
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
