using Flamme.Testing.Shared.Multitenancy;
using Flamme.Common.Services;
using Flamme.Identity.Contracts;
using Flamme.Identity.Entities;
using Flamme.Identity.HostedServices;
using Flamme.Identity.Models;
using Flamme.Identity.Options;
using Flamme.Identity.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Tasks;

namespace Flamme.Identity.UnitTests.HostedServices;

public class AdminUserInitializerTests
{
    [Test]
    public async Task ExecuteAsyncAddsMissingConfiguredPermissionsToExistingAdminRole()
    {
        var roleStore = await CreateRoleStoreAsync(["custom"]);
        var initializer = CreateInitializer(roleStore, ["*"]);

        await initializer.ExecuteAsync(CancellationToken.None);

        var role = await roleStore.FindAsync(new RoleFilter { Id = "admin" });
        await Assert.That(role).IsNotNull();
        await Assert.That(role.Permissions).IsEquivalentTo(["custom", "*"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ExecuteAsyncDoesNotDuplicateExistingConfiguredPermissions()
    {
        var roleStore = await CreateRoleStoreAsync(["*", "custom"]);
        var initializer = CreateInitializer(roleStore, ["*"]);

        await initializer.ExecuteAsync(CancellationToken.None);
        await initializer.ExecuteAsync(CancellationToken.None);

        var role = await roleStore.FindAsync(new RoleFilter { Id = "admin" });
        await Assert.That(role).IsNotNull();
        await Assert.That(role.Permissions).IsEquivalentTo(["*", "custom"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static async Task<MemoryRoleStore> CreateRoleStoreAsync(ICollection<string> permissions)
    {
        var roleStore = new MemoryRoleStore(new MemoryStore<Role>(), TestTenantAccessor.Default);
        await roleStore.AddAsync(new Role
        {
            Id = "admin",
            Name = "Administrator",
            Permissions = permissions
        });
        return roleStore;
    }

    private static AdminUserInitializer CreateInitializer(IRoleStore roleStore, ICollection<string> permissions)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new DefaultAdminUserOptions
        {
            AdminRoleName = "admin",
            AdminRolePermissions = permissions
        });
        return new(
            IUserStore.Mock().Object,
            roleStore,
            IUserManager.Mock().Object,
            IRoleManager.Mock().Object,
            options,
            NullLogger<AdminUserInitializer>.Instance);
    }
}