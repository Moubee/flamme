using Elsa.Common;
using Elsa.Workflows.Management;
using Elsa.Workflows.Runtime.Options;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Quiescence;

/// <summary>
/// Shared scaffolding for <see cref="DrainOrchestrator"/> unit tests: builds the mock dependency graph and exposes
/// hooks the test classes can manipulate.
/// </summary>
public abstract class DrainOrchestratorTestsBase
{
    protected readonly FakeTimeProvider TimeProvider = new(DateTimeOffset.Parse("2026-04-25T10:00:00Z"));
    protected readonly IQuiescenceSignal Signal = Mock.Of<IQuiescenceSignal>().Object;
    protected readonly IIngressSourceRegistry Registry = Mock.Of<IIngressSourceRegistry>().Object;
    protected readonly IExecutionCycleRegistry ExecutionCycleRegistry = Mock.Of<IExecutionCycleRegistry>().Object;
    protected readonly IWorkflowInstanceStore InstanceStore = Mock.Of<IWorkflowInstanceStore>().Object;
    protected readonly IWorkflowExecutionLogStore LogStore = Mock.Of<IWorkflowExecutionLogStore>().Object;
    protected readonly GracefulShutdownOptions Options = new();
    protected readonly HostOptions HostOptionsValue = new() { ShutdownTimeout = TimeSpan.FromSeconds(60) };

    protected DrainOrchestratorTestsBase()
    {
        Mock.Get(Signal).CurrentState.Returns(QuiescenceState.Initial("gen-1"));
        Mock.Get(Signal).BeginDrainAsync(Arg.Any<CancellationToken>()).Returns(() => new ValueTask<QuiescenceState>(QuiescenceState.Initial("gen-1")));
        Mock.Get(Registry).Sources.Returns(Array.Empty<IIngressSource>());
        Mock.Get(Registry).Snapshot().Returns(Array.Empty<IngressSourceSnapshot>());
        Mock.Get(ExecutionCycleRegistry).ActiveCount.Returns(0);
        Mock.Get(ExecutionCycleRegistry).ListActiveCycles().Returns(Array.Empty<ExecutionCycleHandle>());
        Mock.Get(InstanceStore).TryMarkInterruptedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
                    .Returns(true);
    }

    protected void AdvanceTime(TimeSpan span) => TimeProvider.Advance(span);

    protected void SetTime(DateTimeOffset value) => TimeProvider.SetUtcNow(value);

    protected DrainOrchestrator BuildSut()
    {
        var scopeFactory = Mock.Of<IServiceScopeFactory>().Object;
        var scope = Mock.Of<IServiceScope>().Object;
        var sp = Mock.Of<IServiceProvider>().Object;
        Mock.Get(sp).GetService(typeof(IWorkflowInstanceStore)).Returns(InstanceStore);
        Mock.Get(sp).GetService(typeof(IWorkflowExecutionLogStore)).Returns(LogStore);
        Mock.Get(scope).ServiceProvider.Returns(sp);
        Mock.Get(scopeFactory).CreateScope().Returns(scope);

        var identityGenerator = Mock.Of<IIdentityGenerator>().Object;
        Mock.Get(identityGenerator).GenerateId().Returns(() => Guid.NewGuid().ToString("N"));

        return new DrainOrchestrator(
            Signal,
            Registry,
            ExecutionCycleRegistry,
            scopeFactory,
            Microsoft.Extensions.Options.Options.Create(Options),
            Microsoft.Extensions.Options.Options.Create(HostOptionsValue),
            TimeProvider,
            identityGenerator,
            Mock.Of<ILogger<DrainOrchestrator>>().Object);
    }

    protected static IIngressSource CreateSource(string name, TimeSpan? pauseTimeout = null, Func<CancellationToken, ValueTask>? pauseImpl = null)
    {
        var source = Mock.Of<IIngressSource>().Object;
        Mock.Get(source).Name.Returns(name);
        Mock.Get(source).PauseTimeout.Returns(pauseTimeout ?? TimeSpan.FromSeconds(5));
        Mock.Get(source).CurrentState.Returns(IngressSourceState.Running);
        if (pauseImpl is not null)
            Mock.Get(source).PauseAsync(Arg.Any<CancellationToken>()).Returns((global::System.Threading.CancellationToken __arg0) => pauseImpl(__arg0));
        else
            Mock.Get(source).PauseAsync(Arg.Any<CancellationToken>()).Returns(() => ValueTask.CompletedTask);
        return source;
    }
}
