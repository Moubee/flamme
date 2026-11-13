using Elsa.Diagnostics.StructuredLogs.Contracts;
using Elsa.Diagnostics.StructuredLogs.Extensions;
using Elsa.Diagnostics.StructuredLogs.Models;
using Elsa.Diagnostics.StructuredLogs.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.ConformanceTests;

/// <summary>
/// One in-memory <see cref="IStructuredLogStore"/> plus the registry and options it reads.
/// </summary>
public sealed class StructuredLogStoreScenario : IAsyncDisposable
{
    private readonly Func<ValueTask> _flushAsync;
    private readonly Func<ValueTask> _disposeAsync;

    private StructuredLogStoreScenario(
        IStructuredLogStore store,
        IStructuredLogSourceRegistry sourceRegistry,
        bool reportsRingDroppedEvents,
        Func<ValueTask> flushAsync,
        Func<ValueTask> disposeAsync)
    {
        Store = store;
        SourceRegistry = sourceRegistry;
        ReportsRingDroppedEvents = reportsRingDroppedEvents;
        _flushAsync = flushAsync;
        _disposeAsync = disposeAsync;
    }

    public IStructuredLogStore Store { get; }
    public IStructuredLogSourceRegistry SourceRegistry { get; }

    /// <summary>
    /// InMemory reports ring-buffer overflow on <see cref="RecentStructuredLogsResult.DroppedEvents"/>.
    /// Relational QueryAsync stays 0; write-queue drops live on storage diagnostics.
    /// </summary>
    public bool ReportsRingDroppedEvents { get; }

    public async ValueTask WriteAsync(params StructuredLogEvent[] events)
    {
        await Store.WriteManyAsync(events);
        await _flushAsync();
    }

    public ValueTask DisposeAsync() => _disposeAsync();

    public static Task<StructuredLogStoreScenario> CreateInMemoryAsync(Action<StructuredLogsOptions>? configure = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddStructuredLogsServices(options =>
            {
                ApplySharedDefaults(options);
                configure?.Invoke(options);
            })
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        return Task.FromResult(new StructuredLogStoreScenario(
            services.GetRequiredService<IStructuredLogStore>(),
            services.GetRequiredService<IStructuredLogSourceRegistry>(),
            reportsRingDroppedEvents: true,
            () => ValueTask.CompletedTask,
            () => services.DisposeAsync()));
    }

    private static void ApplySharedDefaults(StructuredLogsOptions options)
    {
        options.RecentLogCapacity = 50;
        options.MaxRecentLogQuerySize = 25;
        options.SourceHeartbeatTimeout = TimeSpan.FromSeconds(30);
    }
}
