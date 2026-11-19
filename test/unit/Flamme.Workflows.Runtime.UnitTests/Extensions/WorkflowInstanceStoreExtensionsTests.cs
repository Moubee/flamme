using Flamme.Common.Models;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Models;
using TUnit.Mocks;

namespace Flamme.Workflows.Runtime.UnitTests.Extensions;

public class WorkflowInstanceStoreExtensionsTests
{
    [Test]
    public async Task EnumerateSummariesAsync_AdvancesAcrossPagesWithoutRepeatingOffsets()
    {
        // Arrange
        var store = IWorkflowInstanceStore.Mock().Object;
        var filter = new WorkflowInstanceFilter();
        var requestedPages = new List<PageArgs>();
        var page1 = new List<WorkflowInstanceSummary>
        {
            CreateSummary("workflow-1"),
            CreateSummary("workflow-2")
        };
        var page2 = new List<WorkflowInstanceSummary>
        {
            CreateSummary("workflow-3"),
            CreateSummary("workflow-4")
        };
        Mock.Get(store).SummarizeManyAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>())
                    .Returns((global::Flamme.Workflows.Management.Filters.WorkflowInstanceFilter __arg0, global::Flamme.Common.Models.PageArgs __arg1, global::System.Threading.CancellationToken __arg2) =>
                    {
                        var pageArgs = __arg1;
                        requestedPages.Add(pageArgs with { });

                        var items = pageArgs.Offset switch
                        {
                            0 => page1,
                            2 => page2,
                            _ => new List<WorkflowInstanceSummary>()
                        };

                        return new ValueTask<Page<WorkflowInstanceSummary>>(Page.Of(items, page1.Count + page2.Count));
                    });

        // Act
        var results = new List<WorkflowInstanceSummary>();
        await foreach (var workflowInstance in store.EnumerateSummariesAsync(filter, 2, CancellationToken.None))
            results.Add(workflowInstance);

        // Assert
        await Assert.That(results.Select(x => x.Id).ToArray()).IsEquivalentTo(["workflow-1", "workflow-2", "workflow-3", "workflow-4"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(requestedPages.Select(x => x.Offset).ToArray()).IsEquivalentTo([(int?)0, 2, 4], EqualityComparer<int?>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        foreach (var requestedPage in requestedPages)
            await Assert.That(requestedPage.Limit).IsEqualTo(2);
    }

    private static WorkflowInstanceSummary CreateSummary(string id) => new()
    {
        Id = id,
        DefinitionId = "definition",
        DefinitionVersionId = "definition:1"
    };
}
