using Flamme.Common.Models;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Exceptions;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Services;
using Flamme.Workflows.Management.UnitTests.Helpers;
using Flamme.Workflows.Models;
using Microsoft.Extensions.Logging;
using TUnit.Mocks;

namespace Flamme.Workflows.Management.UnitTests.Services;

public class WorkflowDefinitionServiceTests
{
    private readonly IWorkflowDefinitionStore _workflowDefinitionStore = IWorkflowDefinitionStore.Mock().Object;
    private readonly IWorkflowGraphBuilder _workflowGraphBuilder = IWorkflowGraphBuilder.Mock().Object;
    private readonly IMaterializerRegistry _materializerRegistry = IMaterializerRegistry.Mock().Object;
    private readonly ILogger<WorkflowDefinitionService> _logger = ILogger<WorkflowDefinitionService>.Mock().Object;

    [Test]
    public async Task MaterializeWorkflowAsync_WithValidMaterializer_ReturnsMaterializedGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "test-materializer");
        var (_, workflowGraph) = SetupMaterializerAndGraphBuilder(definition);

        var service = CreateService();

        // Act
        var result = await service.MaterializeWorkflowAsync(definition);

        // Assert
        await Assert.That(result).IsSameReferenceAs(workflowGraph);
    }

    [Test]
    public async Task MaterializeWorkflowAsync_WithMissingMaterializer_ThrowsException()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "missing-materializer");
        Mock.Get(_materializerRegistry).GetMaterializer("missing-materializer").Returns((IWorkflowMaterializer?)null);

        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsExactlyAsync<WorkflowMaterializerNotFoundException>(() => service.MaterializeWorkflowAsync(definition));
    }

    [Test]
    public async Task FindWorkflowDefinitionAsync_ByDefinitionIdAndVersionOptions_ReturnsDefinition()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        Mock.Get(_workflowDefinitionStore).FindAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns(definition);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowDefinitionAsync("def-1", VersionOptions.Published);

        // Assert
        await Assert.That(result).IsSameReferenceAs(definition);
        Mock.Get(_workflowDefinitionStore).FindAsync(
            Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowDefinitionAsync_ByDefinitionVersionId_ReturnsDefinition()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        definition.Id = "version-id-1";
        Mock.Get(_workflowDefinitionStore).FindAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns(definition);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowDefinitionAsync("version-id-1");

        // Assert
        await Assert.That(result).IsSameReferenceAs(definition);
        Mock.Get(_workflowDefinitionStore).FindAsync(
            Arg.Is<WorkflowDefinitionFilter>(f => f.Id == "version-id-1"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowDefinitionAsync_ByHandle_ReturnsDefinition()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        var handle = WorkflowDefinitionHandle.ByDefinitionId("def-1", VersionOptions.Latest);
        Mock.Get(_workflowDefinitionStore).FindAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns(definition);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowDefinitionAsync(handle);

        // Assert
        await Assert.That(result).IsSameReferenceAs(definition);
        Mock.Get(_workflowDefinitionStore).FindAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowDefinitionAsync_ByFilter_ReturnsDefinition()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        var filter = new WorkflowDefinitionFilter { DefinitionId = "def-1" };
        Mock.Get(_workflowDefinitionStore).FindAsync(filter, Arg.Any<CancellationToken>()).Returns(definition);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowDefinitionAsync(filter);

        // Assert
        await Assert.That(result).IsSameReferenceAs(definition);
        Mock.Get(_workflowDefinitionStore).FindAsync(filter, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowGraphAsync_ByDefinitionIdAndVersionOptions_WithAvailableMaterializer_ReturnsGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        var workflowGraph = SetupCompleteWorkflowGraphMocking(definition);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphAsync("def-1", VersionOptions.Published);

        // Assert
        await Assert.That(result).IsSameReferenceAs(workflowGraph);
    }

    [Test]
    public async Task FindWorkflowGraphAsync_ByDefinitionIdAndVersionOptions_WithUnavailableMaterializer_ReturnsNull()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        SetupCompleteWorkflowGraphMocking(definition, materializerAvailable: false);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphAsync("def-1", VersionOptions.Published);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task FindWorkflowGraphAsync_ByDefinitionIdAndVersionOptions_WhenDefinitionNotFound_ReturnsNull()
    {
        // Arrange
        Mock.Get(_workflowDefinitionStore).FindAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns((WorkflowDefinition?)null);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphAsync("def-1", VersionOptions.Published);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task FindWorkflowGraphAsync_ByDefinitionVersionId_ReturnsGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        definition.Id = "version-id-1";
        var workflowGraph = SetupCompleteWorkflowGraphMocking(definition);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphAsync("version-id-1");

        // Assert
        await Assert.That(result).IsSameReferenceAs(workflowGraph);
    }

    [Test]
    public async Task FindWorkflowGraphAsync_ByHandle_ReturnsGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        var handle = WorkflowDefinitionHandle.ByDefinitionId("def-1", VersionOptions.Latest);
        var workflow = new Workflow { Identity = new("def-1", 1, "v1") };
        var workflowGraph = TestHelpers.CreateWorkflowGraph(workflow);
        Mock.Get(_workflowDefinitionStore).FindAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns(definition);
        Mock.Get(_materializerRegistry).IsMaterializerAvailable("materializer").Returns(true);

        var materializer = IWorkflowMaterializer.Mock().Object;
        Mock.Get(materializer).MaterializeAsync(definition, Arg.Any<CancellationToken>()).Returns(workflow);
        Mock.Get(_materializerRegistry).GetMaterializer("materializer").Returns(materializer);
        Mock.Get(_workflowGraphBuilder).BuildAsync(workflow, Arg.Any<CancellationToken>()).Returns(workflowGraph);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphAsync(handle);

        // Assert
        await Assert.That(result).IsSameReferenceAs(workflowGraph);
    }

    [Test]
    public async Task FindWorkflowGraphAsync_ByFilter_ReturnsGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        var filter = new WorkflowDefinitionFilter { DefinitionId = "def-1" };
        var workflow = new Workflow { Identity = new("def-1", 1, "v1") };
        var workflowGraph = TestHelpers.CreateWorkflowGraph(workflow);
        Mock.Get(_workflowDefinitionStore).FindAsync(filter, Arg.Any<CancellationToken>()).Returns(definition);
        Mock.Get(_materializerRegistry).IsMaterializerAvailable("materializer").Returns(true);

        var materializer = IWorkflowMaterializer.Mock().Object;
        Mock.Get(materializer).MaterializeAsync(definition, Arg.Any<CancellationToken>()).Returns(workflow);
        Mock.Get(_materializerRegistry).GetMaterializer("materializer").Returns(materializer);
        Mock.Get(_workflowGraphBuilder).BuildAsync(workflow, Arg.Any<CancellationToken>()).Returns(workflowGraph);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphAsync(filter);

        // Assert
        await Assert.That(result).IsSameReferenceAs(workflowGraph);
    }

    [Test]
    public async Task FindWorkflowGraphsAsync_WithMultipleDefinitions_ReturnsAllGraphs()
    {
        // Arrange
        var definition1 = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        var definition2 = TestHelpers.CreateWorkflowDefinition("def-2", "materializer");
        var definitions = new[] { definition1, definition2 };

        var workflow1 = new Workflow { Identity = new("def-1", 1, "v1") };
        var workflow2 = new Workflow { Identity = new("def-2", 1, "v1") };
        var graph1 = TestHelpers.CreateWorkflowGraph(workflow1);
        var graph2 = TestHelpers.CreateWorkflowGraph(workflow2);

        var filter = new WorkflowDefinitionFilter();
        Mock.Get(_workflowDefinitionStore).FindManyAsync(filter, Arg.Any<CancellationToken>()).Returns(definitions);

        var materializer = IWorkflowMaterializer.Mock().Object;
        Mock.Get(materializer).MaterializeAsync(definition1, Arg.Any<CancellationToken>()).Returns(workflow1);
        Mock.Get(materializer).MaterializeAsync(definition2, Arg.Any<CancellationToken>()).Returns(workflow2);
        Mock.Get(_materializerRegistry).GetMaterializer("materializer").Returns(materializer);
        Mock.Get(_workflowGraphBuilder).BuildAsync(workflow1, Arg.Any<CancellationToken>()).Returns(graph1);
        Mock.Get(_workflowGraphBuilder).BuildAsync(workflow2, Arg.Any<CancellationToken>()).Returns(graph2);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphsAsync(filter);

        // Assert
        var graphs = result.ToList();
        await Assert.That(graphs.Count).IsEqualTo(2);
        await Assert.That(graphs).Contains(graph1);
        await Assert.That(graphs).Contains(graph2);
    }

    [Test]
    public async Task TryFindWorkflowGraphAsync_ByDefinitionIdAndVersionOptions_WithAvailableMaterializer_ReturnsSuccessResult()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        var workflow = new Workflow { Identity = new("def-1", 1, "v1") };
        var workflowGraph = TestHelpers.CreateWorkflowGraph(workflow);
        Mock.Get(_workflowDefinitionStore).FindAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns(definition);
        Mock.Get(_materializerRegistry).IsMaterializerAvailable("materializer").Returns(true);

        var materializer = IWorkflowMaterializer.Mock().Object;
        Mock.Get(materializer).MaterializeAsync(definition, Arg.Any<CancellationToken>()).Returns(workflow);
        Mock.Get(_materializerRegistry).GetMaterializer("materializer").Returns(materializer);
        Mock.Get(_workflowGraphBuilder).BuildAsync(workflow, Arg.Any<CancellationToken>()).Returns(workflowGraph);

        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphAsync("def-1", VersionOptions.Published);

        // Assert
        var nonNullResult = await Assert.That(result).IsNotNull();
        await Assert.That(nonNullResult.WorkflowDefinition).IsSameReferenceAs(definition);
        await Assert.That(nonNullResult.WorkflowGraph).IsSameReferenceAs(workflowGraph);
    }

    [Test]
    public async Task TryFindWorkflowGraphAsync_ByDefinitionIdAndVersionOptions_WithUnavailableMaterializer_ReturnsResultWithNullGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        SetupCompleteWorkflowGraphMocking(definition, materializerAvailable: false);

        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphAsync("def-1", VersionOptions.Published);

        // Assert
        var nonNullResult = await Assert.That(result).IsNotNull();
        await Assert.That(nonNullResult.WorkflowDefinition).IsSameReferenceAs(definition);
        await Assert.That(nonNullResult.WorkflowGraph).IsNull();
    }

    [Test]
    public async Task TryFindWorkflowGraphAsync_ByDefinitionIdAndVersionOptions_WhenDefinitionNotFound_ReturnsEmptyResult()
    {
        // Arrange
        Mock.Get(_workflowDefinitionStore).FindAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns((WorkflowDefinition?)null);

        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphAsync("def-1", VersionOptions.Published);

        // Assert
        var nonNullResult = await Assert.That(result).IsNotNull();
        await Assert.That(nonNullResult.WorkflowDefinition).IsNull();
        await Assert.That(nonNullResult.WorkflowGraph).IsNull();
    }

    [Test]
    public async Task TryFindWorkflowGraphAsync_ByDefinitionVersionId_ReturnsResult()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        definition.Id = "version-id-1";
        var workflowGraph = SetupCompleteWorkflowGraphMocking(definition);

        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphAsync("version-id-1");

        // Assert
        var nonNullResult = await Assert.That(result).IsNotNull();
        await Assert.That(nonNullResult.WorkflowDefinition).IsSameReferenceAs(definition);
        await Assert.That(nonNullResult.WorkflowGraph).IsSameReferenceAs(workflowGraph);
    }

    [Test]
    public async Task TryFindWorkflowGraphAsync_ByHandle_ReturnsResult()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        var handle = WorkflowDefinitionHandle.ByDefinitionId("def-1", VersionOptions.Latest);
        var workflow = new Workflow { Identity = new("def-1", 1, "v1") };
        var workflowGraph = TestHelpers.CreateWorkflowGraph(workflow);
        Mock.Get(_workflowDefinitionStore).FindAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns(definition);
        Mock.Get(_materializerRegistry).IsMaterializerAvailable("materializer").Returns(true);

        var materializer = IWorkflowMaterializer.Mock().Object;
        Mock.Get(materializer).MaterializeAsync(definition, Arg.Any<CancellationToken>()).Returns(workflow);
        Mock.Get(_materializerRegistry).GetMaterializer("materializer").Returns(materializer);
        Mock.Get(_workflowGraphBuilder).BuildAsync(workflow, Arg.Any<CancellationToken>()).Returns(workflowGraph);

        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphAsync(handle);

        // Assert
        var nonNullResult = await Assert.That(result).IsNotNull();
        await Assert.That(nonNullResult.WorkflowDefinition).IsSameReferenceAs(definition);
        await Assert.That(nonNullResult.WorkflowGraph).IsSameReferenceAs(workflowGraph);
    }

    [Test]
    public async Task TryFindWorkflowGraphAsync_ByFilter_ReturnsResult()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        var filter = new WorkflowDefinitionFilter { DefinitionId = "def-1" };
        var workflow = new Workflow { Identity = new("def-1", 1, "v1") };
        var workflowGraph = TestHelpers.CreateWorkflowGraph(workflow);
        Mock.Get(_workflowDefinitionStore).FindAsync(filter, Arg.Any<CancellationToken>()).Returns(definition);
        Mock.Get(_materializerRegistry).IsMaterializerAvailable("materializer").Returns(true);

        var materializer = IWorkflowMaterializer.Mock().Object;
        Mock.Get(materializer).MaterializeAsync(definition, Arg.Any<CancellationToken>()).Returns(workflow);
        Mock.Get(_materializerRegistry).GetMaterializer("materializer").Returns(materializer);
        Mock.Get(_workflowGraphBuilder).BuildAsync(workflow, Arg.Any<CancellationToken>()).Returns(workflowGraph);

        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphAsync(filter);

        // Assert
        var nonNullResult = await Assert.That(result).IsNotNull();
        await Assert.That(nonNullResult.WorkflowDefinition).IsSameReferenceAs(definition);
        await Assert.That(nonNullResult.WorkflowGraph).IsSameReferenceAs(workflowGraph);
    }

    [Test]
    public async Task TryFindWorkflowGraphsAsync_WithMultipleDefinitions_ReturnsAllResults()
    {
        // Arrange
        var definition1 = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        var definition2 = TestHelpers.CreateWorkflowDefinition("def-2", "unavailable-materializer");
        var definitions = new[] { definition1, definition2 };

        var workflow1 = new Workflow { Identity = new("def-1", 1, "v1") };
        var graph1 = TestHelpers.CreateWorkflowGraph(workflow1);

        var filter = new WorkflowDefinitionFilter();
        Mock.Get(_workflowDefinitionStore).FindManyAsync(filter, Arg.Any<CancellationToken>()).Returns(definitions);
        Mock.Get(_materializerRegistry).IsMaterializerAvailable("materializer").Returns(true);
        Mock.Get(_materializerRegistry).IsMaterializerAvailable("unavailable-materializer").Returns(false);

        var materializer = IWorkflowMaterializer.Mock().Object;
        Mock.Get(materializer).MaterializeAsync(definition1, Arg.Any<CancellationToken>()).Returns(workflow1);
        Mock.Get(_materializerRegistry).GetMaterializer("materializer").Returns(materializer);
        Mock.Get(_workflowGraphBuilder).BuildAsync(workflow1, Arg.Any<CancellationToken>()).Returns(graph1);

        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphsAsync(filter);

        // Assert
        var results = result.ToList();
        await Assert.That(results.Count).IsEqualTo(2);

        var result1 = results.First(r => r.WorkflowDefinition?.DefinitionId == "def-1");
        await Assert.That(result1.WorkflowDefinition).IsSameReferenceAs(definition1);
        await Assert.That(result1.WorkflowGraph).IsSameReferenceAs(graph1);

        var result2 = results.First(r => r.WorkflowDefinition?.DefinitionId == "def-2");
        await Assert.That(result2.WorkflowDefinition).IsSameReferenceAs(definition2);
        await Assert.That(result2.WorkflowGraph).IsNull();
    }

    private WorkflowDefinitionService CreateService()
    {
        return new(
            _workflowDefinitionStore,
            _workflowGraphBuilder,
            _materializerRegistry,
            _logger);
    }

    /// <summary>
    /// Sets up a materializer and graph builder for the given definition.
    /// Returns the workflow and workflow graph that were configured.
    /// </summary>
    private (Workflow, WorkflowGraph) SetupMaterializerAndGraphBuilder(WorkflowDefinition definition)
    {
        var workflow = new Workflow { Identity = new(definition.DefinitionId, 1, "v1") };
        var workflowGraph = TestHelpers.CreateWorkflowGraph(workflow);

        var materializer = IWorkflowMaterializer.Mock().Object;
        Mock.Get(materializer).MaterializeAsync(definition, Arg.Any<CancellationToken>()).Returns(workflow);
        Mock.Get(_materializerRegistry).GetMaterializer(definition.MaterializerName).Returns(materializer);
        Mock.Get(_workflowGraphBuilder).BuildAsync(workflow, Arg.Any<CancellationToken>()).Returns(workflowGraph);

        return (workflow, workflowGraph);
    }

    /// <summary>
    /// Sets up complete mocking chain for workflow graph retrieval including definition store, materializer, and graph builder.
    /// Returns the workflow graph that was configured.
    /// </summary>
    private WorkflowGraph SetupCompleteWorkflowGraphMocking(
        WorkflowDefinition definition,
        bool materializerAvailable = true,
        WorkflowDefinitionFilter? filter = null)
    {
        var workflow = new Workflow { Identity = new(definition.DefinitionId, 1, "v1") };
        var workflowGraph = TestHelpers.CreateWorkflowGraph(workflow);

        if (filter != null)
        {
            Mock.Get(_workflowDefinitionStore).FindAsync(filter, Arg.Any<CancellationToken>()).Returns(definition);
        }
        else
        {
            Mock.Get(_workflowDefinitionStore).FindAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                            .Returns(definition);
        }
        Mock.Get(_materializerRegistry).IsMaterializerAvailable(definition.MaterializerName).Returns(materializerAvailable);

        if (materializerAvailable)
        {
            var materializer = IWorkflowMaterializer.Mock().Object;
            Mock.Get(materializer).MaterializeAsync(definition, Arg.Any<CancellationToken>()).Returns(workflow);
            Mock.Get(_materializerRegistry).GetMaterializer(definition.MaterializerName).Returns(materializer);
            Mock.Get(_workflowGraphBuilder).BuildAsync(workflow, Arg.Any<CancellationToken>()).Returns(workflowGraph);
        }

        return workflowGraph;
    }
}
