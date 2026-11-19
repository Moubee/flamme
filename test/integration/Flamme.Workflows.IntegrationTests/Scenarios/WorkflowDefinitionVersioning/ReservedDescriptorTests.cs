using Flamme.Common.Multitenancy;
using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.IntegrationTests.Scenarios.WorkflowDefinitionVersioning;

public class ReservedDescriptorTests
{
    [Test]
    [Arguments("Console.WriteLine", false)]
    [Arguments("Console.WriteLine", true)]
    [Arguments("ControlFlow.If", false)]
    [Arguments("ControlFlow.If", true)]
    public async Task NativeDefinitionCannotPersistAClaimOnAnOccupiedBinding(string name, bool publish)
    {
        var services = new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput).Build();
        try
        {
            await services.PopulateRegistriesAsync();
            var store = services.GetRequiredService<IWorkflowDefinitionStore>();
            var publisher = services.GetRequiredService<IWorkflowDefinitionPublisher>();
            var definition = await publisher.NewAsync();
            // Curated primitives are reserved even for tenant overlays. Other static bindings
            // occupy their registration scope and can also conflict with a shared definition.
            definition.TenantId = name == "Console.WriteLine" ? Tenant.DefaultTenantId : Tenant.AgnosticTenantId;
            definition.Name = name;
            definition.Options.UsableAsActivity = true;
            if (publish)
            {
                var result = await publisher.PublishAsync(definition);
                await Assert.That(result.Succeeded).IsFalse();
                await Assert.That(result.ValidationErrors.Single().Message).Contains(name);
            }
            else
            {
                var error = await Assert.ThrowsExactlyAsync<WorkflowDefinitionValidationException>(() => publisher.SaveDraftAsync(definition));
                await Assert.That(error!.Message).Contains(name);
            }

            await Assert.That(await store.FindAsync(new() { Id = definition.Id, TenantAgnostic = true })).IsNull();
        }
        finally
        {
            await TestResourceDisposal.DisposeAsync(services);
        }
    }
}
