using Flamme.ExternalAuthentication.Contracts;
using Flamme.ExternalAuthentication.Models;
using Flamme.ExternalAuthentication.Secrets.Services;
using Flamme.Secrets.Contracts;
using Flamme.Secrets.Models;
using System.Threading.Tasks;

namespace Flamme.ExternalAuthentication.UnitTests.Secrets;

public class FlammeSecretBindingResolverTests
{
    [Test]
    public async Task MissingBindingReportsOnlySafeState()
    {
        var manager = ISecretManager.Mock().Object;
        Mock.Get(manager).GetAsync("missing", Arg.Any<CancellationToken>()).ReturnsAsync(Task.FromResult<Secret?>(null));
        var resolver = new FlammeSecretBindingResolver(manager, new TestHasher());

        var state = await resolver.GetStateAsync(new SecretBinding(FlammeSecretBindingResolver.ResolverType, "missing"));

        await Assert.That(state.IsConfigured).IsFalse();
        await Assert.That(state.IsResolvable).IsFalse();
    }

    [Test]
    public async Task IncompatibleBindingDoesNotResolveOrTestTheSecret()
    {
        var manager = ISecretManager.Mock().Object;
        var secret = CreateSecret();
        Mock.Get(manager).GetAsync(secret.Name, Arg.Any<CancellationToken>()).ReturnsAsync(Task.FromResult<Secret?>(secret));
        var resolver = new FlammeSecretBindingResolver(manager, new TestHasher());
        var binding = new SecretBinding(FlammeSecretBindingResolver.ResolverType, secret.Name, "certificate", "other-scope");

        var state = await resolver.GetStateAsync(binding);

        await Assert.That(state.IsConfigured).IsTrue();
        await Assert.That(state.IsResolvable).IsFalse();
        Mock.Get(manager).TestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => resolver.ResolveAsync(binding).AsTask());
    }

    [Test]
    public async Task ResolutionReturnsTransientValueAndOpaqueStableGenerationThatChangesOnRotation()
    {
        var manager = ISecretManager.Mock().Object;
        var secret = CreateSecret();
        Mock.Get(manager).GetAsync(secret.Name, Arg.Any<CancellationToken>()).ReturnsAsync(Task.FromResult<Secret?>(secret));
        Mock.Get(manager).TestAsync(secret.Name, Arg.Any<CancellationToken>()).ReturnsAsync(Task.FromResult(new SecretTestResponse { Succeeded = true }));
        Mock.Get(manager).ResolvePayloadAsync(secret, Arg.Any<CancellationToken>()).ReturnsAsync(Task.FromResult(new SecretPayload { Value = "super-secret" }));
        var resolver = new FlammeSecretBindingResolver(manager, new TestHasher());
        var binding = new SecretBinding(FlammeSecretBindingResolver.ResolverType, secret.Name, SecretTypeNames.Text, "external-authentication");

        var state = await resolver.GetStateAsync(binding);
        var first = await resolver.ResolveAsync(binding);
        var sameGeneration = await resolver.ResolveAsync(binding);
        secret.Versions.Add(new SecretVersion
        {
            Version = 2,
            CreatedAt = DateTimeOffset.Parse("2026-07-24T12:00:00Z"),
            Payload = new SecretPayload()
        });
        secret.Versions[0].Status = SecretStatus.Retired;
        var rotated = await resolver.ResolveAsync(binding);
        try
        {
            await Assert.That(state.IsConfigured).IsTrue();
            await Assert.That(state.IsResolvable).IsTrue();
            await Assert.That(first.Value.Reveal()).IsEqualTo("super-secret");
            await Assert.That(sameGeneration.GenerationFingerprint).IsEqualTo(first.GenerationFingerprint);
            await Assert.That(rotated.GenerationFingerprint).IsNotEqualTo(first.GenerationFingerprint);
            await Assert.That(first.GenerationFingerprint).DoesNotContain("super-secret").WithComparison(StringComparison.Ordinal);
        }
        finally
        {
            first.Value.Dispose();
            sameGeneration.Value.Dispose();
            rotated.Value.Dispose();
        }
    }

    [Test]
    public async Task ManagedReplaceStagesUniqueManagedBindingWithoutExposingTheValue()
    {
        var manager = ISecretManager.Mock().Object;
        Mock.Get(manager).GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsAsync(Task.FromResult<Secret?>(null));
        Mock.Get(manager).CreateAsync(Arg.Any<CreateSecretRequest>(), Arg.Any<CancellationToken>()).Returns((CreateSecretRequest request, CancellationToken _) => new Secret { Name = request.Name });
        var resolver = new FlammeSecretBindingResolver(manager, new TestHasher());

        using var value = new SensitiveString("super-secret");
        var binding = await resolver.StageAsync(new ManagedSecretBindingWriteRequest("connection-a", "clientSecret", value));
        var replacementBinding = await resolver.StageAsync(new ManagedSecretBindingWriteRequest("connection-a", "clientSecret", value));

        await Assert.That(binding.Ownership).IsEqualTo(SecretBindingOwnership.Managed);
        await Assert.That(binding.ResolverType).IsEqualTo(FlammeSecretBindingResolver.ResolverType);
        await Assert.That(binding.Reference).StartsWith("external-authentication:").WithComparison(StringComparison.Ordinal);
        await Assert.That(Guid.TryParseExact(binding.Reference["external-authentication:".Length..], "N", out _)).IsTrue();
        await Assert.That(replacementBinding.Reference).IsNotEqualTo(binding.Reference);
        await Assert.That(binding.Reference).DoesNotContain("super-secret").WithComparison(StringComparison.Ordinal);
        Mock.Get(manager).CreateAsync(Arg.Is<CreateSecretRequest>(x => x.Value == "super-secret"), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(2));
        Mock.Get(manager).RotateAsync(Arg.Any<string>(), Arg.Any<RotateSecretRequest>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task RemovingManagedBindingDeletesOnlyStudioOwnedSecretMaterial()
    {
        var manager = ISecretManager.Mock().Object;
        Mock.Get(manager).DeleteAsync("managed-secret", Arg.Any<CancellationToken>()).ReturnsAsync(Task.FromResult(true));
        var resolver = new FlammeSecretBindingResolver(manager, new TestHasher());

        await resolver.RemoveAsync(new SecretBinding(FlammeSecretBindingResolver.ResolverType, "managed-secret", Ownership: SecretBindingOwnership.Managed));

        Mock.Get(manager).DeleteAsync("managed-secret", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => resolver.RemoveAsync(new SecretBinding(FlammeSecretBindingResolver.ResolverType, "deployment-secret")).AsTask());
        Mock.Get(manager).DeleteAsync("deployment-secret", Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    private static Secret CreateSecret() => new()
    {
        Id = "secret-id",
        Name = "contoso-client-secret",
        DisplayName = "Contoso client secret",
        TypeName = SecretTypeNames.Text,
        Scope = "external-authentication",
        Status = SecretStatus.Active,
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                CreatedAt = DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
                Payload = new SecretPayload()
            }
        ]
    };

    private sealed class TestHasher : IExternalAuthenticationHandleHasher
    {
        public string Hash(string value) => $"fingerprint:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))}";
    }
}
