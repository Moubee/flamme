using System.Text.Json;
using Flamme.Testing.Shared;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.Core.UnitTests.Extensions.ActivityExecutionContextExtensions;

public class DictionaryInputEvaluationTests
{
    [Test]
    public async Task ExplicitDictionaryEvaluatorEvaluatesEntriesAndPreservesPlainValues()
    {
        using var expression = JsonDocument.Parse("""{"type":"Literal","value":"evaluated"}""");
        var activity = new DictionaryInputActivity
        {
            Values = new(new Dictionary<string, object>
            {
                ["expression"] = expression.RootElement,
                ["plain"] = "unchanged"
            })
        };
        await using var fixture = new ActivityTestFixture(activity)
            .ConfigureServices(services => services.AddScoped<DictionaryValueEvaluator>());

        var context = await fixture.ExecuteAsync();
        var values = (Dictionary<string, object>)context.ActivityState[nameof(DictionaryInputActivity.Values)];

        await Assert.That(values["expression"]).IsEqualTo("evaluated");
        await Assert.That(values["plain"]).IsEqualTo("unchanged");
    }

    private sealed class DictionaryInputActivity : CodeActivity
    {
        [Input(EvaluatorType = typeof(DictionaryValueEvaluator))]
        public Input<Dictionary<string, object>> Values { get; set; } = new();
    }
}
