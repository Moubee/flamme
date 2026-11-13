using Elsa.Extensions;
using Elsa.Testing.Shared;
using Elsa.Workflows.Activities;

namespace Elsa.Activities.IntegrationTests;

public class NestedLoopMemoryBlockTests : IAsyncDisposable
{
    private readonly WorkflowTestFixture _fixture = new(TestContext.Current!.Output.StandardOutput);
    private const string CurrentValue = "CurrentValue";
    private const string CurrentIndex = "CurrentIndex";

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Test]
    [DisplayName("Nested ParallelForEach does not overwrite the parent ForEach CurrentValue or CurrentIndex")]
    public async Task NestedParallelForEach_PreservesParentForEachCurrentValueAndIndex()
    {
        var outerItems = new[] { "outer-a", "outer-b" };
        var innerItems = new[] { "inner-1", "inner-2", "inner-3" };
        var forEach = new ForEach<string>(outerItems)
        {
            Body = new Sequence
            {
                Activities =
                {
                    new ParallelForEach<string>(innerItems)
                    {
                        Body = WriteLoopState("inner")
                    },
                    WriteLoopState("outer")
                }
            }
        };

        await _fixture.RunActivityAsync(forEach);

        var lines = _fixture.CapturingTextWriter.Lines.ToList();
        await Assert.That(lines).Contains("inner:inner-1:0");
        await Assert.That(lines).Contains("inner:inner-2:1");
        await Assert.That(lines).Contains("inner:inner-3:2");
        await Assert.That(lines).Contains("outer:outer-a:0");
        await Assert.That(lines).Contains("outer:outer-b:1");
        await Assert.That(lines).DoesNotContain("outer:inner-3:2");
        await Assert.That(lines).DoesNotContain("outer:inner-1:0");
    }

    [Test]
    [DisplayName("Nested ForEach does not overwrite the parent ForEach CurrentValue or CurrentIndex")]
    public async Task NestedForEach_PreservesParentForEachCurrentValueAndIndex()
    {
        var outerItems = new[] { "outer-a", "outer-b" };
        var innerItems = new[] { "inner-1", "inner-2" };
        var forEach = new ForEach<string>(outerItems)
        {
            Body = new Sequence
            {
                Activities =
                {
                    new ForEach<string>(innerItems)
                    {
                        Body = WriteLoopState("inner")
                    },
                    WriteLoopState("outer")
                }
            }
        };

        await _fixture.RunActivityAsync(forEach);

        await Assert.That(_fixture.CapturingTextWriter.Lines).IsEquivalentTo([
                "inner:inner-1:0",
                "inner:inner-2:1",
                "outer:outer-a:0",
                "inner:inner-1:0",
                "inner:inner-2:1",
                "outer:outer-b:1"
            ], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static WriteLine WriteLoopState(string scope) =>
        new(context => $"{scope}:{context.GetVariable<string>(CurrentValue)}:{context.GetVariable<int>(CurrentIndex)}");
}
