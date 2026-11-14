using Elsa.UnitTests.Shared;
using Elsa.Workflows.Api.Endpoints.WorkflowDefinitions.List;
using Elsa.Workflows.Api.UnitTests.Endpoints.Descriptors;
using Elsa.Workflows.Api.UnitTests.Endpoints.Features;
using Elsa.Workflows.Management;
using FastEndpoints;

namespace Elsa.Workflows.Api.UnitTests.Endpoints;

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
    public async Task FactoryCreatesEndpointsAfterAnEndpointHostIsDisposed(Type hostTestClass)
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

        await Assert.That(Factory.Create<List>(Mock.Of<IWorkflowDefinitionStore>().Object, Mock.Of<IWorkflowDefinitionLinker>().Object, Array.Empty<IWorkflowDefinitionFilterProvider>())).IsNotNull();
    }
}
