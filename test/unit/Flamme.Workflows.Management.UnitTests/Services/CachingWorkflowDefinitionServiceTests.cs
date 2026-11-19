using Flamme.Caching;
using Flamme.Caching.Options;
using Flamme.Common.Models;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Management.Services;
using Flamme.Workflows.Management.UnitTests.Helpers;
using Flamme.Workflows.Models;
using Microsoft.Extensions.Caching.Memory;
using TUnit.Mocks;

namespace Flamme.Workflows.Management.UnitTests.Services;

public class CachingWorkflowDefinitionServiceTests
{
    private const string DefaultDefinitionId = "def-1";
    private const string DefaultVersionId = "version-id-1";
    private const string DefaultMaterializerName = "materializer";

    private readonly IWorkflowDefinitionService _decoratedService = IWorkflowDefinitionService.Mock().Object;
    private readonly IWorkflowDefinitionCacheManager _cacheManager = IWorkflowDefinitionCacheManager.Mock().Object;
    private readonly IWorkflowDefinitionStore _workflowDefinitionStore = IWorkflowDefinitionStore.Mock().Object;
    private readonly IMaterializerRegistry _materializerRegistry = IMaterializerRegistry.Mock().Object;
    private readonly ICacheManager _cache = ICacheManager.Mock().Object;

    public CachingWorkflowDefinitionServiceTests()
    {
        SetupCacheManager();
    }

    [Test]
    public async Task MaterializeWorkflowAsync_DelegatesToDecoratedService()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition(DefaultDefinitionId, DefaultMaterializerName);
        var (_, workflowGraph) = CreateWorkflowAndGraph(DefaultDefinitionId);
        Mock.Get(_decoratedService).MaterializeWorkflowAsync(definition, Arg.Any<CancellationToken>()).Returns(workflowGraph);
        var service = CreateService();

        // Act
        var result = await service.MaterializeWorkflowAsync(definition);

        // Assert
        await Assert.That(result).IsSameReferenceAs(workflowGraph);
        Mock.Get(_decoratedService).MaterializeWorkflowAsync(definition, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowDefinitionAsync_ByDefinitionIdAndVersionOptions_CreatesCacheKey()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition(DefaultDefinitionId, DefaultMaterializerName);
        Mock.Get(_cacheManager).CreateWorkflowDefinitionVersionCacheKey(DefaultDefinitionId, VersionOptions.Published).Returns("cache-key");
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(DefaultDefinitionId, VersionOptions.Published, Arg.Any<CancellationToken>()).Returns(definition);
        var service = CreateService();

        // Act
        var result = await service.FindWorkflowDefinitionAsync(DefaultDefinitionId, VersionOptions.Published);

        // Assert
        await Assert.That(result).IsSameReferenceAs(definition);
        Mock.Get(_cacheManager).CreateWorkflowDefinitionVersionCacheKey(DefaultDefinitionId, VersionOptions.Published).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowDefinitionAsync_ByDefinitionVersionId_CreatesCacheKey()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition(DefaultDefinitionId, DefaultMaterializerName);
        definition.Id = DefaultVersionId;
        Mock.Get(_cacheManager).CreateWorkflowDefinitionVersionCacheKey(DefaultVersionId).Returns("cache-key");
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(DefaultVersionId, Arg.Any<CancellationToken>()).Returns(definition);
        var service = CreateService();

        // Act
        var result = await service.FindWorkflowDefinitionAsync(DefaultVersionId);

        // Assert
        await Assert.That(result).IsSameReferenceAs(definition);
        Mock.Get(_cacheManager).CreateWorkflowDefinitionVersionCacheKey(DefaultVersionId).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowDefinitionAsync_ByHandle_ConvertsTFilterAndDelegates()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        var handle = WorkflowDefinitionHandle.ByDefinitionId("def-1", VersionOptions.Latest);
        var cacheKey = "cache-key-filter";
        Mock.Get(_cacheManager).CreateWorkflowDefinitionFilterCacheKey(Arg.Any<WorkflowDefinitionFilter>()).Returns(cacheKey);
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns(definition);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowDefinitionAsync(handle);

        // Assert
        await Assert.That(result).IsSameReferenceAs(definition);
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowDefinitionAsync_ByFilter_CreatesCacheKey()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition("def-1", "materializer");
        var filter = new WorkflowDefinitionFilter { DefinitionId = "def-1" };
        var cacheKey = "cache-key-filter";
        Mock.Get(_cacheManager).CreateWorkflowDefinitionFilterCacheKey(filter).Returns(cacheKey);
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(filter, Arg.Any<CancellationToken>()).Returns(definition);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowDefinitionAsync(filter);

        // Assert
        await Assert.That(result).IsSameReferenceAs(definition);
        Mock.Get(_cacheManager).CreateWorkflowDefinitionFilterCacheKey(filter).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowGraphAsync_ByDefinitionIdAndVersionOptions_CreatesCacheKey()
    {
        // Arrange
        var (_, workflowGraph) = CreateWorkflowAndGraph("def-1");
        var cacheKey = "cache-key-graph";
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("def-1", VersionOptions.Published).Returns(cacheKey);
        Mock.Get(_decoratedService).FindWorkflowGraphAsync("def-1", VersionOptions.Published, Arg.Any<CancellationToken>())
                    .Returns(workflowGraph);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphAsync("def-1", VersionOptions.Published);

        // Assert
        await Assert.That(result).IsSameReferenceAs(workflowGraph);
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("def-1", VersionOptions.Published).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowGraphAsync_ByDefinitionVersionId_CreatesCacheKey()
    {
        // Arrange
        var (_, workflowGraph) = CreateWorkflowAndGraph("def-1");
        var cacheKey = "cache-key-graph-version";
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("version-id-1").Returns(cacheKey);
        Mock.Get(_decoratedService).FindWorkflowGraphAsync("version-id-1", Arg.Any<CancellationToken>()).Returns(workflowGraph);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphAsync("version-id-1");

        // Assert
        await Assert.That(result).IsSameReferenceAs(workflowGraph);
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("version-id-1").WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowGraphAsync_ByHandle_ConvertsTFilterAndResolvesDefinitionAndGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition(DefaultDefinitionId, DefaultMaterializerName);
        definition.Id = DefaultVersionId;
        var (_, workflowGraph) = CreateWorkflowAndGraph(DefaultDefinitionId);
        var handle = WorkflowDefinitionHandle.ByDefinitionId(DefaultDefinitionId, VersionOptions.Latest);
        Mock.Get(_cacheManager).CreateWorkflowDefinitionFilterCacheKey(Arg.Any<WorkflowDefinitionFilter>()).Returns("cache-key-def");
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey(DefaultVersionId).Returns("cache-key-graph");
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>()).Returns(definition);
        SetupDefinitionAndGraphMaterialization(definition, workflowGraph);
        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphAsync(handle);

        // Assert
        await Assert.That(result).IsSameReferenceAs(workflowGraph);
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowGraphAsync_ByFilter_ResolvesDefinitionFirstAndCachesGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition(DefaultDefinitionId, DefaultMaterializerName);
        definition.Id = DefaultVersionId;
        var (_, workflowGraph) = CreateWorkflowAndGraph(DefaultDefinitionId);
        var filter = new WorkflowDefinitionFilter { DefinitionId = DefaultDefinitionId };
        Mock.Get(_cacheManager).CreateWorkflowDefinitionFilterCacheKey(filter).Returns("cache-key-def");
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey(DefaultVersionId).Returns("cache-key-graph");
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(filter, Arg.Any<CancellationToken>()).Returns(definition);
        SetupDefinitionAndGraphMaterialization(definition, workflowGraph);
        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphAsync(filter);

        // Assert
        await Assert.That(result).IsSameReferenceAs(workflowGraph);
        Mock.Get(_cacheManager).CreateWorkflowDefinitionFilterCacheKey(filter).WasCalled(Times.Exactly(1));
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey(DefaultVersionId).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowGraphAsync_ByFilter_WithUnavailableMaterializer_ReturnsNull()
    {
        // Arrange
        const string unavailableMaterializer = "unavailable-materializer";
        var definition = TestHelpers.CreateWorkflowDefinition(DefaultDefinitionId, unavailableMaterializer);
        definition.Id = DefaultVersionId;
        var filter = new WorkflowDefinitionFilter { DefinitionId = DefaultDefinitionId };
        Mock.Get(_cacheManager).CreateWorkflowDefinitionFilterCacheKey(filter).Returns("cache-key");
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(filter, Arg.Any<CancellationToken>()).Returns(definition);
        Mock.Get(_materializerRegistry).IsMaterializerAvailable(unavailableMaterializer).Returns(false);
        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphAsync(filter);

        // Assert
        await Assert.That(result).IsNull();
        Mock.Get(_cacheManager).CreateWorkflowDefinitionFilterCacheKey(filter).WasCalled(Times.Exactly(1));
        Mock.Get(_decoratedService).MaterializeWorkflowAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task FindWorkflowGraphsAsync_WithMultipleDefinitions_CachesEachGraph()
    {
        // Arrange
        var definition1 = TestHelpers.CreateWorkflowDefinition("def-1", DefaultMaterializerName);
        definition1.Id = "id-1";
        var definition2 = TestHelpers.CreateWorkflowDefinition("def-2", DefaultMaterializerName);
        definition2.Id = "id-2";
        var (_, graph1) = CreateWorkflowAndGraph("def-1");
        var (_, graph2) = CreateWorkflowAndGraph("def-2");
        var filter = new WorkflowDefinitionFilter();
        Mock.Get(_workflowDefinitionStore).FindManyAsync(filter, Arg.Any<CancellationToken>()).Returns(new[] { definition1, definition2 });
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("id-1").Returns("cache-key-1");
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("id-2").Returns("cache-key-2");
        SetupDefinitionAndGraphMaterialization(definition1, graph1);
        SetupDefinitionAndGraphMaterialization(definition2, graph2);
        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphsAsync(filter);

        // Assert
        var graphs = result.ToList();
        await Assert.That(graphs.Count).IsEqualTo(2);
        await Assert.That(graphs).Contains(graph1);
        await Assert.That(graphs).Contains(graph2);
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("id-1").WasCalled(Times.Exactly(1));
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("id-2").WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task FindWorkflowGraphsAsync_SkipsDefinitionsWithUnavailableMaterializer()
    {
        // Arrange
        var definition1 = TestHelpers.CreateWorkflowDefinition("def-1", "available-materializer");
        definition1.Id = "id-1";
        var definition2 = TestHelpers.CreateWorkflowDefinition("def-2", "unavailable-materializer");
        definition2.Id = "id-2";
        var definitions = new[] { definition1, definition2 };

        var (_, graph1) = CreateWorkflowAndGraph("def-1");

        var filter = new WorkflowDefinitionFilter();
        Mock.Get(_workflowDefinitionStore).FindManyAsync(filter, Arg.Any<CancellationToken>()).Returns(definitions);
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("id-1").Returns("cache-key-1");
        Mock.Get(_materializerRegistry).IsMaterializerAvailable("available-materializer").Returns(true);
        Mock.Get(_materializerRegistry).IsMaterializerAvailable("unavailable-materializer").Returns(false);
        Mock.Get(_decoratedService).MaterializeWorkflowAsync(definition1, Arg.Any<CancellationToken>()).Returns(graph1);

        var service = CreateService();

        // Act
        var result = await service.FindWorkflowGraphsAsync(filter);

        // Assert
        var graphs = result.ToList();
        await Assert.That(graphs).HasSingleItem();
        await Assert.That(graphs[0]).IsSameReferenceAs(graph1);
        Mock.Get(_decoratedService).MaterializeWorkflowAsync(definition2, Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task TryFindWorkflowGraphAsync_ByDefinitionIdAndVersionOptions_ResolvesDefinitionAndGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition(DefaultDefinitionId, DefaultMaterializerName);
        definition.Id = DefaultVersionId;
        var (_, workflowGraph) = CreateWorkflowAndGraph(DefaultDefinitionId);
        Mock.Get(_cacheManager).CreateWorkflowDefinitionVersionCacheKey(DefaultDefinitionId, VersionOptions.Published).Returns("cache-key-def");
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey(DefaultVersionId).Returns("cache-key-graph");
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(DefaultDefinitionId, VersionOptions.Published, Arg.Any<CancellationToken>()).Returns(definition);
        SetupDefinitionAndGraphMaterialization(definition, workflowGraph);
        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphAsync(DefaultDefinitionId, VersionOptions.Published);

        // Assert
        await Assert.That(result.WorkflowDefinition).IsSameReferenceAs(definition);
        await Assert.That(result.WorkflowGraph).IsSameReferenceAs(workflowGraph);
        Mock.Get(_cacheManager).CreateWorkflowDefinitionVersionCacheKey(DefaultDefinitionId, VersionOptions.Published).WasCalled(Times.Exactly(1));
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey(DefaultVersionId).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task TryFindWorkflowGraphAsync_ByDefinitionVersionId_ResolvesDefinitionAndGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition(DefaultDefinitionId, DefaultMaterializerName);
        definition.Id = DefaultVersionId;
        var (_, workflowGraph) = CreateWorkflowAndGraph(DefaultDefinitionId);
        Mock.Get(_cacheManager).CreateWorkflowDefinitionVersionCacheKey(DefaultVersionId).Returns("cache-key-def");
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey(DefaultVersionId).Returns("cache-key-graph");
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(DefaultVersionId, Arg.Any<CancellationToken>()).Returns(definition);
        SetupDefinitionAndGraphMaterialization(definition, workflowGraph);
        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphAsync(DefaultVersionId);

        // Assert
        await Assert.That(result.WorkflowDefinition).IsSameReferenceAs(definition);
        await Assert.That(result.WorkflowGraph).IsSameReferenceAs(workflowGraph);
        Mock.Get(_cacheManager).CreateWorkflowDefinitionVersionCacheKey(DefaultVersionId).WasCalled(Times.Exactly(1));
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey(DefaultVersionId).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task TryFindWorkflowGraphAsync_ByHandle_ConvertsTFilterAndResolvesDefinitionAndGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition(DefaultDefinitionId, DefaultMaterializerName);
        definition.Id = DefaultVersionId;
        var (_, workflowGraph) = CreateWorkflowAndGraph(DefaultDefinitionId);
        var handle = WorkflowDefinitionHandle.ByDefinitionId(DefaultDefinitionId, VersionOptions.Latest);
        Mock.Get(_cacheManager).CreateWorkflowDefinitionFilterCacheKey(Arg.Any<WorkflowDefinitionFilter>()).Returns("cache-key-def");
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey(DefaultVersionId).Returns("cache-key-graph");
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>()).Returns(definition);
        SetupDefinitionAndGraphMaterialization(definition, workflowGraph);
        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphAsync(handle);

        // Assert
        await Assert.That(result.WorkflowDefinition).IsSameReferenceAs(definition);
        await Assert.That(result.WorkflowGraph).IsSameReferenceAs(workflowGraph);
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task TryFindWorkflowGraphAsync_ByFilter_ResolvesDefinitionAndGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition(DefaultDefinitionId, DefaultMaterializerName);
        definition.Id = DefaultVersionId;
        var (_, workflowGraph) = CreateWorkflowAndGraph(DefaultDefinitionId);
        var filter = new WorkflowDefinitionFilter { DefinitionId = DefaultDefinitionId };
        Mock.Get(_cacheManager).CreateWorkflowDefinitionFilterCacheKey(filter).Returns("cache-key-def");
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey(DefaultVersionId).Returns("cache-key-graph");
        Mock.Get(_decoratedService).FindWorkflowDefinitionAsync(filter, Arg.Any<CancellationToken>()).Returns(definition);
        SetupDefinitionAndGraphMaterialization(definition, workflowGraph);
        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphAsync(filter);

        // Assert
        await Assert.That(result.WorkflowDefinition).IsSameReferenceAs(definition);
        await Assert.That(result.WorkflowGraph).IsSameReferenceAs(workflowGraph);
        Mock.Get(_cacheManager).CreateWorkflowDefinitionFilterCacheKey(filter).WasCalled(Times.Exactly(1));
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey(DefaultVersionId).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task TryFindWorkflowGraphsAsync_WithAvailableMaterializer_CachesGraph()
    {
        // Arrange
        var definition = TestHelpers.CreateWorkflowDefinition(DefaultDefinitionId, DefaultMaterializerName);
        definition.Id = "id-1";
        var (_, graph) = CreateWorkflowAndGraph(DefaultDefinitionId);
        var filter = new WorkflowDefinitionFilter();
        Mock.Get(_workflowDefinitionStore).FindManyAsync(filter, Arg.Any<CancellationToken>()).Returns(new[] { definition });
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("id-1").Returns("cache-key");
        SetupDefinitionAndGraphMaterialization(definition, graph);
        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphsAsync(filter);

        // Assert
        var results = result.ToList();
        await Assert.That(results).HasSingleItem();
        await Assert.That(results[0].WorkflowDefinition).IsSameReferenceAs(definition);
        await Assert.That(results[0].WorkflowGraph).IsSameReferenceAs(graph);
    }

    [Test]
    public async Task TryFindWorkflowGraphsAsync_WithUnavailableMaterializer_ReturnsNullGraph()
    {
        // Arrange
        const string unavailableMaterializer = "unavailable-materializer";
        var definition = TestHelpers.CreateWorkflowDefinition(DefaultDefinitionId, unavailableMaterializer);
        definition.Id = "id-1";
        var filter = new WorkflowDefinitionFilter();
        Mock.Get(_workflowDefinitionStore).FindManyAsync(filter, Arg.Any<CancellationToken>()).Returns(new[] { definition });
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("id-1").Returns("cache-key");
        Mock.Get(_materializerRegistry).IsMaterializerAvailable(unavailableMaterializer).Returns(false);
        var service = CreateService();

        // Act
        var result = await service.TryFindWorkflowGraphsAsync(filter);

        // Assert
        var results = result.ToList();
        await Assert.That(results).HasSingleItem();
        await Assert.That(results[0].WorkflowDefinition).IsSameReferenceAs(definition);
        await Assert.That(results[0].WorkflowGraph).IsNull();
        Mock.Get(_decoratedService).MaterializeWorkflowAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task TryFindWorkflowGraphsAsync_WithMixedMaterializers_HandlesEachCorrectly()
    {
        // Arrange
        var definition1 = TestHelpers.CreateWorkflowDefinition("def-1", "available-materializer");
        definition1.Id = "id-1";
        var definition2 = TestHelpers.CreateWorkflowDefinition("def-2", "unavailable-materializer");
        definition2.Id = "id-2";
        var definitions = new[] { definition1, definition2 };

        var (_, graph1) = CreateWorkflowAndGraph("def-1");

        var filter = new WorkflowDefinitionFilter();
        Mock.Get(_workflowDefinitionStore).FindManyAsync(filter, Arg.Any<CancellationToken>()).Returns(definitions);
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("id-1").Returns("cache-key-1");
        Mock.Get(_cacheManager).CreateWorkflowVersionCacheKey("id-2").Returns("cache-key-2");
        Mock.Get(_materializerRegistry).IsMaterializerAvailable("available-materializer").Returns(true);
        Mock.Get(_materializerRegistry).IsMaterializerAvailable("unavailable-materializer").Returns(false);
        Mock.Get(_decoratedService).MaterializeWorkflowAsync(definition1, Arg.Any<CancellationToken>()).Returns(graph1);

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

    private CachingWorkflowDefinitionService CreateService()
    {
        return new(
            _decoratedService,
            _cacheManager,
            _workflowDefinitionStore,
            _materializerRegistry);
    }

    /// <summary>
    /// Sets up the cache manager with proper mock behavior for all cache operations.
    /// </summary>
    private void SetupCacheManager()
    {
        Mock.Get(_cacheManager).Cache.Returns(_cache);

        var cachingOptions = Microsoft.Extensions.Options.Options.Create(new CachingOptions { CacheDuration = TimeSpan.FromMinutes(10) });
        Mock.Get(_cache).CachingOptions.Returns(cachingOptions);

        // Setup cache to call factory functions for all types
        Mock.Get(_cache).GetOrCreateAsync<WorkflowDefinition>(Arg.Any<object>(), Arg.Is<global::System.Func<global::Microsoft.Extensions.Caching.Memory.ICacheEntry, global::System.Threading.Tasks.Task<global::Flamme.Workflows.Management.Entities.WorkflowDefinition>>>(__value => __value is Func<ICacheEntry, Task<WorkflowDefinition>>))
                    .Returns(async (object __arg0, global::System.Func<global::Microsoft.Extensions.Caching.Memory.ICacheEntry, global::System.Threading.Tasks.Task<global::Flamme.Workflows.Management.Entities.WorkflowDefinition>> __arg1) => await __arg1(ICacheEntry.Mock().Object));
        Mock.Get(_cache).GetOrCreateAsync<WorkflowGraph>(Arg.Any<object>(), Arg.Is<global::System.Func<global::Microsoft.Extensions.Caching.Memory.ICacheEntry, global::System.Threading.Tasks.Task<global::Flamme.Workflows.Models.WorkflowGraph>>>(__value => __value is Func<ICacheEntry, Task<WorkflowGraph>>))
                    .Returns(async (object __arg0, global::System.Func<global::Microsoft.Extensions.Caching.Memory.ICacheEntry, global::System.Threading.Tasks.Task<global::Flamme.Workflows.Models.WorkflowGraph>> __arg1) => await __arg1(ICacheEntry.Mock().Object));
        Mock.Get(_cache).GetOrCreateAsync<WorkflowGraphFindResult>(Arg.Any<object>(), Arg.Is<global::System.Func<global::Microsoft.Extensions.Caching.Memory.ICacheEntry, global::System.Threading.Tasks.Task<global::Flamme.Workflows.Management.Models.WorkflowGraphFindResult>>>(__value => __value is Func<ICacheEntry, Task<WorkflowGraphFindResult>>))
                    .Returns(async (object __arg0, global::System.Func<global::Microsoft.Extensions.Caching.Memory.ICacheEntry, global::System.Threading.Tasks.Task<global::Flamme.Workflows.Management.Models.WorkflowGraphFindResult>> __arg1) => await __arg1(ICacheEntry.Mock().Object));
        Mock.Get(_cache).FindOrCreateAsync<WorkflowDefinition>(Arg.Any<object>(), Arg.Is<global::System.Func<global::Microsoft.Extensions.Caching.Memory.ICacheEntry, global::System.Threading.Tasks.Task<global::Flamme.Workflows.Management.Entities.WorkflowDefinition>>>(__value => __value is Func<ICacheEntry, Task<WorkflowDefinition>>))
                    .Returns(async (object __arg0, global::System.Func<global::Microsoft.Extensions.Caching.Memory.ICacheEntry, global::System.Threading.Tasks.Task<global::Flamme.Workflows.Management.Entities.WorkflowDefinition>> __arg1) => (WorkflowDefinition?)await __arg1(ICacheEntry.Mock().Object));
        Mock.Get(_cache).FindOrCreateAsync<WorkflowGraph>(Arg.Any<object>(), Arg.Is<global::System.Func<global::Microsoft.Extensions.Caching.Memory.ICacheEntry, global::System.Threading.Tasks.Task<global::Flamme.Workflows.Models.WorkflowGraph>>>(__value => __value is Func<ICacheEntry, Task<WorkflowGraph>>))
                    .Returns(async (object __arg0, global::System.Func<global::Microsoft.Extensions.Caching.Memory.ICacheEntry, global::System.Threading.Tasks.Task<global::Flamme.Workflows.Models.WorkflowGraph>> __arg1) => (WorkflowGraph?)await __arg1(ICacheEntry.Mock().Object));
    }

    /// <summary>
    /// Creates a workflow and workflow graph for testing.
    /// </summary>
    private (Workflow, WorkflowGraph) CreateWorkflowAndGraph(string definitionId = "def-1", int version = 1)
    {
        var workflow = new Workflow { Identity = new(definitionId, version, $"v{version}") };
        var workflowGraph = TestHelpers.CreateWorkflowGraph(workflow);
        return (workflow, workflowGraph);
    }

    /// <summary>
    /// Sets up a complete workflow definition with graph materialization for testing scenarios
    /// where definition needs to be resolved and materialized into a graph.
    /// </summary>
    private void SetupDefinitionAndGraphMaterialization(
        WorkflowDefinition definition,
        WorkflowGraph workflowGraph,
        string? materializerName = null)
    {
        materializerName ??= definition.MaterializerName;
        Mock.Get(_materializerRegistry).IsMaterializerAvailable(materializerName).Returns(true);
        Mock.Get(_decoratedService).MaterializeWorkflowAsync(definition, Arg.Any<CancellationToken>()).Returns(workflowGraph);
    }
}
