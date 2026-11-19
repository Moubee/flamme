using Flamme.AI.Abstractions.Contracts;
using Flamme.AI.Abstractions.Models;
using System.Threading.Tasks;

namespace Flamme.AI.Copilot.UnitTests;

public class CopilotBoundaryTests
{
    [Test]
    [DisplayName("Abstractions expose provider-neutral contracts")]
    public async Task AbstractionsExposeProviderNeutralContracts()
    {
        var contractTypes = new[]
        {
            typeof(IAIProvider),
            typeof(IAIOrchestrator),
            typeof(IAITool),
            typeof(IAIContextProvider),
            typeof(IAIProposalStore),
            typeof(AIProviderEvent),
            typeof(AIStreamEvent)
        };

        foreach (var type in contractTypes)
            await Assert.That(type.FullName?.Contains("Copilot", StringComparison.OrdinalIgnoreCase) ?? false).IsFalse();
    }
}
