using Flamme.AI.Abstractions.Contracts;
using Flamme.AI.Abstractions.Models;
using Flamme.Extensions;
using Flamme.AI.Persistence.EFCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Flamme.AI.Persistence.EFCore.UnitTests;

public class AIPersistenceRegistrationTests
{
    [Test]
    [DisplayName("AI persistence store registration configures DbContext")]
    public async Task AIPersistenceStoreRegistrationConfiguresDbContext()
    {
        var services = new ServiceCollection();

        services.AddAIPersistenceStores(options => options.UseNpgsql("Host=localhost;Database=elsa_registration"));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        await Assert.That(scope.ServiceProvider.GetRequiredService<AIDbContext>()).IsNotNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IAIProposalStore>()).IsNotNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IAIConversationStore>()).IsOfType(typeof(EFCoreAIConversationStore));
    }

    [Test]
    [DisplayName("AI persistence store registration replaces existing conversation store")]
    public async Task AIPersistenceStoreRegistrationReplacesExistingConversationStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAIConversationStore, StubConversationStore>();
        services.AddSingleton<IAIProposalStore, StubProposalStore>();

        services.AddAIPersistenceStores(options => options.UseNpgsql("Host=localhost;Database=elsa_registration"));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        await Assert.That(scope.ServiceProvider.GetRequiredService<IAIConversationStore>()).IsOfType(typeof(EFCoreAIConversationStore));
        await Assert.That(scope.ServiceProvider.GetRequiredService<IAIProposalStore>()).IsOfType(typeof(EFCoreAIProposalStore));
    }

    [Test]
    [DisplayName("AI persistence store registration requires a DbContext provider")]
    public void AIPersistenceStoreRegistrationRequiresDbContextProvider()
    {
        var services = new ServiceCollection();

        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddAIPersistenceStores());
    }

    [Test]
    [DisplayName("AI persistence store registration uses preconfigured DbContext options")]
    public async Task AIPersistenceStoreRegistrationUsesPreconfiguredDbContextOptions()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DbContextOptionsBuilder<AIDbContext>().UseNpgsql("Host=localhost;Database=elsa_registration").Options);

        services.AddAIPersistenceStores();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        await Assert.That(scope.ServiceProvider.GetRequiredService<AIDbContext>()).IsNotNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IAIConversationStore>()).IsOfType(typeof(EFCoreAIConversationStore));
    }

    private class StubConversationStore : IAIConversationStore
    {
        public ValueTask<AIConversation?> FindAsync(string id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AIConversation?>(null);

        public ValueTask SaveAsync(AIConversation conversation, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private class StubProposalStore : IAIProposalStore
    {
        public ValueTask<AIProposal?> FindAsync(string id, string? tenantId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AIProposal?>(null);

        public ValueTask SaveAsync(AIProposal proposal, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
