using Microsoft.Extensions.Time.Testing;
using Elsa.ExternalAuthentication.Models;
using Elsa.ExternalAuthentication.Stores.InMemory;
using System.Threading.Tasks;

namespace Elsa.ExternalAuthentication.UnitTests.Foundational;

public class InMemoryOneTimeStoresTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task StateStoreConsumesExactlyOneConcurrentAttempt()
    {
        var store = new InMemoryExternalAuthenticationStateStore(new FakeTimeProvider(_now));
        await store.PutAsync("sign-in", "state-hash", "payload", _now.AddMinutes(1));

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => store.TryTakeAsync<string>("sign-in", "state-hash").AsTask()));

        await Assert.That(results.OfType<TakeResult<string>.Taken>()).HasSingleItem();
        await Assert.That(results.OfType<TakeResult<string>.AlreadyConsumed>().Count()).IsEqualTo(15);
    }

    [Test]
    public async Task StateStoreReturnsExpiredWithoutRearmingTheHandle()
    {
        var clock = new FakeTimeProvider(_now);
        var store = new InMemoryExternalAuthenticationStateStore(clock);
        await store.PutAsync("sign-in", "state-hash", "payload", _now.AddSeconds(1));
        clock.SetUtcNow(_now.AddSeconds(1));

        var result = await store.TryTakeAsync<string>("sign-in", "state-hash");

        await Assert.That(result).IsOfType(typeof(TakeResult<string>.Expired));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.PutAsync("sign-in", "state-hash", "replacement", _now.AddMinutes(1)).AsTask());
    }

    [Test]
    public async Task AuthorizationGrantStoreConsumesAClonedGrantOnlyOnce()
    {
        var store = new InMemoryAuthorizationGrantStore(new FakeTimeProvider(_now));
        var grant = ExternalAuthenticationTestData.CreateGrant(_now.AddMinutes(1));
        await store.SaveAsync(grant);
        grant.UserId = "changed-after-save";

        var first = await store.TryTakeAsync(grant.CodeHash);
        var second = await store.TryTakeAsync(grant.CodeHash);

        await Assert.That(first).IsOfType(typeof(TakeResult<AuthorizationGrant>.Taken));
        var taken = (TakeResult<AuthorizationGrant>.Taken)first;
        await Assert.That(taken.Value.UserId).IsEqualTo("user-a");
        await Assert.That(second).IsOfType(typeof(TakeResult<AuthorizationGrant>.AlreadyConsumed));
    }

    [Test]
    public async Task AuthorizationGrantStoreConsumesExactlyOneConcurrentAttempt()
    {
        var store = new InMemoryAuthorizationGrantStore(new FakeTimeProvider(_now));
        var grant = ExternalAuthenticationTestData.CreateGrant(_now.AddMinutes(1));
        await store.SaveAsync(grant);

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => store.TryTakeAsync(grant.CodeHash).AsTask()));

        await Assert.That(results.OfType<TakeResult<AuthorizationGrant>.Taken>()).HasSingleItem();
        await Assert.That(results.OfType<TakeResult<AuthorizationGrant>.AlreadyConsumed>().Count()).IsEqualTo(15);
    }

    [Test]
    public async Task PreviewResultStoreRestrictsResultsToTheInitiatingAdministratorAndConsumesOnce()
    {
        var store = new InMemoryPreviewResultStore(new FakeTimeProvider(_now));
        var preview = ExternalAuthenticationTestData.CreatePreview(_now.AddMinutes(1));
        await store.SaveAsync(preview);

        var unauthorized = await store.TryTakeAsync(preview.HandleHash, "administrator-b");
        var first = await store.TryTakeAsync(preview.HandleHash, preview.AdministratorId);
        var second = await store.TryTakeAsync(preview.HandleHash, preview.AdministratorId);

        await Assert.That(unauthorized).IsOfType(typeof(TakeResult<PreviewResult>.NotFound));
        await Assert.That(first).IsOfType(typeof(TakeResult<PreviewResult>.Taken));
        await Assert.That(second).IsOfType(typeof(TakeResult<PreviewResult>.AlreadyConsumed));
    }

    [Test]
    public async Task PreviewResultStoreConsumesExactlyOneConcurrentAuthorizedAttempt()
    {
        var store = new InMemoryPreviewResultStore(new FakeTimeProvider(_now));
        var preview = ExternalAuthenticationTestData.CreatePreview(_now.AddMinutes(1));
        await store.SaveAsync(preview);

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => store.TryTakeAsync(preview.HandleHash, preview.AdministratorId).AsTask()));

        await Assert.That(results.OfType<TakeResult<PreviewResult>.Taken>()).HasSingleItem();
        await Assert.That(results.OfType<TakeResult<PreviewResult>.AlreadyConsumed>().Count()).IsEqualTo(15);
    }

    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(1)]
    public async Task OneTimeStores_ExpireAtTheExactUtcBoundary(int offsetTicks)
    {
        var fakeTime = new FakeTimeProvider(_now);
        var expiresAt = _now.AddSeconds(1).AddTicks(7);
        var stateStore = new InMemoryExternalAuthenticationStateStore(fakeTime);
        var grantStore = new InMemoryAuthorizationGrantStore(fakeTime);
        var previewStore = new InMemoryPreviewResultStore(fakeTime);
        var grant = ExternalAuthenticationTestData.CreateGrant(expiresAt);
        var preview = ExternalAuthenticationTestData.CreatePreview(expiresAt);
        await stateStore.PutAsync("sign-in", "state", "payload", expiresAt);
        await grantStore.SaveAsync(grant);
        await previewStore.SaveAsync(preview);
        fakeTime.Advance(expiresAt.AddTicks(offsetTicks) - _now);

        var stateResult = await stateStore.TryTakeAsync<string>("sign-in", "state");
        var grantResult = await grantStore.TryTakeAsync(grant.CodeHash);
        var previewResult = await previewStore.TryTakeAsync(preview.HandleHash, preview.AdministratorId);

        await Assert.That(stateResult).IsOfType(offsetTicks < 0 ? typeof(TakeResult<string>.Taken) : typeof(TakeResult<string>.Expired));
        await Assert.That(grantResult).IsOfType(offsetTicks < 0 ? typeof(TakeResult<AuthorizationGrant>.Taken) : typeof(TakeResult<AuthorizationGrant>.Expired));
        await Assert.That(previewResult).IsOfType(offsetTicks < 0 ? typeof(TakeResult<PreviewResult>.Taken) : typeof(TakeResult<PreviewResult>.Expired));
    }

}
