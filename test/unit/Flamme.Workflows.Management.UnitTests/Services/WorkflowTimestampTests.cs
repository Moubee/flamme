using Flamme.Common;
using Flamme.Common.Models;
using Flamme.Mediator.Contracts;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Options;
using Flamme.Workflows.Management.Services;
using Microsoft.Extensions.Time.Testing;

namespace Flamme.Workflows.Management.UnitTests.Services;

public class WorkflowTimestampTests
{
    private static readonly DateTimeOffset InitialUtc = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    [Test]
    public async Task NewDefinitions_UseTheProviderAtEachCreation()
    {
        var time = new FakeTimeProvider(InitialUtc);
        var publisher = CreatePublisher(time, IWorkflowDefinitionStore.Mock().Object);

        var first = publisher.New();
        time.Advance(TimeSpan.FromTicks(37));
        var second = await publisher.NewAsync();

        await Assert.That(first.CreatedAt).IsEqualTo(InitialUtc);
        await Assert.That(second.CreatedAt).IsEqualTo(InitialUtc.AddTicks(37));
    }

    [Test]
    public async Task DraftFromPublishedDefinition_GetsCurrentTimeWithoutChangingPublishedTime()
    {
        var time = new FakeTimeProvider(InitialUtc);
        var published = new WorkflowDefinition { Id = "version-1", DefinitionId = "definition", Version = 1, IsPublished = true, CreatedAt = InitialUtc };
        var store = IWorkflowDefinitionStore.Mock().Object;
        Mock.Get(store).FindLastVersionAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>()).Returns(published);
        Mock.Get(store).FindAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<WorkflowDefinitionOrder<int>>(), Arg.Any<CancellationToken>()).Returns(published);
        var publisher = CreatePublisher(time, store);

        time.Advance(TimeSpan.FromMinutes(5));
        var draft = await publisher.GetDraftAsync("definition", VersionOptions.Latest);

        await Assert.That(draft!.CreatedAt).IsEqualTo(InitialUtc.AddMinutes(5));
        await Assert.That(published.CreatedAt).IsEqualTo(InitialUtc);
        await Assert.That(draft.Version).IsEqualTo(2);
    }

    [Test]
    public async Task NewInstance_SharesOneCreationInstantWithItsWorkflowState()
    {
        var time = new FakeTimeProvider(InitialUtc) { AutoAdvanceAmount = TimeSpan.FromTicks(1) };
        var factory = new WorkflowInstanceFactory(IIdentityGenerator.Mock().Object, time);
        var instance = factory.CreateWorkflowInstance(new Workflow(), new WorkflowInstanceOptions { WorkflowInstanceId = "instance" });

        await Assert.That(instance.CreatedAt).IsEqualTo(InitialUtc);
        await Assert.That(instance.UpdatedAt).IsEqualTo(InitialUtc);
        await Assert.That(instance.WorkflowState.CreatedAt).IsEqualTo(InitialUtc);
        await Assert.That(instance.WorkflowState.UpdatedAt).IsEqualTo(InitialUtc);
    }

    private static WorkflowDefinitionPublisher CreatePublisher(TimeProvider time, IWorkflowDefinitionStore store) => new(
        IWorkflowDefinitionService.Mock().Object,
        store,
        IWorkflowValidator.Mock().Object,
        IIdentityGenerator.Mock().Object,
        IActivitySerializer.Mock().Object,
        IMediator.Mock().Object,
        time,
        Microsoft.Extensions.Options.Options.Create(new ManagementOptions()));
}
