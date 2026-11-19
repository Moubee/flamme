using Elsa.Common.Models;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Activities.WorkflowDefinitionActivity;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Management.Materializers;
using Elsa.Workflows.Management.Models;
using Elsa.Workflows.Management.Services;
using Elsa.Workflows.Models;
using Microsoft.Extensions.Logging;
using TUnit.Mocks;

namespace Elsa.Workflows.Management.UnitTests.Services;

public class WorkflowReferenceUpdaterOriginalSourceTests
{
    private const string CustomSource = "custom workflow source";

    [Test]
    public async Task UpdateWorkflowReferencesAsync_SkipsSourceOnlyConsumerAndUpdatesJsonConsumer()
    {
        var target = CreateTargetDefinition();
        var sourceOnlyConsumer = CreateSourceOnlyConsumer();
        var jsonConsumer = CreateJsonConsumer();
        var sourceOnlyGraph = CreateConsumerGraph(sourceOnlyConsumer, target, "source-consumer-workflow");
        var jsonGraph = CreateConsumerGraph(jsonConsumer, target, "json-consumer-workflow");
        var logger = new CollectingLogger();
        var publisher = IWorkflowDefinitionPublisher.Mock().Object;
        var activityRegistry = IActivityRegistry.Mock().Object;
        var updater = CreateUpdater(target, [sourceOnlyConsumer, jsonConsumer], [sourceOnlyGraph, jsonGraph], publisher, logger, activityRegistry, out _);

        var result = await updater.UpdateWorkflowReferencesAsync(target);

        var updated = await Assert.That(result.UpdatedWorkflows).HasSingleItem();
        await Assert.That(updated.DefinitionId).IsEqualTo(jsonConsumer.DefinitionId);
        await Assert.That(updated.StringData).IsEqualTo("serialized-root");
        await Assert.That(updated.OriginalSource).IsNull();
        await Assert.That(updated.MaterializerName).IsEqualTo(JsonWorkflowMaterializer.MaterializerName);

        await Assert.That(sourceOnlyConsumer.OriginalSource).IsEqualTo(CustomSource);
        await Assert.That(sourceOnlyConsumer.StringData).IsNull();
        await Assert.That(sourceOnlyConsumer.MaterializerName).IsEqualTo("CustomSource");
        await Assert.That(sourceOnlyConsumer.Id).IsEqualTo("consumer-source-v1");

        Mock.Get(publisher).GetDraftAsync(sourceOnlyConsumer.DefinitionId, Arg.Any<VersionOptions>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(publisher).SaveDraftAsync(sourceOnlyConsumer, Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(publisher).PublishAsync(sourceOnlyConsumer, Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(publisher).SaveDraftAsync(jsonConsumer, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        await Assert.That(logger.Messages).Contains(message =>
            message.Contains(sourceOnlyConsumer.DefinitionId, StringComparison.Ordinal)
            && message.Contains(target.DefinitionId, StringComparison.Ordinal)
            && message.Contains(target.Version.ToString(), StringComparison.Ordinal)
            && message.Contains("must be updated manually", StringComparison.Ordinal));
    }

    [Test]
    public async Task UpdateWorkflowReferencesAsync_WhenPublishedSourceOnlyConsumerIsOutdated_DoesNotCreateOrRegisterDraft()
    {
        var target = CreateTargetDefinition();
        var sourceOnlyConsumer = CreateSourceOnlyConsumer(isPublished: true);
        var consumerGraph = CreateConsumerGraph(sourceOnlyConsumer, target, "source-consumer-workflow");
        var logger = new CollectingLogger();
        var publisher = IWorkflowDefinitionPublisher.Mock().Object;
        var activityRegistry = IActivityRegistry.Mock().Object;
        var updater = CreateUpdater(target, [sourceOnlyConsumer], [consumerGraph], publisher, logger, activityRegistry, out var store);

        var result = await updater.UpdateWorkflowReferencesAsync(target);

        await Assert.That(result.UpdatedWorkflows).IsEmpty();
        await Assert.That(sourceOnlyConsumer.OriginalSource).IsEqualTo(CustomSource);
        await Assert.That(sourceOnlyConsumer.IsPublished).IsTrue();
        await Assert.That(sourceOnlyConsumer.Version).IsEqualTo(1);

        var stored = await store.FindAsync(new WorkflowDefinitionFilter
        {
            DefinitionId = sourceOnlyConsumer.DefinitionId,
            VersionOptions = VersionOptions.Latest
        }, CancellationToken.None);
        await Assert.That(stored).IsSameReferenceAs(sourceOnlyConsumer);
        await Assert.That(stored!.IsPublished).IsTrue();
        await Assert.That(stored.Version).IsEqualTo(1);

        Mock.Get(publisher).GetDraftAsync(Arg.Any<string>(), Arg.Any<VersionOptions>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(publisher).SaveDraftAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(publisher).PublishAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(activityRegistry).Add(Arg.Any<Type>(), Arg.Any<ActivityDescriptor>()).WasNeverCalled();
        await Assert.That(logger.Messages).Contains(message =>
            message.Contains(sourceOnlyConsumer.DefinitionId, StringComparison.Ordinal)
            && message.Contains(target.DefinitionId, StringComparison.Ordinal));
    }

    private static WorkflowReferenceUpdater CreateUpdater(
        WorkflowDefinition target,
        IReadOnlyCollection<WorkflowDefinition> consumers,
        IReadOnlyCollection<WorkflowGraph> consumerGraphs,
        IWorkflowDefinitionPublisher publisher,
        ILogger<WorkflowReferenceUpdater> logger,
        IActivityRegistry? activityRegistry,
        out IWorkflowDefinitionStore store)
    {
        var workflowDefinitionService = IWorkflowDefinitionService.Mock().Object;
        store = IWorkflowDefinitionStore.Mock().Object;
        var graphBuilder = IWorkflowReferenceGraphBuilder.Mock().Object;
        var serializer = IApiSerializer.Mock().Object;
        var consumersById = consumers.ToDictionary(c => c.DefinitionId);
        activityRegistry ??= IActivityRegistry.Mock().Object;
        Mock.Get(graphBuilder).BuildGraphAsync(target.DefinitionId, Arg.Any<CancellationToken>())
                    .Returns(new WorkflowReferenceGraph(
                        [target.DefinitionId],
                        consumers.Select(c => new WorkflowReferenceEdge(c.DefinitionId, target.DefinitionId)).ToList()));
        Mock.Get(workflowDefinitionService).FindWorkflowGraphsAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns(consumerGraphs);

        foreach (var (consumer, graph) in consumers.Zip(consumerGraphs))
        {
            Mock.Get(workflowDefinitionService).MaterializeWorkflowAsync(consumer, Arg.Any<CancellationToken>())
                            .Returns(graph);
            Mock.Get(publisher).GetDraftAsync(consumer.DefinitionId, VersionOptions.Latest, Arg.Any<CancellationToken>())
                            .Returns(consumer);
        }
        Mock.Get(store).FindManyAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns([target]);
        Mock.Get(store).FindAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.Workflows.Management.Filters.WorkflowDefinitionFilter __arg0, global::System.Threading.CancellationToken __arg1) =>
                    {
                        var filter = __arg0;
                        return filter.DefinitionId != null && consumersById.TryGetValue(filter.DefinitionId, out var consumer)
                            ? consumer
                            : consumers.First();
                    });
        Mock.Get(publisher).SaveDraftAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.Workflows.Management.Entities.WorkflowDefinition __arg0, global::System.Threading.CancellationToken __arg1) => __arg0);
        Mock.Get(serializer).Serialize(Arg.Any<object>()).Returns("serialized-root");

        return new(
            publisher,
            workflowDefinitionService,
            store,
            graphBuilder,
            new WorkflowDefinitionActivityDescriptorFactory(),
            activityRegistry,
            serializer,
            logger);
    }

    private static WorkflowDefinition CreateTargetDefinition()
    {
        return new()
        {
            Id = "target-v2",
            DefinitionId = "target-def",
            Name = "Target",
            Version = 2,
            IsPublished = true,
            IsLatest = true,
            MaterializerName = JsonWorkflowMaterializer.MaterializerName,
            Options = new WorkflowOptions
            {
                UsableAsActivity = true,
                AutoUpdateConsumingWorkflows = true
            }
        };
    }

    private static WorkflowDefinition CreateSourceOnlyConsumer(bool isPublished = false)
    {
        return new()
        {
            Id = "consumer-source-v1",
            DefinitionId = "consumer-source",
            Name = "Source-only Consumer",
            Version = 1,
            IsPublished = isPublished,
            IsLatest = true,
            MaterializerName = "CustomSource",
            OriginalSource = CustomSource,
            StringData = null
        };
    }

    private static WorkflowDefinition CreateJsonConsumer()
    {
        return new()
        {
            Id = "consumer-json-v1",
            DefinitionId = "consumer-json",
            Name = "Json Consumer",
            Version = 1,
            IsPublished = false,
            IsLatest = true,
            MaterializerName = JsonWorkflowMaterializer.MaterializerName,
            OriginalSource = "FILE_JSON",
            StringData = "OLD_JSON"
        };
    }

    private static WorkflowGraph CreateConsumerGraph(WorkflowDefinition consumer, WorkflowDefinition target, string workflowId)
    {
        var referencedActivity = new WorkflowDefinitionActivity
        {
            Id = $"{workflowId}-ref",
            WorkflowDefinitionId = target.DefinitionId,
            WorkflowDefinitionVersionId = "target-v1",
            Version = 1
        };
        var workflow = new Workflow
        {
            Id = workflowId,
            Identity = new(consumer.DefinitionId, consumer.Version, consumer.Id),
            Publication = new(consumer.IsLatest, consumer.IsPublished),
            Root = referencedActivity
        };
        var rootNode = new ActivityNode(workflow, "Root");
        var childNode = new ActivityNode(referencedActivity, "Root");
        rootNode.AddChild(childNode);

        return new(workflow, rootNode, [rootNode, childNode]);
    }

    private sealed class CollectingLogger : ILogger<WorkflowReferenceUpdater>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
