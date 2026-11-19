using System.Diagnostics.CodeAnalysis;
using Polly;
using Polly.Retry;

namespace Elsa.Resilience.Core.UnitTests.TestHelpers;

/// <summary>
/// A strategy that configures a real Polly retry pipeline with zero delay, so tests can exercise actual retry behavior.
/// </summary>
internal class TestRetryStrategy : IResilienceStrategy
{
    public string Id { get; set; } = "test-retry";
    public string DisplayName { get; set; } = "Test Retry";
    public int MaxRetryAttempts { get; set; } = 2;

    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "This JIT-run helper uses primitive test results and must add generic retries to the supplied builder so the production retry telemetry remains attached; the production strategy interface does not carry Polly's result-preservation requirement.")]
    public Task ConfigurePipeline<T>(ResiliencePipelineBuilder<T> pipelineBuilder, ResilienceContext context)
    {
        pipelineBuilder.AddRetry(new RetryStrategyOptions<T>
        {
            MaxRetryAttempts = MaxRetryAttempts,
            Delay = TimeSpan.Zero,
            BackoffType = DelayBackoffType.Constant,
            ShouldHandle = new PredicateBuilder<T>().Handle<InvalidOperationException>()
        });

        return Task.CompletedTask;
    }
}

/// <summary>
/// A strategy that adds nothing to the pipeline, used to verify pass-through behavior and polymorphic serialization.
/// </summary>
internal class TestNoopStrategy : IResilienceStrategy
{
    public string Id { get; set; } = "test-noop";
    public string DisplayName { get; set; } = "Test Noop";
    public TestStrategyFlavor Flavor { get; set; }

    public Task ConfigurePipeline<T>(ResiliencePipelineBuilder<T> pipelineBuilder, ResilienceContext context) => Task.CompletedTask;
}

internal enum TestStrategyFlavor
{
    Plain,
    Fancy
}
