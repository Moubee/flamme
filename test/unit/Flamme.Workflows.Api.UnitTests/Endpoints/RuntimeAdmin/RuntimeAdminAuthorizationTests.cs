using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Flamme.Authorization;
using Flamme.Workflows.Api.Permissions;
using Flamme.Workflows.Runtime;
using FastEndpoints;
using WorkflowsApiFeature = Flamme.Workflows.Api.Features.WorkflowsApiFeature;
namespace Flamme.Workflows.Api.UnitTests.Endpoints.RuntimeAdmin;

public class RuntimeAdminAuthorizationTests
{
    [Test]
    public async Task StatusEndpoint_RequiresOnlyViewOnTheRuntime()
    {
        // Reading runtime status must not require the verb that pauses or drains it.
        var permission = await GetDeclaredPermission("Flamme.Workflows.Api.Endpoints.RuntimeAdmin.Status.StatusEndpoint");

        await Assert.That(permission.Resource).IsEqualTo(WorkflowPermissions.Runtime);
        await Assert.That(permission.Verb).IsEqualTo(CoreVerbs.View);
    }

    [Test]
    [Arguments("Flamme.Workflows.Api.Endpoints.RuntimeAdmin.Pause.PauseEndpoint")]
    [Arguments("Flamme.Workflows.Api.Endpoints.RuntimeAdmin.Resume.ResumeEndpoint")]
    [Arguments("Flamme.Workflows.Api.Endpoints.RuntimeAdmin.ForceDrain.ForceDrainEndpoint")]
    public async Task MutatingEndpoints_RequireControlOnTheRuntime(string endpointTypeName)
    {
        var permission = await GetDeclaredPermission(endpointTypeName);

        await Assert.That(permission.Resource).IsEqualTo(WorkflowPermissions.Runtime);
        await Assert.That(permission.Verb).IsEqualTo("control");
        await Assert.That(permission.Verb).IsNotEqualTo(CoreVerbs.View);
    }

    // Constant assembly-qualified names let the trimmer preserve these internal endpoints.
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    private static Type ResolveEndpointType(string name) => name switch
    {
        "Flamme.Workflows.Api.Endpoints.RuntimeAdmin.Status.StatusEndpoint" => Type.GetType("Flamme.Workflows.Api.Endpoints.RuntimeAdmin.Status.StatusEndpoint, Flamme.Workflows.Api", throwOnError: true)!,
        "Flamme.Workflows.Api.Endpoints.RuntimeAdmin.Pause.PauseEndpoint" => Type.GetType("Flamme.Workflows.Api.Endpoints.RuntimeAdmin.Pause.PauseEndpoint, Flamme.Workflows.Api", throwOnError: true)!,
        "Flamme.Workflows.Api.Endpoints.RuntimeAdmin.Resume.ResumeEndpoint" => Type.GetType("Flamme.Workflows.Api.Endpoints.RuntimeAdmin.Resume.ResumeEndpoint, Flamme.Workflows.Api", throwOnError: true)!,
        "Flamme.Workflows.Api.Endpoints.RuntimeAdmin.ForceDrain.ForceDrainEndpoint" => Type.GetType("Flamme.Workflows.Api.Endpoints.RuntimeAdmin.ForceDrain.ForceDrainEndpoint, Flamme.Workflows.Api", throwOnError: true)!,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown endpoint.")
    };

    private static async Task<Permission> GetDeclaredPermission(string endpointTypeName)
    {
        var endpointType = ResolveEndpointType(endpointTypeName);
        var endpoint = Activator.CreateInstance(
            endpointType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [IWorkflowRuntimeAdminService.Mock().Object],
            null)!;
        var (requestDtoType, responseDtoType) = GetEndpointDtoTypes(endpointType);
        var definition = new EndpointDefinition(endpointType, requestDtoType, responseDtoType);

        endpointType
            .GetProperty("Definition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(endpoint, definition);

        endpointType.GetMethod("Configure")!.Invoke(endpoint, null);

        var permission = EndpointPermissionRegistry.Find(endpointType);

        await Assert.That(permission.HasValue).IsTrue().Because($"{endpointTypeName} declares no permission.");

        return permission!.Value;
    }

    private static (Type RequestDtoType, Type ResponseDtoType) GetEndpointDtoTypes(Type endpointType)
    {
        var type = endpointType;

        while (type.BaseType != null)
        {
            type = type.BaseType;

            if (!type.IsGenericType)
                continue;

            var genericTypeDefinition = type.GetGenericTypeDefinition();
            var genericArguments = type.GetGenericArguments();

            if (genericTypeDefinition == typeof(Abstractions.FlammeEndpoint<,>))
                return (genericArguments[0], genericArguments[1]);

            if (genericTypeDefinition == typeof(Abstractions.FlammeEndpointWithoutRequest<>))
                return (typeof(EmptyRequest), genericArguments[0]);
        }

        throw new InvalidOperationException($"Unsupported endpoint type '{endpointType.FullName}'.");
    }
}
