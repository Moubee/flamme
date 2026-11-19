using System.Text.Json;
using Flamme.Common.Models;
using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Activities.WorkflowDefinitionActivity;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.IntegrationTests.Scenarios.DependencyWorkflowsPublishing;

public class TransitivePublicationTests : IAsyncDisposable
{
    private readonly IServiceProvider _services = new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
        .ConfigureFlamme(flamme => flamme.UseWorkflowManagement(management =>
            management.UseWorkflowReferenceFinder(sp => new OrderedConsumerQuery(ActivatorUtilities.CreateInstance<DefaultWorkflowReferenceQuery>(sp)))))
        .Build();

    [Test]
    public async Task PublishingSharedDependencyUpdatesDirectAndTransitiveRevisionIds()
    {
        await _services.PopulateRegistriesAsync();
        var leaf = await PublishInitialAsync("Leaf");
        var branch = await PublishInitialAsync("Branch", leaf);
        var outer = await PublishInitialAsync("Outer", branch, leaf);

        var graphBuilder = _services.GetRequiredService<IWorkflowReferenceGraphBuilder>();
        var graph = await graphBuilder.BuildGraphAsync(leaf.DefinitionId);
        await Assert.That(graph.GetDependencies(outer.DefinitionId).SequenceEqual([branch.DefinitionId, leaf.DefinitionId])).IsTrue();

        var publisher = _services.GetRequiredService<IWorkflowDefinitionPublisher>();
        var nextLeaf = (await publisher.GetDraftAsync(leaf.DefinitionId, VersionOptions.Published))!;
        var publication = await publisher.PublishAsync(nextLeaf);
        await Assert.That(publication.Succeeded).IsTrue();

        var nextBranch = await _services.GetWorkflowDefinitionAsync(branch.DefinitionId, VersionOptions.Published);
        var nextOuter = await _services.GetWorkflowDefinitionAsync(outer.DefinitionId, VersionOptions.Published);
        await Assert.That(nextBranch.Version).IsEqualTo(2);
        await Assert.That(nextOuter.Version).IsEqualTo(2);
        await Assert.That(ReadStoredReferences(nextBranch)[leaf.DefinitionId]).IsEqualTo(nextLeaf.Id);
        var outerReferences = ReadStoredReferences(nextOuter);
        await Assert.That(outerReferences[branch.DefinitionId]).IsEqualTo(nextBranch.Id);
        await Assert.That(outerReferences[leaf.DefinitionId]).IsEqualTo(nextLeaf.Id);
        await _services.GetRequiredService<IWorkflowDefinitionService>().MaterializeWorkflowAsync(nextOuter);
    }

    private async Task<WorkflowDefinition> PublishInitialAsync(string name, params WorkflowDefinition[] dependencies)
    {
        var publisher = _services.GetRequiredService<IWorkflowDefinitionPublisher>();
        var root = new Sequence();
        foreach (var dependency in dependencies)
            root.Activities.Add(new WorkflowDefinitionActivity
            {
                Type = dependency.Name!,
                WorkflowDefinitionId = dependency.DefinitionId,
                WorkflowDefinitionVersionId = dependency.Id,
                Version = dependency.Version
            });
        var definition = await publisher.NewAsync(root);
        definition.DefinitionId = name;
        definition.Name = name;
        definition.Options.UsableAsActivity = true;
        definition.Options.AutoUpdateConsumingWorkflows = true;
        var publication = await publisher.PublishAsync(definition);
        await Assert.That(publication.Succeeded).IsTrue();
        return definition;
    }

    private static Dictionary<string, string?> ReadStoredReferences(WorkflowDefinition definition)
    {
        using var json = JsonDocument.Parse(definition.StringData!);
        return json.RootElement.GetProperty("activities").EnumerateArray().ToDictionary(
            activity => activity.GetProperty("workflowDefinitionId").GetString()!,
            activity => activity.GetProperty("workflowDefinitionVersionId").GetString());
    }

    // Visit Branch before Outer so the graph builder discovers Outer -> Branch before Outer -> Leaf.
    private sealed class OrderedConsumerQuery(DefaultWorkflowReferenceQuery inner) : IWorkflowReferenceQuery
    {
        public async Task<IEnumerable<string>> ExecuteAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            (await inner.ExecuteAsync(workflowDefinitionId, cancellationToken)).Order(StringComparer.Ordinal);
    }

    public ValueTask DisposeAsync() => TestResourceDisposal.DisposeAsync(_services);
}
