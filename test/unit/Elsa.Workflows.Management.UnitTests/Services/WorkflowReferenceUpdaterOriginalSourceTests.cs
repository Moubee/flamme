using Elsa.Common.Models;
using Elsa.Dsl.ElsaScript.Contracts;
using Elsa.Dsl.ElsaScript.Materializers;
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
    private const string ElsaScriptSource = "workflow Consumer { WriteLine(\"from-elsascript\"); }";

    [Test]
    public async Task UpdateWorkflowReferencesAsync_SkipsElsaScriptConsumerAndUpdatesJsonConsumer()
    {
        var target = CreateTargetDefinition();
        var elsaScriptConsumer = CreateElsaScriptConsumer();
        var jsonConsumer = CreateJsonConsumer();
        var elsaScriptGraph = CreateConsumerGraph(elsaScriptConsumer, target, "elsa-consumer-workflow");
        var jsonGraph = CreateConsumerGraph(jsonConsumer, target, "json-consumer-workflow");
        var logger = new CollectingLogger();
        var publisher = Mock.Of<IWorkflowDefinitionPublisher>().Object;
        var activityRegistry = Mock.Of<IActivityRegistry>().Object;
        var updater = CreateUpdater(target, [elsaScriptConsumer, jsonConsumer], [elsaScriptGraph, jsonGraph], publisher, logger, activityRegistry, out _);

        var result = await updater.UpdateWorkflowReferencesAsync(target);

        var updated = await Assert.That(result.UpdatedWorkflows).HasSingleItem();
        await Assert.That(updated.DefinitionId).IsEqualTo(jsonConsumer.DefinitionId);
        await Assert.That(updated.StringData).IsEqualTo("serialized-root");
        await Assert.That(updated.OriginalSource).IsNull();
        await Assert.That(updated.MaterializerName).IsEqualTo(JsonWorkflowMaterializer.MaterializerName);

        await Assert.That(elsaScriptConsumer.OriginalSource).IsEqualTo(ElsaScriptSource);
        await Assert.That(elsaScriptConsumer.StringData).IsNull();
        await Assert.That(elsaScriptConsumer.MaterializerName).IsEqualTo("ElsaScript");
        await Assert.That(elsaScriptConsumer.Id).IsEqualTo("consumer-elsascript-v1");

        var compiler = Mock.Of<IElsaScriptCompiler>().Object;
        Mock.Get(compiler).CompileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(new Workflow());
        var materializer = new ElsaScriptWorkflowMaterializer(compiler);
        await materializer.MaterializeAsync(elsaScriptConsumer, CancellationToken.None);
        Mock.Get(compiler).CompileAsync(ElsaScriptSource, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));

        Mock.Get(publisher).GetDraftAsync(elsaScriptConsumer.DefinitionId, Arg.Any<VersionOptions>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(publisher).SaveDraftAsync(elsaScriptConsumer, Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(publisher).PublishAsync(elsaScriptConsumer, Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(publisher).SaveDraftAsync(jsonConsumer, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        await Assert.That(logger.Messages).Contains(message =>
            message.Contains(elsaScriptConsumer.DefinitionId, StringComparison.Ordinal)
            && message.Contains(target.DefinitionId, StringComparison.Ordinal)
            && message.Contains(target.Version.ToString(), StringComparison.Ordinal)
            && message.Contains("must be updated manually", StringComparison.Ordinal));
    }

    [Test]
    public async Task UpdateWorkflowReferencesAsync_WhenPublishedElsaScriptConsumerIsOutdated_DoesNotCreateOrRegisterDraft()
    {
        var target = CreateTargetDefinition();
        var elsaScriptConsumer = CreateElsaScriptConsumer(isPublished: true);
        var consumerGraph = CreateConsumerGraph(elsaScriptConsumer, target, "elsa-consumer-workflow");
        var logger = new CollectingLogger();
        var publisher = Mock.Of<IWorkflowDefinitionPublisher>().Object;
        var activityRegistry = Mock.Of<IActivityRegistry>().Object;
        var updater = CreateUpdater(target, [elsaScriptConsumer], [consumerGraph], publisher, logger, activityRegistry, out var store);

        var result = await updater.UpdateWorkflowReferencesAsync(target);

        await Assert.That(result.UpdatedWorkflows).IsEmpty();
        await Assert.That(elsaScriptConsumer.OriginalSource).IsEqualTo(ElsaScriptSource);
        await Assert.That(elsaScriptConsumer.IsPublished).IsTrue();
        await Assert.That(elsaScriptConsumer.Version).IsEqualTo(1);

        var stored = await store.FindAsync(new WorkflowDefinitionFilter
        {
            DefinitionId = elsaScriptConsumer.DefinitionId,
            VersionOptions = VersionOptions.Latest
        }, CancellationToken.None);
        await Assert.That(stored).IsSameReferenceAs(elsaScriptConsumer);
        await Assert.That(stored!.IsPublished).IsTrue();
        await Assert.That(stored.Version).IsEqualTo(1);

        Mock.Get(publisher).GetDraftAsync(Arg.Any<string>(), Arg.Any<VersionOptions>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(publisher).SaveDraftAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(publisher).PublishAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(activityRegistry).Add(Arg.Any<Type>(), Arg.Any<ActivityDescriptor>()).WasNeverCalled();
        await Assert.That(logger.Messages).Contains(message =>
            message.Contains(elsaScriptConsumer.DefinitionId, StringComparison.Ordinal)
            && message.Contains(target.DefinitionId, StringComparison.Ordinal));

        var compiler = Mock.Of<IElsaScriptCompiler>().Object;
        Mock.Get(compiler).CompileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(new Workflow());
        await new ElsaScriptWorkflowMaterializer(compiler).MaterializeAsync(elsaScriptConsumer, CancellationToken.None);
        Mock.Get(compiler).CompileAsync(ElsaScriptSource, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
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
        var workflowDefinitionService = Mock.Of<IWorkflowDefinitionService>().Object;
        store = Mock.Of<IWorkflowDefinitionStore>().Object;
        var graphBuilder = Mock.Of<IWorkflowReferenceGraphBuilder>().Object;
        var serializer = Mock.Of<IApiSerializer>().Object;
        var consumersById = consumers.ToDictionary(c => c.DefinitionId);
        activityRegistry ??= Mock.Of<IActivityRegistry>().Object;
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

    private static WorkflowDefinition CreateElsaScriptConsumer(bool isPublished = false)
    {
        return new()
        {
            Id = "consumer-elsascript-v1",
            DefinitionId = "consumer-elsascript",
            Name = "ElsaScript Consumer",
            Version = 1,
            IsPublished = isPublished,
            IsLatest = true,
            MaterializerName = "ElsaScript",
            OriginalSource = ElsaScriptSource,
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
