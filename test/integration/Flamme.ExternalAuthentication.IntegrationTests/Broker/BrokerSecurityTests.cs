using Microsoft.Extensions.Time.Testing;
using Elsa.Testing.Shared.Multitenancy;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Common;
using Elsa.Common.Multitenancy;
using Elsa.Common.Services;
using Elsa.ExternalAuthentication.Contracts;
using Elsa.ExternalAuthentication.Models;
using Elsa.ExternalAuthentication.Notifications;
using Elsa.ExternalAuthentication.Options;
using Elsa.ExternalAuthentication.Policies;
using Elsa.ExternalAuthentication.Services;
using Elsa.ExternalAuthentication.Stores.InMemory;
using Elsa.Identity.Contracts;
using Elsa.Identity.Entities;
using Elsa.Identity.Models;
using Elsa.Identity.Options;
using Elsa.Identity.Providers;
using Elsa.Identity.Services;
using Elsa.Mediator.Contracts;
using Elsa.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Elsa.ExternalAuthentication.IntegrationTests.Broker;

public class BrokerSecurityTests
{
    [Test]
    public async Task LocalAuthorizationCodeExchangeAndRefreshResolveCurrentPermissionsInTheTokenTenant()
    {
        const string verifier = "local-login-code-verifier";
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var tenantAccessor = new DefaultTenantAccessor();
        var user = new User { Id = "user-a", Name = "admin", TenantId = "tenant-a", Roles = ["admin"] };
        var role = new Role { Id = "admin", Name = "Administrator", TenantId = "tenant-a", Permissions = ["*"] };
        var credentials = IUserCredentialsValidator.Mock().Object;
        Mock.Get(credentials).ValidateAsync("admin", "password", Arg.Any<CancellationToken>())
            .ReturnsAsync(ValueTask.FromResult<User?>(user));
        var users = IUserProvider.Mock().Object;
        Mock.Get(users).FindAsync(Arg.Any<UserFilter>(), Arg.Any<CancellationToken>())
            .Returns(() => Task.FromResult(tenantAccessor.TenantId == "tenant-a" ? user : null));
        var roles = IRoleProvider.Mock().Object;
        Mock.Get(roles).FindManyAsync(Arg.Any<RoleFilter>(), Arg.Any<CancellationToken>())
            .Returns(() => ValueTask.FromResult<IEnumerable<Role>>(tenantAccessor.TenantId == "tenant-a" ? [role] : []));
        var local = CreateLocalIdentityServices(users, roles, tenantAccessor);
        var externalTokenIssuer = IExternalAuthenticationTokenIssuer.Mock().Object;
        Mock.Get(externalTokenIssuer).RefreshAsync("studio", Arg.Any<SensitiveString>(), Arg.Any<CancellationToken>())
            .ReturnsAsync(ValueTask.FromException<ExternalTokenResponse>(new InvalidOperationException("A local refresh token must not use the external-session issuer.")));
        var broker = CreateBroker(
            new RecordingAdapter(),
            tokenIssuer: externalTokenIssuer,
            credentialsValidator: credentials,
            userProvider: users,
            roleProvider: roles,
            tokenService: local.Tokens,
            identityRefreshTokenService: local.RefreshTokens,
            tenantAccessor: tenantAccessor);
        BrokerCallbackResult authorization;
        using (tenantAccessor.PushContext(new Tenant { Id = "tenant-a", Name = "Tenant A" }))
        {
            authorization = await broker.InitiateLocalAsync(
                new LocalBrokerAuthorizationRequest(
                    "studio",
                    new Uri("https://studio.example/authentication/external/callback"),
                    "code",
                    challenge,
                    "S256",
                    "/workflows",
                    "admin",
                    "password"),
                "tenant-a");
        }
        var code = Query(authorization.RedirectUri!, "code");

        BrokerTokenResult exchange;
        using (tenantAccessor.PushContext(new Tenant { Id = "tenant-b", Name = "Tenant B" }))
        {
            exchange = await broker.ExchangeAsync(new BrokerTokenRequest(
                "authorization_code",
                "studio",
                new Uri("https://studio.example/authentication/external/callback"),
                code,
                verifier,
                null,
                "https://studio.example"));

            await Assert.That(tenantAccessor.TenantId).IsEqualTo("tenant-b");
        }

        await Assert.That(exchange.Error).IsNull();
        var accessToken = new JsonWebTokenHandler().ReadJsonWebToken(exchange.Token!.AccessToken);
        await Assert.That(accessToken.Claims).Contains(claim => claim.Type == JwtRegisteredClaimNames.Sub && claim.Value == user.Id);
        await Assert.That(accessToken.Claims).Contains(claim => claim.Type == "permissions" && claim.Value == "*");

        role.Permissions = ["workflows:manage"];
        var refresh = await broker.ExchangeAsync(new BrokerTokenRequest(
            "refresh_token",
            "studio",
            null,
            null,
            null,
            exchange.Token.RefreshToken,
            "https://studio.example"));

        await Assert.That(refresh.Error).IsNull();
        await Assert.That(refresh.Token).IsNotNull();
        await Assert.That(exchange.Token.RefreshExpiresIn > 0).IsTrue();
        await Assert.That(refresh.Token.RefreshExpiresIn > 0).IsTrue();
        var refreshedAccessToken = new JsonWebTokenHandler().ReadJsonWebToken(refresh.Token.AccessToken);
        await Assert.That(refreshedAccessToken.Claims).Contains(claim => claim.Type == "permissions" && claim.Value == "workflows:manage");
        await Assert.That(refreshedAccessToken.Claims).DoesNotContain(claim => claim.Type == "permissions" && claim.Value == "*");
        Mock.Get(externalTokenIssuer).RefreshAsync(Arg.Any<string>(), Arg.Any<SensitiveString>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task LocalRefreshGrantRefusesARevokedSession()
    {
        var user = new User { Id = "user-a", Name = "admin" };
        var users = IUserProvider.Mock().Object;
        Mock.Get(users).FindAsync(Arg.Any<UserFilter>(), Arg.Any<CancellationToken>()).ReturnsAsync(Task.FromResult<User?>(user));
        var roles = IRoleProvider.Mock().Object;
        Mock.Get(roles).FindManyAsync(Arg.Any<RoleFilter>(), Arg.Any<CancellationToken>()).ReturnsAsync(ValueTask.FromResult<IEnumerable<Role>>([]));
        var tenantAccessor = new DefaultTenantAccessor();
        var local = CreateLocalIdentityServices(users, roles, tenantAccessor);
        var broker = CreateBroker(
            new RecordingAdapter(),
            userProvider: users,
            roleProvider: roles,
            identityRefreshTokenService: local.RefreshTokens,
            tenantAccessor: tenantAccessor);
        var session = new SignInSession("session-a", DateTimeOffset.UtcNow);
        var refreshToken = (await local.AccessTokenIssuer.IssueTokensAsync(user, session)).RefreshToken;
        var request = new BrokerTokenRequest("refresh_token", "studio", null, null, null, refreshToken, "https://studio.example");

        await Assert.That((await broker.ExchangeAsync(request)).Error).IsNull();

        await local.SessionRevoker.RevokeAsync(session);

        var refused = await broker.ExchangeAsync(request);
        await Assert.That(refused.Error).IsNotNull();
        await Assert.That(refused.Token).IsNull();
    }

    [Test]
    public async Task LocalInitiationTreatsNullUserTenantAsTheDefaultTenant()
    {
        var credentials = IUserCredentialsValidator.Mock().Object;
        Mock.Get(credentials).ValidateAsync("admin", "password", Arg.Any<CancellationToken>())
            .ReturnsAsync(ValueTask.FromResult<User?>(new User { Id = "admin", Name = "admin" }));
        var broker = CreateBroker(new RecordingAdapter(), credentialsValidator: credentials);
        var request = new LocalBrokerAuthorizationRequest(
            "studio", new Uri("https://studio.example/authentication/external/callback"), "code", "challenge", "S256", "/workflows", "admin", "password", "state");

        var result = await broker.InitiateLocalAsync(request, Tenant.DefaultTenantId);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.RedirectUri?.AbsoluteUri).StartsWith("https://studio.example/authentication/external/callback?code=");
        await Assert.That(result.RedirectUri?.Query).Contains("state=state");
    }

    [Test]
    public async Task OpaqueRefreshTokensContinueToUseTheExternalSessionIssuer()
    {
        var expected = new ExternalTokenResponse("access", "Bearer", 300, "session.rotated", 600, 600);
        var externalTokenIssuer = IExternalAuthenticationTokenIssuer.Mock().Object;
        Mock.Get(externalTokenIssuer).RefreshAsync("studio", Arg.Any<SensitiveString>(), Arg.Any<CancellationToken>()).Returns(expected);
        var identityRefreshTokenService = IIdentityRefreshTokenService.Mock().Object;
        var broker = CreateBroker(
            new RecordingAdapter(),
            tokenIssuer: externalTokenIssuer,
            identityRefreshTokenService: identityRefreshTokenService);

        var result = await broker.ExchangeAsync(new BrokerTokenRequest(
            "refresh_token",
            "studio",
            null,
            null,
            null,
            "session.random",
            "https://studio.example"));

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Token).IsSameReferenceAs(expected);
        Mock.Get(identityRefreshTokenService).RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ExternalInitiationUsesExactlyOneOpaqueProviderStateAndPersistsAdapterPayload()
    {
        var adapter = new RecordingAdapter();
        var broker = CreateBroker(adapter);
        var request = Request("/workflows");

        var result = await broker.InitiateExternalAsync(request, "tenant-a");

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.NavigationUri).IsNotNull();
        await Assert.That(Query(result.NavigationUri!, "state")).IsEqualTo(adapter.CorrelationState);
        await Assert.That(adapter.CorrelationState).IsNotEqualTo(adapter.Transaction!.HandleHash);
        await Assert.That(adapter.Transaction.ProtectedPayload).IsNotEquivalentTo(
            new byte[] { 1, 2, 3 },
            EqualityComparer<byte>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("//evil.example")]
    [Arguments("/administration")]
    public async Task InitiationRejectsReturnPathsOutsideTheAuthenticationClientAllowlist(string returnPath)
    {
        var adapter = new RecordingAdapter();
        var broker = CreateBroker(adapter);

        var result = await broker.InitiateExternalAsync(Request(returnPath), "tenant-a");

        await Assert.That(result.Error?.Error).IsEqualTo("invalid_request");
        await Assert.That(adapter.CorrelationState).IsNull();
    }

    [Test]
    public async Task CallbackFailureAfterTrustedStateRedirectsOnlyToRegisteredCallback()
    {
        var adapter = new RecordingAdapter { ThrowOnCallback = true };
        var broker = CreateBroker(adapter);
        var initiated = await broker.InitiateExternalAsync(Request("/workflows"), "tenant-a");

        var result = await broker.CompleteCallbackAsync("contoso", adapter.CorrelationState!, new Dictionary<string, IReadOnlyCollection<string>> { ["state"] = [adapter.CorrelationState!] });

        await Assert.That(result.Error?.Error).IsEqualTo("authentication_failed");
        await Assert.That(result.RedirectUri?.AbsoluteUri).StartsWith("https://studio.example/authentication/external/callback?");
        await Assert.That(result.RedirectUri?.AbsoluteUri).DoesNotContain("issuer.example");
    }

    [Test]
    public async Task SuccessfulExternalSignInRecordsTheTimestampForAnExistingIdentityLink()
    {
        var scenario = CreateIdentityLinkTrackingScenario();
        var existing = await scenario.Provisioner.CreateLinkOrGetExistingAsync(new ProvisioningRequest("tenant-a", "contoso", scenario.Identity, new UserCreationProposal("external")));
        var signedInAt = new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero);
        scenario.Clock.SetUtcNow(signedInAt);

        var result = await CompleteExternalSignInAsync(scenario);
        var link = await scenario.Provisioner.FindLinkAsync("tenant-a", "contoso", scenario.Identity);

        await Assert.That(result.Error).IsNull();
        await Assert.That(link?.Id).IsEqualTo(existing.Link.Id);
        await Assert.That(link?.LastSignedInAt).IsEqualTo(signedInAt);
    }

    [Test]
    public async Task SuccessfulExternalSignInRecordsTheInitialTimestampForANewIdentityLink()
    {
        var scenario = CreateIdentityLinkTrackingScenario();
        var signedInAt = new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero);
        scenario.Clock.SetUtcNow(signedInAt);

        var result = await CompleteExternalSignInAsync(scenario);
        var link = await scenario.Provisioner.FindLinkAsync("tenant-a", "contoso", scenario.Identity);

        await Assert.That(result.Error).IsNull();
        await Assert.That(link).IsNotNull();
        await Assert.That(link.LastSignedInAt).IsEqualTo(signedInAt);
    }

    [Test]
    public async Task RepeatSuccessfulExternalSignInReplacesTheIdentityLinkTimestamp()
    {
        var scenario = CreateIdentityLinkTrackingScenario();
        await scenario.Provisioner.CreateLinkOrGetExistingAsync(new ProvisioningRequest("tenant-a", "contoso", scenario.Identity, new UserCreationProposal("external")));
        var firstSignInAt = new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero);
        scenario.Clock.SetUtcNow(firstSignInAt);
        await CompleteExternalSignInAsync(scenario);
        var secondSignInAt = firstSignInAt.AddMinutes(1);
        scenario.Clock.SetUtcNow(secondSignInAt);

        var result = await CompleteExternalSignInAsync(scenario);
        var link = await scenario.Provisioner.FindLinkAsync("tenant-a", "contoso", scenario.Identity);

        await Assert.That(result.Error).IsNull();
        await Assert.That(link?.LastSignedInAt).IsEqualTo(secondSignInAt);
    }

    [Test]
    public async Task UnsuccessfulExternalSignInDoesNotRecordTheIdentityLinkTimestamp()
    {
        var scenario = CreateIdentityLinkTrackingScenario(throwOnCallback: true);
        await scenario.Provisioner.CreateLinkOrGetExistingAsync(new ProvisioningRequest("tenant-a", "contoso", scenario.Identity, new UserCreationProposal("external")));
        scenario.Clock.SetUtcNow(new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero));

        var result = await CompleteExternalSignInAsync(scenario);
        var link = await scenario.Provisioner.FindLinkAsync("tenant-a", "contoso", scenario.Identity);

        await Assert.That(result.Error?.Error).IsEqualTo("authentication_failed");
        await Assert.That(link?.LastSignedInAt).IsNull();
    }

    [Test]
    public async Task ProviderCallbackStateCannotBeReplayed()
    {
        var adapter = new RecordingAdapter { ThrowOnCallback = true };
        var broker = CreateBroker(adapter);
        await broker.InitiateExternalAsync(Request("/workflows"), "tenant-a");
        var parameters = new Dictionary<string, IReadOnlyCollection<string>> { ["state"] = [adapter.CorrelationState!] };

        _ = await broker.CompleteCallbackAsync("contoso", adapter.CorrelationState!, parameters);
        var replay = await broker.CompleteCallbackAsync("contoso", adapter.CorrelationState!, parameters);

        await Assert.That(replay.Error?.Error).IsEqualTo("invalid_request");
        await Assert.That(replay.RedirectUri).IsNull();
    }

    [Test]
    [Arguments("revision")]
    [Arguments("disabled")]
    [Arguments("archived")]
    public async Task CallbackRejectsConnectionChangesAfterInitiation(string change)
    {
        var adapter = new RecordingAdapter { ThrowOnCallback = true };
        var broker = CreateBroker(adapter);
        await broker.InitiateExternalAsync(Request("/workflows"), "tenant-a");
        var connection = adapter.Connection!.Connection;
        switch (change)
        {
            case "revision": connection.MaterialRevision = "revision-b"; break;
            case "disabled": connection.IsEnabled = false; break;
            case "archived": connection.ArchivedAt = DateTimeOffset.UtcNow; break;
        }

        var result = await broker.CompleteCallbackAsync("contoso", adapter.CorrelationState!, new Dictionary<string, IReadOnlyCollection<string>> { ["state"] = [adapter.CorrelationState!] });

        await Assert.That(result.Error?.Error).IsEqualTo(change == "revision" ? "flow_changed" : "method_unavailable");
        await Assert.That(result.RedirectUri?.AbsoluteUri).StartsWith("https://studio.example/authentication/external/callback?");
    }

    [Test]
    public async Task RefreshRotationRevokesTheSessionWhenAnOlderTokenIsReused()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new InMemoryExternalAuthenticationSessionStore(clock);
        var connection = new IdentityProviderConnection { Id = "connection-a", TenantId = "tenant-a", Key = "contoso", AdapterType = "fake", DisplayName = "Contoso", IsEnabled = true, MaterialRevision = "revision-a" };
        var effective = new EffectiveIdentityProviderConnection(connection, ConnectionSourceOwnership.Configuration, new(ConnectionScopeKind.Tenant, "tenant-a"), ConnectionValidity.Valid, false, "test");
        var registry = IIdentityProviderConnectionRegistry.Mock().Object;
        Mock.Get(registry).FindByKeyAsync("tenant-a", "contoso", Arg.Any<CancellationToken>()).ReturnsAsync(ValueTask.FromResult<EffectiveIdentityProviderConnection?>(effective));
        var users = IUserProvider.Mock().Object;
        Mock.Get(users).FindAsync(Arg.Any<UserFilter>(), Arg.Any<CancellationToken>()).ReturnsAsync(Task.FromResult<User?>(new User { Id = "user-a", Name = "alice", TenantId = "tenant-a" }));
        var roles = IRoleProvider.Mock().Object;
        Mock.Get(roles).FindManyAsync(Arg.Any<RoleFilter>(), Arg.Any<CancellationToken>()).ReturnsAsync(ValueTask.FromResult<IEnumerable<Role>>([]));
        var tokens = IElsaTokenService.Mock().Object;
        Mock.Get(tokens).IssueAccessTokenAsync(Arg.Any<TokenIssuanceContext>(), Arg.Any<CancellationToken>()).ReturnsAsync(ValueTask.FromResult(new IssuedAccessToken("access", clock.GetUtcNow().AddHours(1))));
        var issuer = new DefaultExternalAuthenticationTokenIssuer(store, registry, [], users, roles, tokens, new DefaultTenantAccessor(), clock, Microsoft.Extensions.Options.Options.Create(new ExternalAuthenticationOptions()));
        var session = new ExternalAuthenticationSession { Id = "session-a", AuthenticationClientId = "studio", TenantId = "tenant-a", UserId = "user-a", ConnectionKey = "contoso", ConnectionMaterialRevision = "revision-a", SecretGenerationFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([])), Issuer = "issuer", SubjectHash = "subject", StartedAt = clock.GetUtcNow(), LastRefreshedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow().AddHours(1), RefreshExpiresAt = clock.GetUtcNow().AddHours(1) };

        var first = await issuer.IssueAsync(session);
        var second = await issuer.RefreshAsync("studio", new SensitiveString(first.RefreshToken));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => issuer.RefreshAsync("studio", new SensitiveString(first.RefreshToken)).AsTask());
        var revoked = await store.FindByIdAsync(session.Id);

        await Assert.That(second.RefreshToken).IsNotEqualTo(first.RefreshToken);
        await Assert.That(revoked?.RevokedAt).IsNotNull();
        await Assert.That(revoked?.RevocationReason).IsEqualTo("refresh_token_reuse");
    }

    [Test]
    public async Task PkceMismatchConsumesTheAuthorizationCode()
    {
        var grants = new InMemoryAuthorizationGrantStore(new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        var broker = CreateBroker(new RecordingAdapter(), grants, new FixedHasher());
        await grants.SaveAsync(new AuthorizationGrant { CodeHash = "hash:code", ClientId = "studio", CallbackUri = new Uri("https://studio.example/authentication/external/callback"), TenantId = "tenant-a", UserId = "user-a", PkceChallenge = "expected", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) });

        var result = await broker.ExchangeAsync(new BrokerTokenRequest("authorization_code", "studio", new Uri("https://studio.example/authentication/external/callback"), "code", "wrong", null, "https://studio.example"));
        var after = await grants.TryTakeAsync("hash:code");

        await Assert.That(result.Error?.Error).IsEqualTo("invalid_request");
        await Assert.That(after).IsOfType(typeof(TakeResult<AuthorizationGrant>.AlreadyConsumed));
    }

    [Test]
    public async Task InitiationRejectsAnExactCallbackUriMismatch()
    {
        var adapter = new RecordingAdapter();
        var broker = CreateBroker(adapter);

        var result = await broker.InitiateExternalAsync(new BrokerAuthorizationRequest("studio", new Uri("https://studio.example/other"), "code", "challenge", "S256", "/workflows", "contoso"), "tenant-a");

        await Assert.That(result.Error?.Error).IsEqualTo("invalid_request");
        await Assert.That(adapter.CorrelationState).IsNull();
    }

    [Test]
    public async Task ExchangeRequiresAnExactPublicOriginAndConfidentialBasicClientId()
    {
        var publicBroker = CreateBroker(new RecordingAdapter());
        var publicResult = await publicBroker.ExchangeAsync(new BrokerTokenRequest("authorization_code", "studio", new Uri("https://studio.example/authentication/external/callback"), "code", "verifier", null, "https://studio.example.attacker"));

        var secretResolver = new MutableSecretResolver();
        var confidentialClient = new AuthenticationClient("confidential", "Confidential", AuthenticationClientType.Confidential, new HashSet<Uri> { new("https://studio.example/authentication/external/callback") }, new HashSet<Uri>(), new HashSet<string>(), new HashSet<string> { "/workflows" }, new SecretBinding("test", "client"), true);
        var confidentialBroker = CreateBroker(new RecordingAdapter(), clients: [confidentialClient], resolvers: [secretResolver]);
        var confidentialResult = await confidentialBroker.ExchangeAsync(new BrokerTokenRequest("authorization_code", "confidential", new Uri("https://studio.example/authentication/external/callback"), "code", "verifier", null, null, "other-client", "secret"));

        await Assert.That(publicResult.Error?.Error).IsEqualTo("invalid_request");
        await Assert.That(confidentialResult.Error?.Error).IsEqualTo("invalid_request");
    }

    [Test]
    public async Task SecretGenerationRotationInvalidatesTrustedCallback()
    {
        var resolver = new MutableSecretResolver();
        var adapter = new RecordingAdapter { ThrowOnCallback = true };
        var broker = CreateBroker(adapter, resolvers: [resolver], configureConnection: connection => connection.SecretBindings["clientSecret"] = new SecretBinding("test", "client"));
        await broker.InitiateExternalAsync(Request("/workflows"), "tenant-a");
        resolver.Generation = "generation-2";

        var result = await broker.CompleteCallbackAsync("contoso", adapter.CorrelationState!, new Dictionary<string, IReadOnlyCollection<string>> { ["state"] = [adapter.CorrelationState!] });

        await Assert.That(result.Error?.Error).IsEqualTo("flow_changed");
    }

    [Test]
    public async Task RejectedBrokerOutcomesPublishTheirSafePublicCategory()
    {
        var sender = INotificationSender.Mock().Object;
        await using var services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var notifier = new ExternalAuthenticationSecurityNotifier(services);
        var broker = CreateBroker(new RecordingAdapter(), notifier: notifier);

        var result = await broker.InitiateExternalAsync(Request("//attacker.example"), "tenant-a");

        await Assert.That(result.Error?.Error).IsEqualTo("invalid_request");
        Mock.Get(sender).SendAsync(
            Arg.Is<Elsa.Mediator.Contracts.INotification>(value => value is ExternalAuthenticationOutcomeRecorded notification &&
                notification.Flow == "external" &&
                notification.Stage == "initiate" &&
                notification.Category == "invalid_request" &&
                notification.Context.Outcome == SecurityEventOutcome.Rejected),
            Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task AdapterInitiationFailureReturnsASafeObservedOutcome()
    {
        var sender = INotificationSender.Mock().Object;
        await using var services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        var broker = CreateBroker(
            new RecordingAdapter { ThrowOnInitiation = true },
            notifier: new ExternalAuthenticationSecurityNotifier(services));

        var result = await broker.InitiateExternalAsync(Request("/workflows"), "tenant-a");

        await Assert.That(result.Error?.Error).IsEqualTo("temporarily_unavailable");
        Mock.Get(sender).SendAsync(
            Arg.Is<Elsa.Mediator.Contracts.INotification>(value => value is ExternalAuthenticationOutcomeRecorded notification &&
                notification.Category == "temporarily_unavailable" &&
                notification.Context.Outcome == SecurityEventOutcome.Failed),
            Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    internal static ExternalAuthenticationBroker CreateBroker(
        RecordingAdapter adapter,
        IAuthorizationGrantStore? grants = null,
        IExternalAuthenticationHandleHasher? hasher = null,
        IEnumerable<ISecretBindingResolver>? resolvers = null,
        IReadOnlyCollection<AuthenticationClient>? clients = null,
        Action<IdentityProviderConnection>? configureConnection = null,
        IExternalIdentityResolver? identityResolver = null,
        IPermissionGrantResolver? permissionGrantResolver = null,
        IExternalAuthenticationSessionStore? sessionStore = null,
        IExternalAuthenticationTokenIssuer? tokenIssuer = null,
        IUserCredentialsValidator? credentialsValidator = null,
        IUserProvider? userProvider = null,
        IRoleProvider? roleProvider = null,
        IElsaTokenService? tokenService = null,
        IIdentityRefreshTokenService? identityRefreshTokenService = null,
        ITenantAccessor? tenantAccessor = null,
        ExternalAuthenticationSecurityNotifier? notifier = null,
        ConnectionValidity connectionValidity = ConnectionValidity.Valid,
        ConnectionValidity? assessedValidity = null,
        bool includeLoginMethod = false,
        TimeProvider? clock = null)
    {
        var connection = new IdentityProviderConnection
        {
            Id = "connection-a", TenantId = "tenant-a", Key = "contoso", AdapterType = "fake", AdapterSettings = JsonElement.Parse("{}"),
            DisplayName = "Contoso", IsEnabled = true, MaterialRevision = "revision-a"
        };
        configureConnection?.Invoke(connection);
        var effective = new EffectiveIdentityProviderConnection(connection, ConnectionSourceOwnership.Configuration, new(ConnectionScopeKind.Tenant, "tenant-a"), connectionValidity, false, "test");
        var registry = IIdentityProviderConnectionRegistry.Mock().Object;
        Mock.Get(registry).FindByKeyAsync("tenant-a", "contoso", Arg.Any<CancellationToken>()).ReturnsAsync(ValueTask.FromResult<EffectiveIdentityProviderConnection?>(effective));
        Mock.Get(registry).FindByIdAsync("tenant-a", "connection-a", Arg.Any<CancellationToken>()).ReturnsAsync(ValueTask.FromResult<EffectiveIdentityProviderConnection?>(effective));
        IReadOnlyCollection<LoginMethod> loginMethods = includeLoginMethod
            ? [new LoginMethod(connection.Id, connection.Key, LoginMethodKind.External, connection.DisplayName, null, 0, false, new Uri($"/external-authentication/authorize/{connection.Key}", UriKind.Relative))]
            : [];
        Mock.Get(registry).GetAsync("tenant-a", Arg.Any<CancellationToken>()).ReturnsAsync(ValueTask.FromResult(new EffectiveConnectionRegistry([effective], loginMethods, "v1")));
        var validityAssessor = IIdentityProviderConnectionValidityAssessor.Mock().Object;
        Mock.Get(validityAssessor).AssessAsync(Arg.Any<EffectiveIdentityProviderConnection>(), Arg.Any<CancellationToken>())
            .Returns((EffectiveIdentityProviderConnection candidate, CancellationToken cancellationToken) => ValueTask.FromResult(candidate with
            {
                Validity = assessedValidity ?? candidate.Validity
            }));
        var options = Microsoft.Extensions.Options.Options.Create(new ExternalAuthenticationOptions
        {
            Clients = clients?.ToList() ?? [new AuthenticationClient("studio", "Studio", AuthenticationClientType.Public,
                new HashSet<Uri> { new("https://studio.example/authentication/external/callback") }, new HashSet<Uri>(), new HashSet<string> { "https://studio.example" }, new HashSet<string> { "/workflows" }, null, true)]
        });
        var brokerClock = clock ?? new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        return new ExternalAuthenticationBroker(registry, validityAssessor, [adapter], resolvers ?? [], hasher ?? new HmacExternalAuthenticationHandleHasher(), new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(), identityResolver ?? IExternalIdentityResolver.Mock().Object, permissionGrantResolver ?? IPermissionGrantResolver.Mock().Object, new InMemoryExternalAuthenticationStateStore(brokerClock), grants ?? new InMemoryAuthorizationGrantStore(brokerClock), sessionStore ?? new InMemoryExternalAuthenticationSessionStore(brokerClock), tokenIssuer ?? IExternalAuthenticationTokenIssuer.Mock().Object, credentialsValidator ?? IUserCredentialsValidator.Mock().Object, userProvider ?? IUserProvider.Mock().Object, roleProvider ?? IRoleProvider.Mock().Object, tokenService ?? IElsaTokenService.Mock().Object, identityRefreshTokenService ?? IIdentityRefreshTokenService.Mock().Object, tenantAccessor ?? new DefaultTenantAccessor(), brokerClock, options, notifier);
    }

    private static BrokerAuthorizationRequest Request(string returnPath) => new("studio", new Uri("https://studio.example/authentication/external/callback"), "code", "challenge", "S256", returnPath, "contoso");
    private static string? Query(Uri uri, string key) => System.Web.HttpUtility.ParseQueryString(uri.Query)[key];

    private static IdentityLinkTrackingScenario CreateIdentityLinkTrackingScenario(bool throwOnCallback = false)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var identity = new ExternalIdentity("https://issuer.example", "subject-a", new Dictionary<string, IReadOnlyCollection<string>>());
        var users = new MemoryUserStore(new MemoryStore<User>(), new TestTenantAccessor("tenant-a"));
        var provisioner = new InMemoryExternalIdentityProvisioner(
            users,
            new StoreBasedUserProvider(users),
            IRoleProvider.Mock().Object,
            new GuidIdentityGenerator(),
            clock,
            new FixedHasher(),
            new InMemoryExternalIdentityProvisionerState());
        var identityResolver = new DefaultExternalIdentityResolver(
            provisioner,
            [new CreateUserUnlinkedIdentityPolicy()],
            Microsoft.Extensions.Options.Options.Create(new ExternalAuthenticationOptions
            {
                UnlinkedIdentityPolicy = new UnlinkedIdentityPolicyOptions { DefaultType = "create-user" }
            }));
        var permissionGrantResolver = IPermissionGrantResolver.Mock().Object;
        Mock.Get(permissionGrantResolver).ResolveAsync(Arg.Any<PermissionGrantResolutionContext>(), Arg.Any<CancellationToken>())
            .ReturnsAsync(ValueTask.FromResult(new PermissionGrantResult([], [])));
        var adapter = new RecordingAdapter
        {
            ThrowOnCallback = throwOnCallback,
            AuthenticationResult = new ExternalAuthenticationResult(identity, identity.Claims, [])
        };
        var broker = CreateBroker(adapter, identityResolver: identityResolver, permissionGrantResolver: permissionGrantResolver, clock: clock);

        return new IdentityLinkTrackingScenario(broker, provisioner, adapter, identity, clock);
    }

    private static async Task<BrokerCallbackResult> CompleteExternalSignInAsync(IdentityLinkTrackingScenario scenario)
    {
        await scenario.Broker.InitiateExternalAsync(Request("/workflows"), "tenant-a");
        return await scenario.Broker.CompleteCallbackAsync("contoso", scenario.Adapter.CorrelationState!, new Dictionary<string, IReadOnlyCollection<string>> { ["state"] = [scenario.Adapter.CorrelationState!] });
    }

    private static LocalIdentityServices CreateLocalIdentityServices(IUserProvider users, IRoleProvider roles, DefaultTenantAccessor tenantAccessor)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new IdentityTokenOptions
        {
            SigningKey = "local-external-authentication-test-signing-key",
            Issuer = "https://elsa.test",
            Audience = "elsa-api"
        });
        var tokens = new DefaultElsaTokenService(new FakeTimeProvider(DateTimeOffset.UtcNow), options);
        var accessTokenIssuer = new DefaultAccessTokenIssuer(roles, tokens);
        var sessionRevoker = new SessionRevoker(new MemoryRevokedSessionStore(new MemoryStore<RevokedSession>()), new FakeTimeProvider(DateTimeOffset.UtcNow), options);
        return new(tokens, accessTokenIssuer, sessionRevoker, new DefaultIdentityRefreshTokenService(users, accessTokenIssuer, tenantAccessor, sessionRevoker, options));
    }

    private sealed record LocalIdentityServices(
        DefaultElsaTokenService Tokens,
        DefaultAccessTokenIssuer AccessTokenIssuer,
        SessionRevoker SessionRevoker,
        DefaultIdentityRefreshTokenService RefreshTokens);


    private sealed record IdentityLinkTrackingScenario(
        ExternalAuthenticationBroker Broker,
        InMemoryExternalIdentityProvisioner Provisioner,
        RecordingAdapter Adapter,
        ExternalIdentity Identity,
        FakeTimeProvider Clock);

    internal sealed class RecordingAdapter : IExternalAuthenticationAdapter
    {
        public string Type => "fake";
        public string? CorrelationState { get; private set; }
        public BrokerTransaction? Transaction { get; private set; }
        public EffectiveIdentityProviderConnection? Connection { get; private set; }
        public bool ThrowOnInitiation { get; init; }
        public bool ThrowOnCallback { get; init; }
        public ExternalAuthenticationResult? AuthenticationResult { get; init; }
        public ExternalAuthenticationAdapterDescriptor Describe() => throw new NotSupportedException();
        public ValueTask<ConnectionValidationResult> ValidateAsync(ConnectionValidationContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ExternalAuthorizationRequest> CreateAuthorizationRequestAsync(ExternalAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            if (ThrowOnInitiation)
                throw new InvalidOperationException();
            CorrelationState = context.CorrelationState;
            Transaction = context.Transaction;
            Connection = context.Connection;
            return ValueTask.FromResult(new ExternalAuthorizationRequest(new Uri($"https://issuer.example/authorize?state={Uri.EscapeDataString(context.CorrelationState)}"), [1, 2, 3]));
        }
        public ValueTask<ExternalAuthenticationResult> AuthenticateCallbackAsync(ExternalCallbackContext context, CancellationToken cancellationToken = default)
        {
            if (ThrowOnCallback)
                throw new InvalidOperationException();
            if (AuthenticationResult is not null)
                return ValueTask.FromResult(AuthenticationResult);
            throw new NotSupportedException();
        }
        public ValueTask<ConnectionTestResult> TestAsync(ConnectionTestContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ExternalLogoutRequest?> CreateLogoutRequestAsync(ExternalLogoutContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedHasher : IExternalAuthenticationHandleHasher
    {
        public string Hash(string value) => $"hash:{value}";
    }

    private sealed class MutableSecretResolver : ISecretBindingResolver
    {
        public string Type => "test";
        public string Generation { get; set; } = "generation-1";
        public ValueTask<SecretBindingState> GetStateAsync(SecretBinding binding, CancellationToken cancellationToken = default) => ValueTask.FromResult(new SecretBindingState(true, true));
        public ValueTask<ResolvedSecretBinding> ResolveAsync(SecretBinding binding, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ResolvedSecretBinding(new SensitiveString("secret"), Generation));
    }
}
