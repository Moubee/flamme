using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using Flamme.Extensions;
using Flamme.Server.Web;
using Flamme.Workflows.Activities.Flowchart.Extensions;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Extensions;
using Flamme.Workflows.Management.Options;
using Flamme.Workflows.Management.Providers;
using Flamme.Workflows.Management.Services;
using Flamme.Workflows.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flamme.Workflows.PerformanceTests.ActivityDescriptors;

[Config(typeof(Config)), JsonExporterAttribute.Full, GcServer(true), GcConcurrent(true)]
public class CatalogBenchmarks
{
    private ServiceProvider _services = null!;
    private IServiceScope _scope = null!;
    private TypedActivityProvider _provider = null!;
    private IActivityRegistry _registry = null!;
    private ActivityRegistryPopulator _populator = null!;
    private ActivityRegistryLookupService _lookup = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationManager());
        services.AddFlamme(flamme => flamme
            .AddActivitiesFrom<ClassicServerHost>()
            .UseFlowchart(flowchart => flowchart.UseTokenBasedExecution())
            .UseScheduling()
            .UseCSharp(options => options.AllowHostCodeExecution = false)
            .UseJavaScript()
            .UsePython(python => python.PythonOptions += options => options.AllowHostCodeExecution = true)
            .UseLiquid()
            .UseHttp());

        _services = services.BuildServiceProvider();
        _scope = _services.CreateScope();
        var management = _scope.ServiceProvider.GetRequiredService<IOptions<ManagementOptions>>().Value;
        var selected = management.ActivityTypes.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        management.ActivityTypes = new HashSet<Type>(selected);
        _provider = _scope.ServiceProvider.GetRequiredService<TypedActivityProvider>();
        _registry = _scope.ServiceProvider.GetRequiredService<IActivityRegistry>();
        _populator = new ActivityRegistryPopulator([_provider], _registry);
        _lookup = new ActivityRegistryLookupService(_registry, [_provider]);

        if (_registry.ListAll().Any())
            throw new InvalidOperationException("The registry must start empty.");

        var providerRows = (await _provider.GetDescriptorsAsync()).ToArray();
        if (!providerRows.Select(row => row.ClrType).SequenceEqual(selected))
            throw new InvalidOperationException("The provider must describe each selected activity in order.");

        var expected = providerRows.Select(Identity).ToHashSet();
        var first = await CreateAndPopulateRegistry();
        CheckCatalog(first, expected);
        await _populator.EnsureRegistryPopulatedAsync(CancellationToken.None);
        var initial = CheckCatalog(_registry, expected);
        await ForceRefresh();
        var refreshed = CheckCatalog(_registry, expected);
        if (ReferenceEquals(initial, refreshed) || !ReferenceEquals(refreshed, await LookupHit()))
            throw new InvalidOperationException("Refresh must replace the descriptor and a cached lookup must return it.");

        var exported = typeof(IActivity).Assembly.GetExportedTypes().Where(type =>
            typeof(IActivity).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface && !type.IsGenericTypeDefinition);
        var registered = RegisterCoreAssembly();
        if (registered.Count == 0 || !registered.SetEquals(exported))
            throw new InvalidOperationException("Assembly registration did not select the same exported activities.");

        // Include the workload identity in each report so comparisons can detect catalog changes.
        var identities = string.Join('\n', providerRows.Select(row => $"{row.ClrType.FullName}|{row.TypeName}|{row.Version}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identities)));
        Console.WriteLine($"Catalog fixture: {selected.Length} CLR activities, ordered identity SHA-256 {fingerprint}.");
        Console.WriteLine($"Core assembly selection: {registered.Count} exported activities.");
    }

    [Benchmark]
    public async Task<IActivityRegistry> CreateAndPopulateRegistry()
    {
        var registry = ActivatorUtilities.CreateInstance<ActivityRegistry>(_scope.ServiceProvider);
        var populator = new ActivityRegistryPopulator([_provider], registry);
        await populator.EnsureRegistryPopulatedAsync(CancellationToken.None);
        return registry;
    }

    [Benchmark]
    public Task ForceRefresh() => _populator.PopulateRegistryAsync(CancellationToken.None);

    [Benchmark]
    public Task<ActivityDescriptor?> LookupHit() => _lookup.FindAsync("Console.WriteLine", 1);

    [Benchmark]
    public HashSet<Type> RegisterCoreAssembly()
    {
        var services = new ServiceCollection();
        services.AddActivitiesFrom(typeof(IActivity).Assembly);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ManagementOptions>>().Value.ActivityTypes;
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _scope.Dispose();
        await _services.DisposeAsync();
    }

    private static (Type ClrType, string TypeName, int Version) Identity(ActivityDescriptor descriptor) =>
        (descriptor.ClrType, descriptor.TypeName, descriptor.Version);

    private static ActivityDescriptor CheckCatalog(IActivityRegistry registry, HashSet<(Type ClrType, string TypeName, int Version)> expected)
    {
        var rows = registry.ListAll().ToArray();
        if (rows.Length != expected.Count || !expected.SetEquals(rows.Select(Identity)))
            throw new InvalidOperationException("The populated catalog must contain all provider identities and versions.");

        var delay = rows.Single(row => row.TypeName == "Time.Delay");
        var request = rows.Single(row => row.TypeName == "Http.SendRequest");
        if (!Equals(delay.Inputs.Single(input => input.Name == "TimeSpan").DefaultValue, TimeSpan.FromMinutes(1)) ||
            !Equals(request.CustomProperties["ResilienceCategory"], "HTTP") ||
            rows.Single(row => row.TypeName == "Scripting.RunCSharp").IsBrowsable ||
            !rows.Single(row => row.TypeName == "Scripting.RunPython").IsBrowsable)
            throw new InvalidOperationException("Host-scoped defaults, HTTP modifiers or scripting visibility changed.");

        return rows.Single(row => row.TypeName == "Console.WriteLine" && row.Version == 1);
    }
}
