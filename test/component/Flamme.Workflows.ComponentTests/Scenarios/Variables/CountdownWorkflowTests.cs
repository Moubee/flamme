using Flamme.Common.Models;
using Flamme.Expressions.Helpers;
using Flamme.Extensions;
using Flamme.Workflows.ComponentTests.Abstractions;
using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.ComponentTests.Scenarios.Variables.Workflows;
using Flamme.Workflows.Management;
using Flamme.Workflows.Models;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Messages;
using Flamme.Workflows.State;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.ComponentTests.Scenarios.Variables;

public class CountdownWorkflowTests(App app) : AppComponentTest(app)
{
    [Test]
    [DisplayName("Variable is persisted across workflow runs")]
    public async Task VariableIsPersistedAcrossWorkflowRuns()
    {
        var workflowRuntime = Scope.ServiceProvider.GetRequiredService<IWorkflowRuntime>();
        var workflowClient = await workflowRuntime.CreateClientAsync();
        var workflowInstanceStore = Scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
        var bookmarkStore = Scope.ServiceProvider.GetRequiredService<IBookmarkStore>();
        var runAndCreateRequest = new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(CountdownWorkflow.DefinitionId, VersionOptions.Latest),
        };
        var runResponse = await workflowClient.CreateAndRunInstanceAsync(runAndCreateRequest);
        var workflowInstanceId = runResponse.WorkflowInstanceId;
        var createdBookmarks = await bookmarkStore.FindManyAsync(new()
        {
            WorkflowInstanceId = workflowInstanceId
        });
        var bookmarks = new Stack<StoredBookmark>(createdBookmarks);
        var expectedCounter = 3;

        while (bookmarks.Any())
        {
            var workflowInstance = await workflowInstanceStore.FindAsync(workflowInstanceId);
            var workflowState = workflowInstance!.WorkflowState;
            var rootWorkflowActivityExecutionContext = workflowState.ActivityExecutionContexts.Single(x => x.ParentContextId == null);
            var variables = GetVariablesDictionary(rootWorkflowActivityExecutionContext);
            var actualCounter = variables["counterVariable"].ConvertTo<int>();
            await Assert.That(actualCounter).IsEqualTo(--expectedCounter);

            var bookmark = bookmarks.Pop();
            var runRequest = new RunWorkflowInstanceRequest
            {
                BookmarkId = bookmark.Id,
            };
            
            await workflowClient.RunInstanceAsync(runRequest);

            createdBookmarks = await bookmarkStore.FindManyAsync(new()
            {
                WorkflowInstanceId = workflowInstanceId
            });

            foreach (var newBookmark in createdBookmarks)
                bookmarks.Push(newBookmark);
        }
    }

    private VariablesDictionary GetVariablesDictionary(ActivityExecutionContextState context)
    {
        return context.Properties.GetOrAdd(WorkflowInstanceStorageDriver.VariablesDictionaryStateKey, () => new VariablesDictionary());
    }
}