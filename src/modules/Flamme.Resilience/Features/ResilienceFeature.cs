using Flamme.Expressions.Options;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Flamme.Resilience.Endpoints.SimulateResponse;
using Flamme.Resilience.Entities;
using Flamme.Resilience.Modifiers;
using Flamme.Resilience.Options;
using Flamme.Resilience.Recorders;
using Flamme.Resilience.Serialization;
using Flamme.Resilience.StrategySources;
using Flamme.Workflows;
using Flamme.Workflows.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Flamme.Common.Serialization;

namespace Flamme.Resilience.Features;

public class ResilienceFeature(IModule module) : FeatureBase(module)
{
    private Func<IServiceProvider, IRetryAttemptRecorder> _retryAttemptRecorder = sp => sp.GetRequiredService<ActivityExecutionContextRetryAttemptRecorder>();
    private Func<IServiceProvider, IRetryAttemptReader> _retryAttemptReader = sp => sp.GetRequiredService<ActivityExecutionContextRetryAttemptReader>();

    public ResilienceFeature AddResilienceStrategyType<T>() where T : IResilienceStrategy
    {
        return AddResilienceStrategyType(typeof(T));
    }

    public ResilienceFeature AddResilienceStrategyType(Type strategyType)
    {
        Services.Configure<ResilienceOptions>(options => options.StrategyTypes.Add(strategyType));
        return this;
    }

    public ResilienceFeature WithActivityExecutionContextRetryAttemptRecorder()
    {
        return WithRetryAttemptRecorder<ActivityExecutionContextRetryAttemptRecorder>()
            .WithRetryAttemptReader<ActivityExecutionContextRetryAttemptReader>();
    }

    public ResilienceFeature WithVoidRetryAttemptRecorder()
    {
        return WithRetryAttemptRecorder<VoidRetryAttemptRecorder>()
            .WithRetryAttemptReader<VoidRetryAttemptReader>();
    }

    public ResilienceFeature WithRetryAttemptRecorder<T>() => WithRetryAttemptRecorder(sp => (IRetryAttemptRecorder)ActivatorUtilities.CreateInstance<T>(sp)!);
    public ResilienceFeature WithRetryAttemptReader<T>() => WithRetryAttemptReader(sp => (IRetryAttemptReader)ActivatorUtilities.CreateInstance<T>(sp)!);

    public ResilienceFeature WithRetryAttemptRecorder(Func<IServiceProvider, IRetryAttemptRecorder> recorder)
    {
        _retryAttemptRecorder = recorder;
        return this;
    }

    public ResilienceFeature WithRetryAttemptReader(Func<IServiceProvider, IRetryAttemptReader> reader)
    {
        _retryAttemptReader = reader;
        return this;
    }

    public override void Configure()
    {
        Module.AddFastEndpointsAssembly<ResilienceFeature>();

        Services.Configure<ExpressionOptions>(options =>
        {
            options.AddTypeAlias<List<RetryAttemptRecord>>("RetryAttemptRecordList");
        });

        Services.Configure<SerializationTypeOptions>(options =>
        {
            options.RegisterTypeAlias(typeof(List<RetryAttemptRecord>), "RetryAttemptRecordList");
        });
    }

    public override void Apply()
    {
        Services.AddOptions<ResilienceOptions>();
        Services.AddOptions<SimulateResponseOptions>();
        Services.TryAddSingleton(TimeProvider.System);

        Services
            .AddSingleton<ResilienceStrategySerializer>()
            .AddSingleton<SimulateResponseSessionStore>()
            .AddSingleton<IActivityDescriptorModifier, ResilientActivityDescriptorModifier>()
            .AddScoped<IResilienceStrategyCatalog, ResilienceStrategyCatalog>()
            .AddScoped<IResilienceStrategyConfigEvaluator, ResilienceStrategyConfigEvaluator>()
            .AddScoped<IResilientActivityInvoker, ResilientActivityInvoker>()
            .AddScoped<IResilienceStrategySource, ConfigurationResilienceStrategySource>()
            .AddSingleton(VoidRetryAttemptRecorder.Instance)
            .AddSingleton(VoidRetryAttemptReader.Instance)
            .AddScoped<ActivityExecutionContextRetryAttemptRecorder>()
            .AddScoped<ActivityExecutionContextRetryAttemptReader>()
            .AddScoped(_retryAttemptRecorder)
            .AddScoped(_retryAttemptReader)
            .AddHandlersFrom<ResilienceFeature>();

        // Register transient exception detection infrastructure
        Services
            .AddSingleton<ITransientExceptionStrategy, DefaultTransientExceptionStrategy>()
            .AddSingleton<ITransientExceptionDetector, TransientExceptionDetector>();
    }
}
