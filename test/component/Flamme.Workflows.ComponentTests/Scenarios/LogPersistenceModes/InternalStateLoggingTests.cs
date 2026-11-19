using Flamme.Api.Client.Resources.WorkflowDefinitions.Contracts;
using Flamme.Api.Client.Resources.WorkflowDefinitions.Responses;
using Flamme.Testing.Shared.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.ComponentTests.Abstractions;
using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.Helpers;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.ComponentTests.Scenarios.LogPersistenceModes;

public class InternalStateLoggingTests(App app) : AppComponentTest(app)
{
    [Test]
    public async Task ActivityInternalState_ShouldBeIndependentOfDefaultMode()
    {
        var records = await ExecuteAndGetWriteLinesAsync("internal-state-logging-activity");

        await AssertInternalStateAsync(await GetRecordAsync(records, "WriteLine1"), textIncluded: false, internalStateIncluded: true);
        await AssertInternalStateAsync(await GetRecordAsync(records, "WriteLine2"), textIncluded: true, internalStateIncluded: false);
    }

    [Test]
    public async Task WorkflowInternalState_ShouldApplyWhenActivityInternalStateIsMissing()
    {
        var records = await ExecuteAndGetWriteLinesAsync("internal-state-logging-workflow");

        await AssertInternalStateAsync(await GetRecordAsync(records, "WriteLine1"), textIncluded: false, internalStateIncluded: true);
        await AssertInternalStateAsync(await GetRecordAsync(records, "WriteLine2"), textIncluded: false, internalStateIncluded: false);
    }

    private static async Task<ActivityExecutionRecord> GetRecordAsync(IReadOnlyList<ActivityExecutionRecord> records, string activityName) =>
        await Assert.That(records).HasSingleItem(x => x.ActivityName == activityName);

    private static async Task AssertInternalStateAsync(ActivityExecutionRecord record, bool textIncluded, bool internalStateIncluded)
    {
        await Assert.That(record.ActivityState?.ContainsKey(nameof(WriteLine.Text)) == true).IsEqualTo(textIncluded);

        if (internalStateIncluded)
        {
            await Assert.That(record.Properties).IsNotNull();
            return;
        }

        await Assert.That(record.Properties).IsNull();
        await Assert.That(record.Payload).IsNull();
    }

    private async Task<IReadOnlyList<ActivityExecutionRecord>> ExecuteAndGetWriteLinesAsync(string workflowDefinitionId)
    {
        var client = WorkflowServer.CreateApiClient<IExecuteWorkflowApi>();
        using var response = await client.ExecuteAsync(workflowDefinitionId);
        var model = await response.ReadAsJsonAsync<ExecuteWorkflowDefinitionResponse>(WorkflowServer.Services);
        var writeLineActivityTypeName = ActivityTypeNameHelper.GenerateTypeName<WriteLine>();
        var store = Scope.ServiceProvider.GetRequiredService<IActivityExecutionStore>();
        var records = await store.FindManyAsync(new ActivityExecutionRecordFilter
        {
            WorkflowInstanceId = model.WorkflowState.Id
        });

        return records.Where(x => x.ActivityType == writeLineActivityTypeName).ToList();
    }
}
