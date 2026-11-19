using System.Runtime.CompilerServices;
using Flamme.Common.Models;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Models;

namespace Flamme.Workflows.Runtime;

public static class WorkflowInstanceStoreExtensions
{
    public static async IAsyncEnumerable<WorkflowInstanceSummary> EnumerateSummariesAsync(
        this IWorkflowInstanceStore store, 
        WorkflowInstanceFilter filter, 
        int batchSize = 100, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pageArgs = PageArgs.FromPage(0, batchSize);

        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await store.SummarizeManyAsync(filter, pageArgs, cancellationToken);
            var workflowInstances = page.Items;

            if (workflowInstances.Count == 0)
                yield break;

            foreach (var workflowInstance in workflowInstances)
                yield return workflowInstance;

            pageArgs = pageArgs.Next();
        }
    }
}