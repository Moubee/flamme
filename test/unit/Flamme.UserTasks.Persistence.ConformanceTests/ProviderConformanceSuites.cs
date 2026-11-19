using Elsa.UserTasks.Persistence.ConformanceTests.Infrastructure;
using Elsa.UserTasks.Persistence.ConformanceTests.Providers;

namespace Elsa.UserTasks.Persistence.ConformanceTests;

// Share one fixture per provider and serialize tests that use its mutable clock and stores.
internal static class ProviderFixtureKeys
{
    public const string InMemory = "UserTasks:InMemory";
    public const string PostgreSql = "UserTasks:EFCore.PostgreSql";
}

[InheritsTests]
[ClassDataSource<InMemoryUserTaskStoreFixture>(Shared = SharedType.Keyed, Key = ProviderFixtureKeys.InMemory)]
[NotInParallel(ProviderFixtureKeys.InMemory)]
[ConformanceProvider(ConformanceProviders.InMemory)]
public sealed class InMemoryUserTaskRepositoryConformanceTests(InMemoryUserTaskStoreFixture fixture)
    : UserTaskRepositoryConformanceTests(fixture);

[InheritsTests]
[ClassDataSource<InMemoryUserTaskStoreFixture>(Shared = SharedType.Keyed, Key = ProviderFixtureKeys.InMemory)]
[NotInParallel(ProviderFixtureKeys.InMemory)]
[ConformanceProvider(ConformanceProviders.InMemory)]
public sealed class InMemoryUserTaskGuestSessionConformanceTests(InMemoryUserTaskStoreFixture fixture)
    : UserTaskGuestSessionConformanceTests(fixture);

[InheritsTests]
[ClassDataSource<InMemoryUserTaskStoreFixture>(Shared = SharedType.Keyed, Key = ProviderFixtureKeys.InMemory)]
[NotInParallel(ProviderFixtureKeys.InMemory)]
[ConformanceProvider(ConformanceProviders.InMemory)]
public sealed class InMemoryUserTaskInvitationOutboxConformanceTests(InMemoryUserTaskStoreFixture fixture)
    : UserTaskInvitationOutboxConformanceTests(fixture);

[InheritsTests]
[ClassDataSource<InMemoryUserTaskStoreFixture>(Shared = SharedType.Keyed, Key = ProviderFixtureKeys.InMemory)]
[NotInParallel(ProviderFixtureKeys.InMemory)]
[ConformanceProvider(ConformanceProviders.InMemory)]
public sealed class InMemoryUserTaskFaultInjectionConformanceTests(InMemoryUserTaskStoreFixture fixture)
    : UserTaskFaultInjectionConformanceTests(fixture);

[InheritsTests]
[ClassDataSource<PostgreSqlUserTaskStoreFixture>(Shared = SharedType.Keyed, Key = ProviderFixtureKeys.PostgreSql)]
[NotInParallel(ProviderFixtureKeys.PostgreSql)]
[ConformanceProvider(ConformanceProviders.PostgreSql)]
public sealed class PostgreSqlUserTaskRepositoryConformanceTests(PostgreSqlUserTaskStoreFixture fixture)
    : UserTaskRepositoryConformanceTests(fixture);

[InheritsTests]
[ClassDataSource<PostgreSqlUserTaskStoreFixture>(Shared = SharedType.Keyed, Key = ProviderFixtureKeys.PostgreSql)]
[NotInParallel(ProviderFixtureKeys.PostgreSql)]
[ConformanceProvider(ConformanceProviders.PostgreSql)]
public sealed class PostgreSqlUserTaskGuestSessionConformanceTests(PostgreSqlUserTaskStoreFixture fixture)
    : UserTaskGuestSessionConformanceTests(fixture);

[InheritsTests]
[ClassDataSource<PostgreSqlUserTaskStoreFixture>(Shared = SharedType.Keyed, Key = ProviderFixtureKeys.PostgreSql)]
[NotInParallel(ProviderFixtureKeys.PostgreSql)]
[ConformanceProvider(ConformanceProviders.PostgreSql)]
public sealed class PostgreSqlUserTaskInvitationOutboxConformanceTests(PostgreSqlUserTaskStoreFixture fixture)
    : UserTaskInvitationOutboxConformanceTests(fixture);

[InheritsTests]
[ClassDataSource<PostgreSqlUserTaskStoreFixture>(Shared = SharedType.Keyed, Key = ProviderFixtureKeys.PostgreSql)]
[NotInParallel(ProviderFixtureKeys.PostgreSql)]
[ConformanceProvider(ConformanceProviders.PostgreSql)]
public sealed class PostgreSqlUserTaskFaultInjectionConformanceTests(PostgreSqlUserTaskStoreFixture fixture)
    : UserTaskFaultInjectionConformanceTests(fixture);
