using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Time.Testing;
using System.Reflection;
using Elsa.Common;
using Elsa.Mediator.Contracts;
using Elsa.Scheduling.ScheduledTasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Scheduling.UnitTests.ScheduledTasks;

public class ScheduledTaskDisposalTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly ICommandSender _commandSender = ICommandSender.Mock().Object;
    private readonly IServiceScopeFactory _scopeFactory = IServiceScopeFactory.Mock().Object;
    private readonly IServiceScope _scope = IServiceScope.Mock().Object;
    private readonly FakeTimeProvider _timeProvider = new(Now);
    private readonly ICronParser _cronParser = ICronParser.Mock().Object;
    private readonly ServiceProvider _services;

    public ScheduledTaskDisposalTests()
    {
        _services = new ServiceCollection().AddSingleton(_commandSender).BuildServiceProvider();
        Mock.Get(_scope).ServiceProvider.Returns(_services);
        Mock.Get(_scopeFactory).CreateScope().Returns(_scope);
        Mock.Get(_cronParser).GetNextOccurrence(Arg.Any<string>()).Returns(Now.AddHours(1));
    }

    [Test]
    [Arguments(ScheduledTaskKind.Cron)]
    [Arguments(ScheduledTaskKind.Recurring)]
    [Arguments(ScheduledTaskKind.SpecificInstant)]
    public async Task DisposeDuringScopeCreation_DoesNotExecuteOrReschedule(ScheduledTaskKind kind)
    {
        using var scheduledTask = CreateScheduledTask(kind);
        Mock.Get(_scopeFactory).CreateScope().Returns(() =>
        {
            // The callback has passed its initial disposal check, but has not read its token yet.
            scheduledTask.Dispose();
            return _scope;
        });

        await InvokeElapsedAsync(scheduledTask);

        Mock.Get(_commandSender).SendAsync(Arg.Any<ICommand>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        Mock.Get(_scope).Dispose().WasCalled(Times.Exactly(1));
        await Assert.That(GetTimer(scheduledTask)).IsNull();
    }

    [Test]
    [Arguments(ScheduledTaskKind.Cron)]
    [Arguments(ScheduledTaskKind.Recurring)]
    [Arguments(ScheduledTaskKind.SpecificInstant)]
    public async Task QueuedCallbackAfterDisposal_DoesNotCreateScope(ScheduledTaskKind kind)
    {
        using var scheduledTask = CreateScheduledTask(kind);
        scheduledTask.Dispose();

        await InvokeElapsedAsync(scheduledTask);

        Mock.Get(_scopeFactory).CreateScope().WasNeverCalled();
        Mock.Get(_commandSender).SendAsync(Arg.Any<ICommand>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [Arguments(ScheduledTaskKind.Cron)]
    [Arguments(ScheduledTaskKind.Recurring)]
    [Arguments(ScheduledTaskKind.SpecificInstant)]
    public async Task DisposeDuringExecution_CompletesCallbackWithoutRescheduling(ScheduledTaskKind kind)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completeCommand = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock.Get(_commandSender).SendAsync(Arg.Any<ICommand>(), Arg.Any<CancellationToken>())
            .Returns((ICommand _, CancellationToken _) =>
            {
                started.TrySetResult();
                return completeCommand.Task;
            });
        using var scheduledTask = CreateScheduledTask(kind);
        var callback = InvokeElapsedAsync(scheduledTask);

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scheduledTask.Dispose();
        }
        finally
        {
            completeCommand.TrySetResult();
            await callback.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Mock.Get(_commandSender).SendAsync(Arg.Any<ICommand>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_scope).Dispose().WasCalled(Times.Exactly(1));
        await Assert.That(GetTimer(scheduledTask)).IsNull();
    }

    private IDisposable CreateScheduledTask(ScheduledTaskKind kind)
    {
        var task = ITask.Mock().Object;
        return kind switch
        {
            ScheduledTaskKind.Cron => new ScheduledCronTask(task, "0 * * * * *", _cronParser, _scopeFactory, _timeProvider, NullLogger<ScheduledCronTask>.Instance),
            ScheduledTaskKind.Recurring => new ScheduledRecurringTask(task, Now.AddHours(1), TimeSpan.FromHours(1), _timeProvider, _scopeFactory, NullLogger<ScheduledRecurringTask>.Instance),
            ScheduledTaskKind.SpecificInstant => new ScheduledSpecificInstantTask(task, Now.AddHours(1), _timeProvider, _scopeFactory, NullLogger<ScheduledSpecificInstantTask>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static Task InvokeElapsedAsync(IDisposable scheduledTask)
    {
        var callback = GetScheduledTaskType(scheduledTask).GetMethod("OnElapsedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Task>>(scheduledTask);
        return callback();
    }

    private static object? GetTimer(IDisposable scheduledTask) =>
        GetScheduledTaskType(scheduledTask).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scheduledTask);

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicMethods | DynamicallyAccessedMemberTypes.NonPublicFields)]
    private static Type GetScheduledTaskType(IDisposable scheduledTask) => scheduledTask switch
    {
        ScheduledCronTask => typeof(ScheduledCronTask),
        ScheduledRecurringTask => typeof(ScheduledRecurringTask),
        ScheduledSpecificInstantTask => typeof(ScheduledSpecificInstantTask),
        _ => throw new ArgumentException("Unknown scheduled task type.", nameof(scheduledTask))
    };

    public void Dispose() => _services.Dispose();

    public enum ScheduledTaskKind
    {
        Cron,
        Recurring,
        SpecificInstant
    }
}
