using System.Diagnostics.CodeAnalysis;
using Flamme.UnitTests.Shared;
using Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.List;
using Flamme.Workflows.Api.UnitTests.Endpoints.Descriptors;
using Flamme.Workflows.Api.UnitTests.Endpoints.Features;
using Flamme.Workflows.Management;
using FastEndpoints;

namespace Flamme.Workflows.Api.UnitTests.Endpoints;

/// <summary>
/// Pins the cleanup the classes in <see cref="FastEndpointsCollection"/> depend on, in the one order that used to fail:
/// an endpoint host runs and is disposed, and only then is an endpoint created through the factory.
/// </summary>
[NotInParallel(nameof(FastEndpointsCollection))]
public class FastEndpointsResolverTests
{
    [Test]
    [Arguments(typeof(DescriptorCatalogAuthorizationTests))]
    [Arguments(typeof(InstalledFeaturesAuthorizationTests))]
    public async Task FactoryCreatesEndpointsAfterAnEndpointHostIsDisposed([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type hostTestClass)
    {
        switch (Activator.CreateInstance(hostTestClass))
        {
            case DescriptorCatalogAuthorizationTests host:
                try
                {
                    await host.InitializeAsync();
                }
                finally
                {
                    await host.DisposeAsync();
                }
                break;
            case InstalledFeaturesAuthorizationTests host:
                try
                {
                    await host.InitializeAsync();
                }
                finally
                {
                    await host.DisposeAsync();
                }
                break;
            default:
                throw new InvalidOperationException($"Unsupported endpoint host test class: {hostTestClass}.");
        }

        await Assert.That(Factory.Create<List>(IWorkflowDefinitionStore.Mock().Object, IWorkflowDefinitionLinker.Mock().Object, Array.Empty<IWorkflowDefinitionFilterProvider>())).IsNotNull();
    }
}
