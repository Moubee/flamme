using Flamme.Api.Client.Resources.WorkflowDefinitions.Contracts;
using Flamme.Api.Client.Resources.WorkflowDefinitions.Responses;
using Flamme.Api.Client.Resources.WorkflowInstances.Models;
using Flamme.Common.Entities;
using Flamme.Testing.Shared.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.ComponentTests.Abstractions;
using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.Helpers;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Filters;
using Flamme.Workflows.Runtime.OrderDefinitions;
using Microsoft.Extensions.DependencyInjection;
using Open.Linq.AsyncExtensions;

namespace Flamme.Workflows.ComponentTests.Scenarios.LogPersistenceModes;

public class InputOutputLoggingTests(App app) : AppComponentTest(app)
{
    [Test]
    [Arguments("input-output-logging-1", true, false, true, false)]
    [Arguments("input-output-logging-2", true, false, true, false)]
    public async Task Workflow_ShouldHonorSettings_WhenExecuting(string workflowDefinitionId, params bool[] shouldBeIncludedArray)
    {
        var workflowState = await ExecuteWorkflowAsync(workflowDefinitionId);
        var workflowInstanceId = workflowState.Id;
        var writeLineActivityTypeName = ActivityTypeNameHelper.GenerateTypeName<WriteLine>();
        var activityExecutionRecords = await GetActivityExecutionRecordsAsync(workflowInstanceId, x => x.ActivityType == writeLineActivityTypeName).ToList();

        for (var i = 0; i < shouldBeIncludedArray.Length; i++)
        {
            var activityExecutionRecord = activityExecutionRecords[i];
            var shouldBeIncluded = shouldBeIncludedArray[i];
            var isIncluded = activityExecutionRecord.ActivityState?.ContainsKey(nameof(WriteLine.Text)) == true;
            await Assert.That(isIncluded).IsEqualTo(shouldBeIncluded);
        }
    }

    [Test]
    public async Task WorkflowAsActivity_ShouldHonorSettings_WhenExecuting()
    {
        var workflowState = await ExecuteWorkflowAsync("input-output-logging-3");
        var activityExecutionRecord = await GetRecordByActivityNameAsync(workflowState.Id, "InputOutputLogging3-Child1");
        var output1IsIncluded = activityExecutionRecord.Outputs!.ContainsKey("Output1");
        var output2IsIncluded = activityExecutionRecord.Outputs!.ContainsKey("Output2");

        await Assert.That(output1IsIncluded).IsTrue();
        await Assert.That(output2IsIncluded).IsTrue();
    }

    [Test]
    public async Task WorkflowAsActivityInternal_ShouldHonorSettings_WhenExecuting()
    {
        var workflowState = await ExecuteWorkflowAsync("input-output-logging-3");
        var setOutput1Record = await GetRecordByActivityNameAsync(workflowState.Id, "SetOutput1");
        var setOutput2Record = await GetRecordByActivityNameAsync(workflowState.Id, "SetOutput2");
        var output1IsIncluded = setOutput1Record.ActivityState?.ContainsKey("OutputName") == true;
        var output2IsIncluded = setOutput2Record.ActivityState?.ContainsKey("OutputName") == true;

        await Assert.That(output1IsIncluded).IsFalse();
        await Assert.That(output2IsIncluded).IsTrue();
    }

    private async Task<WorkflowState> ExecuteWorkflowAsync(string workflowDefinitionId)
    {
        var client = WorkflowServer.CreateApiClient<IExecuteWorkflowApi>();
        using var response = await client.ExecuteAsync(workflowDefinitionId);
        var model = await response.ReadAsJsonAsync<ExecuteWorkflowDefinitionResponse>(WorkflowServer.Services);
        return model.WorkflowState;
    }

    private Task<IEnumerable<ActivityExecutionRecord>> GetActivityExecutionRecordsAsync(string workflowInstanceId, Func<ActivityExecutionRecord, bool> predicate)
    {
        var activityExecutionRecordFilter = new ActivityExecutionRecordFilter
        {
            WorkflowInstanceId = workflowInstanceId,
        };
        return GetActivityExecutionRecordsAsync(activityExecutionRecordFilter, predicate);
    }

    private async Task<ActivityExecutionRecord> GetRecordByActivityNameAsync(string workflowInstanceId, string name)
    {
        var filter = new ActivityExecutionRecordFilter
        {
            WorkflowInstanceId = workflowInstanceId,
            Name = name
        };
        return await Assert.That(await GetActivityExecutionRecordsAsync(filter)).HasSingleItem();
    }

    private async Task<IEnumerable<ActivityExecutionRecord>> GetActivityExecutionRecordsAsync(ActivityExecutionRecordFilter filter, Func<ActivityExecutionRecord, bool>? predicate = null)
    {
        var activityExecutionStore = Scope.ServiceProvider.GetRequiredService<IActivityExecutionStore>();
        var orderBy = new ActivityExecutionRecordOrder<DateTimeOffset>(x => x.StartedAt, OrderDirection.Ascending);
        var activityExecutionRecords = await activityExecutionStore.FindManyAsync(filter, orderBy);
        return predicate == null ? activityExecutionRecords : activityExecutionRecords.Where(predicate);
    }
}