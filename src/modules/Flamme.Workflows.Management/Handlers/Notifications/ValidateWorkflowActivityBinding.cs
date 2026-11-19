using Flamme.Common.Models;
using Flamme.Common.Multitenancy;
using Flamme.Mediator.Contracts;
using Flamme.Workflows.Management.Activities.WorkflowDefinitionActivity;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Exceptions;
using Flamme.Workflows.Management.Notifications;

namespace Flamme.Workflows.Management.Handlers.Notifications;

/// <summary>Checks native workflow-as-activity bindings before a draft or publication enters the store.</summary>
public class ValidateWorkflowActivityBinding(IWorkflowDefinitionStore store, ITenantAccessor tenantAccessor, IActivityRegistry registry,
    WorkflowDefinitionActivityDescriptorFactory descriptorFactory, IEnumerable<IActivityDescriptorValidator> validators) :
    INotificationHandler<WorkflowDefinitionDraftSaving>, INotificationHandler<WorkflowDefinitionPublishing>
{
    public Task HandleAsync(WorkflowDefinitionDraftSaving notification, CancellationToken cancellationToken) =>
        ValidateAsync(notification.WorkflowDefinition, cancellationToken);

    public Task HandleAsync(WorkflowDefinitionPublishing notification, CancellationToken cancellationToken) =>
        ValidateAsync(notification.WorkflowDefinition, cancellationToken);

    private async Task ValidateAsync(WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        if (definition.Options.UsableAsActivity != true)
            return;

        var descriptor = descriptorFactory.CreateDescriptor(definition);
        descriptor.TenantId = definition.TenantId ?? tenantAccessor.TenantId;
        descriptor.Version = definition.Version == 0 ? 1 : definition.Version;
        foreach (var validator in validators)
            if (validator.GetValidationError(descriptor) is { } error)
                throw new WorkflowDefinitionValidationException(error);

        var name = descriptor.TypeName;
        var version = descriptor.Version;
        var scope = descriptor.TenantId;
        var staticBinding = registry.Find(x => x.TypeName == name && x.Version == version &&
            (x.TenantId ?? Tenant.AgnosticTenantId) == scope && x.ClrType != descriptor.ClrType);
        if (staticBinding != null)
            throw new WorkflowDefinitionValidationException($"'{name}' version {version} is already registered in tenant '{scope}'.");

        var summaries = await store.FindSummariesAsync(new() { UsableAsActivity = true, VersionOptions = VersionOptions.SpecificVersion(version) }, cancellationToken);
        var potentialConflicts = summaries
            .Where(x => x.DefinitionId != definition.DefinitionId && !string.IsNullOrWhiteSpace(x.Name))
            .Where(x => WorkflowDefinitionActivityDescriptorFactory.GetTypeName(x.Name) == name)
            .Select(x => x.Id)
            .ToArray();
        if (potentialConflicts.Length == 0)
            return;

        // Shared and tenant-specific workflows may intentionally provide the same activity.
        // Fetch the owning tenant only for matching names; summaries omit the executable graph.
        var matches = await store.FindManyAsync(new() { Ids = potentialConflicts }, cancellationToken);
        if (matches.Any(x => x.TenantId.NormalizeTenantId() == scope))
            throw new WorkflowDefinitionValidationException($"Another workflow already provides '{name}' version {version} in tenant '{scope}'.");
    }
}
