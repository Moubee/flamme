using Flamme.Common;
using Flamme.Common.Entities;
using Flamme.Common.Models;
using Flamme.Mediator.Contracts;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Materializers;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Management.Notifications;
using Flamme.Workflows.Management.Options;
using Flamme.Workflows.Models;
using Microsoft.Extensions.Options;

namespace Flamme.Workflows.Management.Services;

/// <inheritdoc />
public class WorkflowDefinitionPublisher(
    IWorkflowDefinitionService workflowDefinitionService,
    IWorkflowDefinitionStore workflowDefinitionStore,
    IWorkflowValidator workflowValidator,
    IIdentityGenerator identityGenerator,
    IActivitySerializer activitySerializer,
    IMediator mediator,
    TimeProvider timeProvider,
    IOptions<ManagementOptions> options)
    : IWorkflowDefinitionPublisher
{
    /// <inheritdoc />
    public WorkflowDefinition New(IActivity? root = null)
    {
        root ??= new Sequence();
        var id = identityGenerator.GenerateId();
        var definitionId = identityGenerator.GenerateId();
        const int version = 1;

        return new()
        {
            Id = id,
            DefinitionId = definitionId,
            Version = version,
            IsLatest = true,
            IsPublished = false,
            CreatedAt = timeProvider.GetUtcNow(),
            StringData = activitySerializer.Serialize(root),
            MaterializerName = JsonWorkflowMaterializer.MaterializerName
        };
    }

    public Task<WorkflowDefinition> NewAsync(IActivity? root = null, CancellationToken cancellationToken = default)
    {
        root ??= new Sequence();
        var id = identityGenerator.GenerateId();
        var definitionId = identityGenerator.GenerateId();
        const int version = 1;

        var workflowDefinition = new WorkflowDefinition
        {
            Id = id,
            DefinitionId = definitionId,
            Version = version,
            IsLatest = true,
            IsPublished = false,
            CreatedAt = timeProvider.GetUtcNow(),
            StringData = activitySerializer.Serialize(root),
            MaterializerName = JsonWorkflowMaterializer.MaterializerName
        };

        return Task.FromResult(workflowDefinition);
    }

    /// <inheritdoc />
    public async Task<ImportWorkflowResult> CreateImportAsync(WorkflowDefinition definition, bool publish, CancellationToken cancellationToken = default)
    {
        definition = Initialize(definition);
        definition.Version = 1;
        List<WorkflowValidationError> errors = [];
        if (publish)
        {
            errors = await ValidateAsync(definition, cancellationToken);
            if (errors.Count > 0 && options.Value.FailOnValidationErrors)
                return new(false, definition, errors);

            await mediator.SendAsync(new WorkflowDefinitionPublishing(definition), cancellationToken);
        }
        else
            await mediator.SendAsync(new WorkflowDefinitionDraftSaving(definition), cancellationToken);

        definition.IsPublished = publish;
        definition.IsLatest = true;
        if (!await workflowDefinitionStore.TryAddAsync(definition, cancellationToken))
            return new(false, definition, [new("A workflow with this revision ID or logical version already exists.")]);

        if (publish)
            await mediator.SendAsync(new WorkflowDefinitionPublished(definition, new([])), cancellationToken);
        else
        {
            await mediator.SendAsync(new WorkflowDefinitionDraftSaved(definition), cancellationToken);
            await mediator.SendAsync(new WorkflowDefinitionCreated(definition), cancellationToken);
        }
        return new(true, definition, errors);
    }

    /// <inheritdoc />
    public async Task<PublishWorkflowDefinitionResult> PublishAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        var filter = WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Latest).ToFilter();
        var definition = await workflowDefinitionStore.FindAsync(filter, cancellationToken);

        if (definition == null)
            return new(false, new List<WorkflowValidationError>
            {
                new("Workflow definition not found.")
            }, new([]));

        return await PublishAsync(definition, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PublishWorkflowDefinitionResult> PublishAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default)
    {
        var validationErrors = await ValidateAsync(definition, cancellationToken);

        if (validationErrors.Any() && options.Value.FailOnValidationErrors)
            return new(false, validationErrors, new([]));

        await mediator.SendAsync(new WorkflowDefinitionPublishing(definition), cancellationToken);
        var definitionId = definition.DefinitionId;

        // Reset current latest and published definitions.
        var publishedWorkflows = await workflowDefinitionStore.FindManyAsync(new()
        {
            DefinitionId = definitionId,
            VersionOptions = VersionOptions.LatestOrPublished
        }, cancellationToken);

        // Publish the new head and demote its predecessors in one store operation. Cloning
        // also avoids mutating visible rows before the in-memory store validates the batch.
        var demotions = new List<WorkflowDefinition>();
        var retractions = new List<WorkflowDefinition>();
        foreach (var previous in publishedWorkflows)
        {
            var demoted = previous.ShallowClone();
            demoted.IsLatest = false;
            demoted.IsPublished = false;
            if (previous.IsPublished)
                retractions.Add(demoted);
            if (previous.Id != definition.Id)
                demotions.Add(demoted);
        }

        definition = Initialize(definition);
        definition.IsPublished = true;
        definition.IsLatest = true;
        await workflowDefinitionStore.SavePublicationAsync(demotions.Append(definition), cancellationToken);

        foreach (var retracted in retractions)
            await mediator.SendAsync(new WorkflowDefinitionVersionRetracted(retracted), cancellationToken);

        var affectedWorkflows = new AffectedWorkflows(new List<WorkflowDefinition>());
        await mediator.SendAsync(new WorkflowDefinitionPublished(definition, affectedWorkflows), cancellationToken);
        return new(true, validationErrors, affectedWorkflows);
    }

    /// <inheritdoc />
    public async Task<WorkflowDefinition?> RetractAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        var filter = WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Published).ToFilter();
        var definition = await workflowDefinitionStore.FindAsync(filter, cancellationToken);

        if (definition == null)
            return null;

        return await RetractAsync(definition, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkflowDefinition> RetractAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default)
    {
        if (!definition.IsPublished)
            throw new InvalidOperationException("Cannot retract an unpublished workflow definition.");

        definition.IsPublished = false;

        await mediator.SendAsync(new WorkflowDefinitionRetracting(definition), cancellationToken);
        await workflowDefinitionStore.SaveAsync(definition, cancellationToken);
        await mediator.SendAsync(new WorkflowDefinitionRetracted(definition), cancellationToken);
        return definition;
    }

    public async Task<WorkflowDefinition> RevertVersionAsync(string definitionId, int version, CancellationToken cancellationToken = default)
    {
        var latestVersionFilter = new WorkflowDefinitionFilter
        {
            DefinitionId = definitionId,
            VersionOptions = VersionOptions.Latest
        };
        var latestVersion = await workflowDefinitionStore.FindAsync(latestVersionFilter, cancellationToken);
        var lastVersionFilter = new WorkflowDefinitionFilter
        {
            DefinitionId = definitionId
        };
        var lastVersion = await workflowDefinitionStore.FindLastVersionAsync(lastVersionFilter, cancellationToken);

        if (latestVersion != null)
        {
            latestVersion.IsLatest = false;
            await workflowDefinitionStore.SaveAsync(latestVersion, cancellationToken);
        }

        var draft = await GetDraftAsync(definitionId, VersionOptions.SpecificVersion(version), cancellationToken);
        draft!.Id = identityGenerator.GenerateId();
        draft.Version = (lastVersion?.Version ?? 0) + 1;
        draft.IsLatest = true;

        await workflowDefinitionStore.SaveAsync(draft, cancellationToken);
        return draft;
    }

    /// <inheritdoc />
    public async Task<WorkflowDefinition?> GetDraftAsync(string definitionId, VersionOptions versionOptions, CancellationToken cancellationToken = default)
    {
        var filter = new WorkflowDefinitionFilter
        {
            DefinitionId = definitionId,
            VersionOptions = versionOptions
        };
        var order = new WorkflowDefinitionOrder<int>(x => x.Version, OrderDirection.Descending);
        var lastVersion = await workflowDefinitionStore.FindLastVersionAsync(new()
        {
            DefinitionId = definitionId
        }, cancellationToken);
        var definition = await workflowDefinitionStore.FindAsync(filter, order, cancellationToken) ?? lastVersion;

        if (definition == null!)
            return null;

        if (!definition.IsPublished && !definition.IsReadonly)
            return definition;

        var draft = definition.ShallowClone();
        draft.CustomProperties = new Dictionary<string, object>(definition.CustomProperties);

        draft.Version = lastVersion?.Version + 1 ?? 1;
        draft.CreatedAt = timeProvider.GetUtcNow();
        draft.Id = identityGenerator.GenerateId();
        draft.IsLatest = true;
        draft.IsPublished = false;

        return draft;
    }

    /// <inheritdoc />
    public async Task<WorkflowDefinition> SaveDraftAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default)
    {
        var draft = definition;
        var definitionId = definition.DefinitionId;
        var filter = new WorkflowDefinitionFilter
        {
            DefinitionId = definitionId
        };
        var highestVersion = await workflowDefinitionStore.FindLastVersionAsync(filter, cancellationToken);
        var latestVersion = await workflowDefinitionStore.FindAsync(new()
        {
            DefinitionId = definitionId,
            VersionOptions = VersionOptions.Latest
        }, cancellationToken);

        draft.Version = draft.Id == latestVersion?.Id
            ? latestVersion.Version
            : draft.Id == highestVersion?.Id
                ? highestVersion.Version
                : highestVersion?.Version + 1 ?? 1;
        draft.IsLatest = true;
        draft = Initialize(draft);

        await mediator.SendAsync(new WorkflowDefinitionDraftSaving(draft), cancellationToken);

        if (latestVersion != null && latestVersion.Id != draft.Id)
        {
            latestVersion.IsLatest = false;
            await workflowDefinitionStore.SaveManyAsync([latestVersion, draft], cancellationToken);
        }
        else
        {
            await workflowDefinitionStore.SaveAsync(draft, cancellationToken);
        }
        await mediator.SendAsync(new WorkflowDefinitionDraftSaved(draft), cancellationToken);

        if (highestVersion is null)
            await mediator.SendAsync(new WorkflowDefinitionCreated(definition), cancellationToken);

        return draft;
    }

    private WorkflowDefinition Initialize(WorkflowDefinition definition)
    {
        if (definition.Id == null!)
            definition.Id = identityGenerator.GenerateId();

        if (definition.DefinitionId == null!)
            definition.DefinitionId = identityGenerator.GenerateId();

        if (definition.Version == 0)
            definition.Version = 1;

        return definition;
    }

    private async Task<List<WorkflowValidationError>> ValidateAsync(WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var graph = await workflowDefinitionService.MaterializeWorkflowAsync(definition, cancellationToken);
        return (await workflowValidator.ValidateAsync(graph.Workflow, cancellationToken)).ToList();
    }
}
