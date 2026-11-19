using System.Text.Json.Nodes;
using Flamme.AI.Host.Services;
using System.Threading.Tasks;

namespace Flamme.AI.Host.UnitTests.Grounding;

public class WorkflowProposalDiffServiceTests
{
    [Test]
    [DisplayName("Workflow proposal diff compares draft against baseline graph")]
    public async Task WorkflowProposalDiffComparesDraftAgainstBaselineGraph()
    {
        var service = new WorkflowProposalDiffService();
        var baseline = new JsonObject
        {
            ["activities"] = new JsonArray
            {
                (JsonNode)new JsonObject { ["id"] = "kept", ["type"] = "Elsa.WriteLine" },
                (JsonNode)new JsonObject { ["id"] = "removed", ["type"] = "Elsa.Http.HttpEndpoint" }
            }
        };
        var draft = new JsonObject
        {
            ["activities"] = new JsonArray
            {
                (JsonNode)new JsonObject { ["id"] = "kept", ["type"] = "Elsa.WriteLine" },
                (JsonNode)new JsonObject { ["id"] = "added", ["type"] = "Elsa.SendEmail" }
            }
        };

        var diff = service.CreateDiff(draft, baseline);

        await Assert.That(diff.AddedActivityIds).IsEquivalentTo(["added"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(diff.RemovedActivityIds).IsEquivalentTo(["removed"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
