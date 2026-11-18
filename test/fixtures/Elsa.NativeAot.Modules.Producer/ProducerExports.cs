using Elsa.CodeGeneration;
using Elsa.Features.Abstractions;
using Elsa.Features.Models;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.NativeAot.Modules.Producer;

/// <summary>
/// Compiled producer entry point consumed without assembly discovery.
/// </summary>
public static class ProducerExports
{
    public static FeatureModuleExport Foundation { get; } = new ExplicitFeatureCatalogBuilder()
        .Add(module => new FoundationFeature(module), new FeatureDescriptor("Foundation", "Fixture", "Foundation fixture"))
        .AddHostedService<FoundationHostedService>()
        .CreateExport(new ModuleExportDescriptor("fixture.foundation", 1, capabilities: ["fixture.foundation-service"]));
}

/// <summary>
/// State belongs to one host; the shared export retains no host instances.
/// </summary>
public sealed class ProducerState
{
    public const string StatePropertyKey = "fixture.state";
    public string Value { get; set; } = "default";
    public int Constructors { get; set; }
    public int Configurations { get; set; }
    public int Applications { get; set; }
    public int HostedConstructors { get; set; }
    public int Starts { get; set; }
    public int Stops { get; set; }
    public List<string> Events { get; } = [];
    public List<Guid> ScopeIds { get; } = [];
    public List<Guid> DisposedScopeIds { get; } = [];
}

public sealed class FoundationFeature : FeatureBase
{
    private readonly ProducerState _state;

    public FoundationFeature(IModule module) : base(module)
    {
        _state = (ProducerState)module.Properties[ProducerState.StatePropertyKey];
        _state.Constructors++;
    }

    public string Value { get; set; } = "default";

    public override void Configure()
    {
        _state.Configurations++;
        _state.Value = Value;
        _state.Events.Add("foundation.configure");
    }

    public override void ConfigureHostedServices() => Module.ConfigureHostedService<FoundationHostedService>(-10);

    public override void Apply()
    {
        _state.Applications++;
        _state.Events.Add("foundation.apply");
        Services.AddSingleton(_state);
        Services.AddScoped<FoundationService>();
    }
}

public sealed class FoundationService : IDisposable
{
    private readonly ProducerState _state;

    public FoundationService(ProducerState state)
    {
        _state = state;
        state.ScopeIds.Add(Id);
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Value => _state.Value;
    public void Dispose() => _state.DisposedScopeIds.Add(Id);
}

public sealed class FoundationHostedService : IHostedService
{
    private readonly ProducerState _state;
    private readonly IServiceScopeFactory _scopeFactory;

    public FoundationHostedService(ProducerState state, IServiceScopeFactory scopeFactory)
    {
        _state = state;
        _scopeFactory = scopeFactory;
        state.HostedConstructors++;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<FoundationService>();
        if (service.Value != _state.Value)
            throw new InvalidOperationException("The hosted producer resolved another host's state.");
        await Task.Delay(1, cancellationToken);
        _state.Starts++;
        _state.Events.Add("foundation.start");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        _state.Stops++;
        _state.Events.Add("foundation.stop");
    }
}


// Importing this producer must never activate its unused features.
internal sealed class UnselectedFeature : FeatureBase
{
    public UnselectedFeature(IModule module) : base(module) =>
        throw new InvalidOperationException("The host did not select this feature.");
}
