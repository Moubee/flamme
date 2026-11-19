using Flamme.Common;
using Flamme.Mediator.Contracts;
using Flamme.Scheduling.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Flamme.Scheduling.ScheduledTasks;

/// <summary>
/// A scheduled recurring task.
/// </summary>
public class ScheduledRecurringTask : IScheduledTask, IDisposable
{
    private readonly ITask _task;
    private readonly TimeProvider _timeProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledRecurringTask> _logger;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly CancellationToken _cancellationToken;
    private readonly SemaphoreSlim _executionSemaphore = new(1, 1);
    private DateTimeOffset _startAt;
    private Timer? _timer;
    private bool _executing;
    private bool _cancellationRequested;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="ScheduledRecurringTask"/>.
    /// </summary>
    public ScheduledRecurringTask(ITask task, DateTimeOffset startAt, TimeSpan interval, TimeProvider timeProvider, IServiceScopeFactory scopeFactory, ILogger<ScheduledRecurringTask> logger)
    {
        _task = task;
        _timeProvider = timeProvider;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _startAt = startAt;
        _interval = interval;
        _cancellationTokenSource = new();
        _cancellationToken = _cancellationTokenSource.Token;

        Schedule();
    }

    /// <inheritdoc />
    public void Cancel()
    {
        _timer?.Dispose();

        if (_executing)
        {
            _cancellationRequested = true;
            return;
        }

        _cancellationTokenSource.Cancel();
    }

    private void Schedule()
    {
        var startAt = _startAt;
        var adjusted = false;

        while (true)
        {
            var now = _timeProvider.GetUtcNow();
            var delay = startAt - now;

            if (!adjusted && delay <= TimeSpan.Zero)
            {
                adjusted = true;
                continue;
            }

            SetupTimer(delay);
            break;
        }
    }

    private void SetupTimer(TimeSpan delay)
    {
        // Handle edge cases where delay is zero or negative (e.g., due to clock drift, fast execution, or time alignment)
        // Instead of silently returning, use a minimum delay to ensure the timer fires and workflow continues scheduling
        if (delay <= TimeSpan.Zero)
        {
            _logger.LogWarning("Calculated delay is {Delay} which is not positive. Using minimum delay of 1ms to ensure timer fires", delay);
            delay = TimeSpan.FromMilliseconds(1);
        }

        _timer = new(delay.TotalMilliseconds)
        {
            Enabled = true
        };

        _timer.Elapsed += async (_, _) => await OnElapsedAsync();
    }

    // Timer events cannot return a Task to their caller, so contain failures across the entire callback.
    private async Task OnElapsedAsync()
    {
        try
        {
            _timer?.Dispose();
            _timer = null;

            // Check if disposed before proceeding
            if (_disposed) return;

            _startAt = _timeProvider.GetUtcNow() + _interval;

            using var scope = _scopeFactory.CreateScope();
            var commandSender = scope.ServiceProvider.GetRequiredService<ICommandSender>();

            // Stop a callback whose scope was resolved during disposal
            if (_disposed) return;

            var cancellationToken = _cancellationToken;
            if (!cancellationToken.IsCancellationRequested)
            {
                var acquired = false;
                try
                {
                    acquired = await _executionSemaphore.WaitAsync(0, cancellationToken);
                    if (!acquired) return;
                    _executing = true;
                    await commandSender.SendAsync(new RunScheduledTask(_task), cancellationToken);

                    if (_cancellationRequested)
                    {
                        _cancellationRequested = false;
                        _cancellationTokenSource.Cancel();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception e)
                {
                    if (!_disposed)
                    {
                        try
                        {
                            _logger.LogError(e, "Error executing scheduled task");
                        }
                        catch
                        {
                            // Logging infrastructure may already be disposed during shutdown.
                        }
                    }
                }
                finally
                {
                    _executing = false;
                    if (acquired && !_disposed)
                        _executionSemaphore.Release();
                }
            }

            if (!cancellationToken.IsCancellationRequested && !_disposed)
                Schedule();
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            if (!_disposed)
            {
                try
                {
                    _logger.LogError(e, "Error executing scheduled task");
                }
                catch
                {
                    // Logging infrastructure may already be disposed during shutdown.
                }
            }
        }
    }

    void IDisposable.Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _cancellationTokenSource.Dispose();
        _executionSemaphore.Dispose();
    }
}