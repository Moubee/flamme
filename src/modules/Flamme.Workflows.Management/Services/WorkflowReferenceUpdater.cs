using Flamme.Common.Models;
using Flamme.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Activities.WorkflowDefinitionActivity;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Materializers;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Models;
using Microsoft.Extensions.Logging;

namespace Flamme.Workflows.Management.Services;

public class WorkflowReferenceUpdater(
    IWorkflowDefinitionPublisher publisher,
    IWorkflowDefinitionService workflowDefinitionService,
    IWorkflowDefinitionStore workflowDefinitionStore,
    IWorkflowReferenceGraphBuilder workflowReferenceGraphBuilder,
    WorkflowDefinitionActivityDescriptorFactory workflowDefinitionActivityDescriptorFactory,
    IActivityRegistry activityRegistry,
    IApiSerializer serializer,
    ILogger<WorkflowReferenceUpdater> logger)
    : IWorkflowReferenceUpdater
{
    private bool _isUpdating;

    public async Task<UpdateWorkflowReferencesResult> UpdateWorkflowReferencesAsync(
        WorkflowDefinition referencedDefinition,
        CancellationToken cancellationToken = default)
    {
        if (_isUpdating ||
            referencedDefinition.Options is not { UsableAsActivity: true, AutoUpdateConsumingWorkflows: true })
            return new([]);

        var referenceGraph = await workflowReferenceGraphBuilder.BuildGraphAsync(referencedDefinition.DefinitionId, cancellationToken);
        
        // Get all consumer (source) and referenced (target) IDs from the edges
        var referencingIds = referenceGraph.Edges.Select(e => e.Source).Distinct().ToList();
        var referencedIds = referenceGraph.Edges.Select(e => e.Target).Distinct().ToList();

        if (referencingIds.Count == 0)
            return new([]);

        var referencingWorkflowGraphs = (await workflowDefinitionService.FindWorkflowGraphsAsync(new()
            {
                DefinitionIds = referencingIds,
                VersionOptions = VersionOptions.Latest,
                IsReadonly = false
            }, cancellationToken))
            .ToDictionary(g => g.Workflow.Identity.DefinitionId);

        var referencedWorkflowDefinitionList = (await workflowDefinitionStore.FindManyAsync(new()
        {
            DefinitionIds = referencedIds,
            VersionOptions = VersionOptions.Published,
            IsReadonly = false
        }, cancellationToken)).ToList();

        var referencedWorkflowDefinitionsPublished = referencedWorkflowDefinitionList
            .GroupBy(x => x.DefinitionId)
            .Select(group =>
            {
                var publishedVersion = group.FirstOrDefault(x => x.IsPublished);
                return publishedVersion ?? group.First();
            })
            .ToDictionary(d => d.DefinitionId);

        var initialPublicationState = new Dictionary<string, bool>();

        foreach (var workflowGraph in referencingWorkflowGraphs)
            initialPublicationState[workflowGraph.Key] = workflowGraph.Value.Workflow.Publication.IsPublished;

        // Add the initially referenced definition
        referencedWorkflowDefinitionsPublished[referencedDefinition.DefinitionId] = referencedDefinition;

        // Use the OutboundEdges lookup from the graph (Source → what it depends on)
        var dependencyMap = referenceGraph.OutboundEdges;

        // Perform topological sort to ensure dependent workflows are processed in the right order
        var sortedWorkflowIds = referencingIds
            .TSort(id => dependencyMap[id], true)
            // Only process workflows that exist in our referencing workflows dictionary
            .Where(id => referencingWorkflowGraphs.ContainsKey(id))
            .ToList();

        var updatedWorkflows = new List<WorkflowDefinition>();
        _isUpdating = true;
        try
        {
            foreach (var id in sortedWorkflowIds)
            {
                var targets = dependencyMap[id]
                    .Select(targetId => referencedWorkflowDefinitionsPublished.GetValueOrDefault(targetId))
                    .OfType<WorkflowDefinition>()
                    .Where(target => initialPublicationState.GetValueOrDefault(target.DefinitionId, target.IsPublished))
                    .ToList();
                if (targets.Count == 0) continue;

                var updated = await UpdateWorkflowAsync(referencingWorkflowGraphs[id], targets, cancellationToken);
                if (updated == null) continue;

                // Persist this revision before materializing any workflow that consumes it.
                if (initialPublicationState[id])
                {
                    var result = await publisher.PublishAsync(updated, cancellationToken);
                    if (!result.Succeeded)
                    {
                        logger.LogWarning("Could not publish reference updates for workflow definition {DefinitionId}: {Errors}.",
                            id, string.Join(", ", result.ValidationErrors.Select(error => error.Message)));
                        continue;
                    }

                    referencedWorkflowDefinitionsPublished[id] = updated;
                }
                else
                {
                    updated = await publisher.SaveDraftAsync(updated, cancellationToken);
                    await RegisterSavedDraftAsync(updated, cancellationToken);
                }

                updatedWorkflows.Add(updated);
            }
        }
        finally
        {
            _isUpdating = false;
        }

        return new(updatedWorkflows);
    }

    private async Task<WorkflowDefinition?> UpdateWorkflowAsync(
        WorkflowGraph graph,
        IReadOnlyCollection<WorkflowDefinition> targets,
        CancellationToken cancellationToken)
    {
        var id = graph.Workflow.Identity.DefinitionId;
        var latest = await workflowDefinitionStore.FindAsync(new WorkflowDefinitionFilter
        {
            DefinitionId = id,
            VersionOptions = VersionOptions.Latest
        }, cancellationToken);
        if (latest == null) return null;

        if (latest.MaterializerName != JsonWorkflowMaterializer.MaterializerName)
        {
            foreach (var target in targets)
            {
                if (FindActivities(graph.Root, target.DefinitionId).Any(a => a.WorkflowDefinitionVersionId != target.Id))
                {
                    logger.LogWarning(
                        "Skipping reference update for workflow definition {ConsumerDefinitionId}: it is authored in a non-JSON source format and must be updated manually to reference {ReferencedDefinitionId} version {ReferencedVersion}.",
                        id, target.DefinitionId, target.Version);
                }
            }

            return null;
        }

        var draft = await publisher.GetDraftAsync(id, VersionOptions.Latest, cancellationToken);
        if (draft == null) return null;

        var draftGraph = await workflowDefinitionService.MaterializeWorkflowAsync(draft, cancellationToken);
        var changed = false;
        foreach (var target in targets)
        {
            foreach (var activity in FindActivities(draftGraph.Root, target.DefinitionId).Where(a => a.WorkflowDefinitionVersionId != target.Id))
            {
                activity.WorkflowDefinitionVersionId = target.Id;
                activity.Version = target.Version;
                activity.LatestAvailablePublishedVersionId = target.Id;
                activity.LatestAvailablePublishedVersion = target.Version;
                changed = true;
            }
        }

        if (!changed) return null;

        if (draftGraph.Root.Activity is Workflow workflow)
        {
            draft.StringData = serializer.Serialize(workflow.Root);
            draft.OriginalSource = null;
        }

        return draft;
    }

    private async Task RegisterSavedDraftAsync(WorkflowDefinition draft, CancellationToken cancellationToken)
    {
        if (draft.Options.UsableAsActivity != true || string.IsNullOrWhiteSpace(draft.Name)) return;

        var publishedVersion = await workflowDefinitionStore.FindAsync(
            WorkflowDefinitionHandle.ByDefinitionId(draft.DefinitionId, VersionOptions.Published).ToFilter(),
            cancellationToken);
        var descriptor = workflowDefinitionActivityDescriptorFactory.CreateDescriptor(draft, publishedVersion);
        activityRegistry.Add(typeof(WorkflowDefinitionActivityProvider), descriptor);
    }

    private static IEnumerable<WorkflowDefinitionActivity> FindActivities(ActivityNode node, string definitionId)
    {
        // Do not drill into activities that are WorkflowDefinitionActivity
        if (node.Activity is WorkflowDefinitionActivity)
            yield break;

        foreach (var child in node.Children)
        {
            if (child.Activity is WorkflowDefinitionActivity activity && activity.WorkflowDefinitionId == definitionId)
                yield return activity;
            foreach (var grandChildActivity in FindActivities(child, definitionId))
                yield return grandChildActivity;
        }
    }
}