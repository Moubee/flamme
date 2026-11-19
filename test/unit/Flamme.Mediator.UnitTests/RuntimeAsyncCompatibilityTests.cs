using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Flamme.Mediator.CommandStrategies;
using Flamme.Mediator.Contracts;
using Flamme.Mediator.Middleware.Command;
using Flamme.Mediator.Middleware.Command.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Mediator.UnitTests;

public class RuntimeAsyncCompatibilityTests
{
    [Test]
    [Arguments(typeof(CommandPipeline), nameof(CommandPipeline.InvokeAsync))]
    [Arguments(typeof(CommandHandlerInvokerMiddleware), nameof(CommandHandlerInvokerMiddleware.InvokeAsync))]
    [Arguments(typeof(DefaultStrategy), nameof(DefaultStrategy.ExecuteAsync))]
    public async Task ProductionAsyncMethods_UseSelectedCompilerModel(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type declaringType, string methodName)
    {
        var method = declaringType.GetMethod(methodName)!;
#if NET11_0_OR_GREATER
        const MethodImplAttributes asyncFlag = MethodImplAttributes.Async;
#else
        // The Async metadata flag was introduced in .NET 11; verify its absence on .NET 10.
        const MethodImplAttributes asyncFlag = (MethodImplAttributes)0x2000;
#endif
#if FLAMME_RUNTIME_ASYNC
        await Assert.That((method.GetMethodImplementationFlags() & asyncFlag) != 0).IsTrue();
        await Assert.That(method.IsDefined(typeof(AsyncStateMachineAttribute), false)).IsFalse();
#else
        await Assert.That((method.GetMethodImplementationFlags() & asyncFlag) != 0).IsFalse();
        await Assert.That(method.IsDefined(typeof(AsyncStateMachineAttribute), false)).IsTrue();
#endif
    }

    [Test]
    public async Task CommandMiddleware_AfterSuspension_FlowsAmbientStateWithoutCapturingSynchronizationContext()
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var services = new ServiceCollection()
            .AddSingleton<ICommandHandler>(new GatedCommandHandler(completion.Task))
            .BuildServiceProvider();
        var context = new CommandContext(new GatedCommand(), new DefaultStrategy(), typeof(string),
            new Dictionary<object, object>(), services, CancellationToken.None);
        var ambientTenant = new AsyncLocal<string?>();
        var synchronizationContext = new TrackingSynchronizationContext();
        SynchronizationContext? continuationContext = null;
        string? continuationTenant = null;
        var middleware = new CommandHandlerInvokerMiddleware(_ =>
        {
            continuationContext = SynchronizationContext.Current;
            continuationTenant = ambientTenant.Value;
            return ValueTask.CompletedTask;
        });
        var previousContext = SynchronizationContext.Current;
        var previousTenant = ambientTenant.Value;
        ValueTask invocation;

        try
        {
            ambientTenant.Value = "tenant-a";
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            invocation = middleware.InvokeAsync(context);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            ambientTenant.Value = previousTenant;
        }

        // The incomplete handler forces both the Task strategy and ValueTask middleware to suspend.
        var suspended = !invocation.IsCompleted;
        completion.SetResult("completed");
        await invocation.AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(suspended).IsTrue();
        await Assert.That(context.Result).IsEqualTo("completed");
        await Assert.That(continuationTenant).IsEqualTo("tenant-a");
        await Assert.That(ambientTenant.Value).IsEqualTo(previousTenant);
        await Assert.That(continuationContext).IsNull();
        await Assert.That(synchronizationContext.PostCount).IsEqualTo(0);
    }

    private sealed record GatedCommand : ICommand<string>;

    private sealed class GatedCommandHandler(Task<string> completion) : ICommandHandler<GatedCommand, string>
    {
        public Task<string> HandleAsync(GatedCommand command, CancellationToken cancellationToken) => completion;
    }

    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _postCount);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var previousContext = Current;
                try
                {
                    SetSynchronizationContext(this);
                    callback(state);
                }
                finally
                {
                    SetSynchronizationContext(previousContext);
                }
            });
        }
    }
}
