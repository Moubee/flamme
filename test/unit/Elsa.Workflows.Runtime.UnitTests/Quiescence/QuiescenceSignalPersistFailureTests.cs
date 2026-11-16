using Elsa.Common;
using Elsa.KeyValues.Contracts;
using Elsa.KeyValues.Entities;
using Elsa.KeyValues.Models;
using Elsa.Workflows.Runtime.Options;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.Time.Testing;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Quiescence;

/// <summary>
/// Failed persist must restore only pause fields. These tests fail if that revert is removed.
/// </summary>
public class QuiescenceSignalPersistFailureTests
{
    private const string PauseKey = "elsa.quiescence.host-pause.default";

    private readonly FakeTimeProvider _timeProvider;
    private readonly IExecutionCycleRegistry _cycleRegistry;

    public QuiescenceSignalPersistFailureTests()
    {
        _timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-04-24T10:00:00Z"));
        _cycleRegistry = IExecutionCycleRegistry.Mock().Object;
    }

    [Test]
    [DisplayName("Failed pause keeps the node unpaused and a retry persists")]
    public async Task FailedPause_LeavesUnpaused_AndRetryPersists()
    {
        var store = new ThrowingKeyValueStore { ThrowOnSave = true };
        var sut = CreateSignal(store);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            sut.PauseAsync("maintenance", "op", CancellationToken.None).AsTask());

        await Assert.That(ex.Message).IsEqualTo("boom-save");
        await Assert.That(sut.CurrentState.Reason.HasFlag(QuiescenceReason.AdministrativePause)).IsFalse();
        await Assert.That(store.Pairs.ContainsKey(PauseKey)).IsFalse();

        store.ThrowOnSave = false;
        await sut.PauseAsync("maintenance", "op", CancellationToken.None);

        await Assert.That(sut.CurrentState.Reason.HasFlag(QuiescenceReason.AdministrativePause)).IsTrue();
        await Assert.That(store.Pairs.ContainsKey(PauseKey)).IsTrue();
    }

    [Test]
    [DisplayName("Failed resume keeps the node paused")]
    public async Task FailedResume_LeavesPaused()
    {
        var store = new ThrowingKeyValueStore();
        var sut = CreateSignal(store);
        await sut.PauseAsync("maintenance", "op", CancellationToken.None);
        store.ThrowOnDelete = true;

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            sut.ResumeAsync("op", CancellationToken.None).AsTask());

        await Assert.That(ex.Message).IsEqualTo("boom-delete");
        await Assert.That(sut.CurrentState.Reason.HasFlag(QuiescenceReason.AdministrativePause)).IsTrue();
        await Assert.That(store.Pairs.ContainsKey(PauseKey)).IsTrue();
    }

    [Test]
    [DisplayName("Racing pause and resume that both fail leave live state matching the store")]
    public async Task ConcurrentFailedPauseAndResume_LiveMatchesStore()
    {
        var store = new GatedThrowingKeyValueStore { ThrowOnDelete = true };
        var sut = CreateSignal(store);

        var pauseTask = sut.PauseAsync("maintenance", "op", CancellationToken.None).AsTask();
        await store.SaveStarted.Task;
        var resumeTask = sut.ResumeAsync("op", CancellationToken.None).AsTask();
        for (var i = 0; i < 20 && sut.CurrentState.Reason.HasFlag(QuiescenceReason.AdministrativePause); i++)
            await Task.Delay(5);
        store.ReleaseThrow();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => pauseTask);
        await resumeTask;

        await Assert.That(sut.CurrentState.Reason.HasFlag(QuiescenceReason.AdministrativePause)).IsFalse();
        await Assert.That(store.Pairs.ContainsKey(PauseKey)).IsFalse();
    }

    [Test]
    [DisplayName("Drain during a failing pause keeps drain and rolls back the pause")]
    public async Task DrainDuringFailedPause_KeepsDrain_AndRollsBackPause()
    {
        var store = new GatedThrowingKeyValueStore();
        var sut = CreateSignal(store);

        var pauseTask = sut.PauseAsync("maintenance", "op", CancellationToken.None).AsTask();
        await store.SaveStarted.Task;
        await sut.BeginDrainAsync();
        store.ReleaseThrow();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => pauseTask);

        await Assert.That(sut.CurrentState.Reason.HasFlag(QuiescenceReason.Drain)).IsTrue();
        await Assert.That(sut.CurrentState.Reason.HasFlag(QuiescenceReason.AdministrativePause)).IsFalse();
        await Assert.That(store.Pairs.ContainsKey(PauseKey)).IsFalse();
    }

    private QuiescenceSignal CreateSignal(IKeyValueStore store) =>
        QuiescenceSignal.Create(
            Microsoft.Extensions.Options.Options.Create(new GracefulShutdownOptions
            {
                PausePersistence = PausePersistencePolicy.AcrossReactivations
            }),
            _timeProvider,
            _cycleRegistry,
            store);

    private sealed class ThrowingKeyValueStore : IKeyValueStore
    {
        public readonly Dictionary<string, SerializedKeyValuePair> Pairs = new(StringComparer.Ordinal);
        public bool ThrowOnSave { get; set; }
        public bool ThrowOnDelete { get; set; }

        public Task SaveAsync(SerializedKeyValuePair keyValuePair, CancellationToken cancellationToken)
        {
            if (ThrowOnSave)
                throw new InvalidOperationException("boom-save");
            Pairs[keyValuePair.Key] = keyValuePair;
            return Task.CompletedTask;
        }

        public Task<SerializedKeyValuePair?> FindAsync(KeyValueFilter filter, CancellationToken cancellationToken)
        {
            SerializedKeyValuePair? match = filter.Key is not null && Pairs.TryGetValue(filter.Key, out var pair) ? pair : null;
            return Task.FromResult(match);
        }

        public Task<IEnumerable<SerializedKeyValuePair>> FindManyAsync(KeyValueFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<SerializedKeyValuePair>>(Pairs.Values.ToArray());

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            if (ThrowOnDelete)
                throw new InvalidOperationException("boom-delete");
            Pairs.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class GatedThrowingKeyValueStore : IKeyValueStore
    {
        public readonly Dictionary<string, SerializedKeyValuePair> Pairs = new(StringComparer.Ordinal);
        public readonly TaskCompletionSource SaveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _throwGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ThrowOnDelete { get; set; }

        public void ReleaseThrow() => _throwGate.TrySetResult();

        public async Task SaveAsync(SerializedKeyValuePair keyValuePair, CancellationToken cancellationToken)
        {
            SaveStarted.TrySetResult();
            await _throwGate.Task;
            throw new InvalidOperationException("boom-save");
        }

        public Task<SerializedKeyValuePair?> FindAsync(KeyValueFilter filter, CancellationToken cancellationToken)
        {
            SerializedKeyValuePair? match = filter.Key is not null && Pairs.TryGetValue(filter.Key, out var pair) ? pair : null;
            return Task.FromResult(match);
        }

        public Task<IEnumerable<SerializedKeyValuePair>> FindManyAsync(KeyValueFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<SerializedKeyValuePair>>(Pairs.Values.ToArray());

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            if (ThrowOnDelete)
                throw new InvalidOperationException("boom-delete");
            Pairs.Remove(key);
            return Task.CompletedTask;
        }
    }
}
