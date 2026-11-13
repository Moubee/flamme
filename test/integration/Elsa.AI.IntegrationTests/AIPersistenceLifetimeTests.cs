using Elsa.AI.Abstractions.Contracts;
using Elsa.AI.Persistence.EFCore;
using Elsa.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.AI.IntegrationTests;

public class AIPersistenceLifetimeTests
{
    [Test]
    [DisplayName("EF Core conversation persistence resolves with scope validation enabled")]
    public async Task EFCoreConversationPersistenceResolvesWithScopeValidationEnabled()
    {
        var services = new ServiceCollection();
        services.AddAIHostServices();
        services.AddAIPersistenceStores(options => options.UseNpgsql("Host=localhost;Database=elsa_registration"));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using var scope = provider.CreateScope();

        await Assert.That(scope.ServiceProvider.GetRequiredService<IAIOrchestrator>()).IsNotNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IAIConversationStore>()).IsNotNull();
    }
}
