using Flamme.Common.Models;
using Flamme.KeyValues.Features;
using Flamme.Mediator.HostedServices;
using Flamme.Testing.Shared;
using Flamme.Workflows.Helpers;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Models;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Activities;
using Flamme.Workflows.Runtime.Messages;
using Flamme.Workflows.Runtime.Models;
using Flamme.Workflows.Runtime.Options;
using Flamme.Workflows.Runtime.Stimuli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flamme.Workflows.IntegrationTests.Scenarios.PublishEventOutbox;

public class Tests
{
    private readonly TextWriter _testOutputHelper;

    public Tests()
    {
        _testOutputHelper = TestContext.Current!.Output.StandardOutput;
    }

    [Test]
    [DisplayName("With outbox on, in-workflow PublishEvent survives a crash between parent commit and delivery")]
    public async Task PublishEvent_WithOutboxOn_SurvivesCrashBetweenCommitAndDelivery()
    {
        await using var services = CreateServices(useTransactionalOutbox: true, processOutboxAfterCommit: false);
        await services.PopulateRegistriesAsync();

        var parentResponse = await RunPublisherAsync(services);
        await Assert.That(parentResponse.Status).IsEqualTo(WorkflowStatus.Finished);

        var outboxItems = (await services.GetRequiredService<IWorkflowDispatchOutboxStore>().FindManyAsync()).ToList();
        var outboxItem = (await Assert.That(outboxItems).HasSingleItem());
        await Assert.That(outboxItem.Kind).IsEqualTo(WorkflowDispatchOutboxItemKind.TriggerWorkflows);
        await Assert.That(outboxItem.OwnerWorkflowInstanceId).IsEqualTo(parentResponse.WorkflowInstanceId);
        var command = await Assert.That(outboxItem.TriggerWorkflowsCommand).IsNotNull();
        await Assert.That(command.ActivityTypeName).IsEqualTo(ActivityTypeNameHelper.GenerateTypeName<Event>());
        await Assert.That((await Assert.That(command.Stimulus).IsTypeOf<EventStimulus>())!.EventName).IsEqualTo(PublishOrderShippedEventWorkflow.EventName);

        var parentInstance = await services.GetRequiredService<IWorkflowInstanceStore>().FindAsync(new WorkflowInstanceFilter
        {
            Id = parentResponse.WorkflowInstanceId
        });
        await Assert.That(parentInstance).IsNotNull();
        await Assert.That(parentInstance.WorkflowState.HasWorkflowDispatchOutboxItem(outboxItem.Id)).IsTrue();
        await Assert.That(await FindConsumerInstancesAsync(services)).IsEmpty();

        await services.GetRequiredService<IWorkflowDispatchOutboxProcessor>().ProcessAsync();

        await Assert.That(await services.GetRequiredService<IWorkflowDispatchOutboxStore>().FindManyAsync()).IsEmpty();

        var commandProcessor = services.GetServices<IHostedService>().OfType<BackgroundCommandSenderHostedService>().Single();
        await commandProcessor.StartAsync(CancellationToken.None);

        try
        {
            await WaitUntilConsumerInstanceExistsAsync(services);
            await Assert.That(await FindConsumerInstancesAsync(services)).HasSingleItem();
        }
        finally
        {
            await commandProcessor.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    [DisplayName("With outbox off, in-workflow PublishEvent is not written to the outbox")]
    public async Task PublishEvent_WithOutboxOff_DoesNotWriteToOutbox()
    {
        await using var services = CreateServices(useTransactionalOutbox: false, processOutboxAfterCommit: false);
        await services.PopulateRegistriesAsync();

        var parentResponse = await RunPublisherAsync(services);
        await Assert.That(parentResponse.Status).IsEqualTo(WorkflowStatus.Finished);
        await Assert.That(await services.GetRequiredService<IWorkflowDispatchOutboxStore>().FindManyAsync()).IsEmpty();
    }

    private ServiceProvider CreateServices(bool useTransactionalOutbox, bool processOutboxAfterCommit)
    {
        return (ServiceProvider)new TestApplicationBuilder(_testOutputHelper)
            .AddWorkflow<PublishOrderShippedEventWorkflow>()
            .AddWorkflow<ConsumeOrderShippedEventWorkflow>()
            .ConfigureFlamme(flamme => flamme.Configure<KeyValueFeature>())
            .ConfigureServices(services => services.Configure<WorkflowDispatcherOptions>(options =>
            {
                options.UseTransactionalOutbox = useTransactionalOutbox;
                options.ProcessOutboxAfterCommit = processOutboxAfterCommit;
            }))
            .Build();
    }

    private static async Task<RunWorkflowInstanceResponse> RunPublisherAsync(IServiceProvider services)
    {
        var workflowRuntime = services.GetRequiredService<IWorkflowRuntime>();
        var workflowClient = await workflowRuntime.CreateClientAsync();
        return await workflowClient.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(PublishOrderShippedEventWorkflow), VersionOptions.Published)
        });
    }

    private static ValueTask<IEnumerable<WorkflowInstance>> FindConsumerInstancesAsync(IServiceProvider services)
    {
        return services.GetRequiredService<IWorkflowInstanceStore>().FindManyAsync(new WorkflowInstanceFilter
        {
            DefinitionId = nameof(ConsumeOrderShippedEventWorkflow)
        });
    }

    private static async Task WaitUntilConsumerInstanceExistsAsync(IServiceProvider services)
    {
        using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!timeoutTokenSource.IsCancellationRequested)
        {
            if ((await FindConsumerInstancesAsync(services)).Any())
                return;

            await Task.Delay(50);
        }

        Assert.Fail("ConsumeOrderShippedEventWorkflow was not created before the timeout elapsed.");
    }
}
