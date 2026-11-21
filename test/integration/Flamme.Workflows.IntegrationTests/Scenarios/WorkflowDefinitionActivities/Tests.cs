using Flamme.Testing.Shared;
using Flamme.Workflows.Management.Activities.WorkflowDefinitionActivity;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.IntegrationTests.Scenarios.WorkflowDefinitionActivities;

public class Tests : IAsyncDisposable
{
    private readonly CapturingTextWriter _capturingTextWriter = new();
    private readonly IServiceProvider _services;

    public Tests()
    {
        _services = new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
            .WithCapturingTextWriter(_capturingTextWriter)
            .WithWorkflowsFromDirectory("Scenarios", "WorkflowDefinitionActivities", "Workflows")
            .Build();
    }

    [Test]
    [MethodDataSource(nameof(WorkflowDefinitionActivityTestCases))]
    public async Task Should_Execute_WorkflowDefinitionActivity_Scenarios(string workflowDefinitionId, string expectedOutput)
    {
        // Populate registries.
        await _services.PopulateRegistriesAsync();

        // Run the workflow.
        await _services.RunWorkflowUntilEndAsync(workflowDefinitionId);

        // Assert - verify expected output appears.
        var lines = _capturingTextWriter.Lines.ToList();
        await Assert.That(lines).Contains(expectedOutput);
    }

    public static IEnumerable<Func<(string WorkflowDefinitionId, string ExpectedOutput)>> WorkflowDefinitionActivityTestCases()
    {
        yield return static () => ("parent-with-input", "Hello from parent!");
        yield return static () => ("parent-with-output", "Received from child: Child output value");
        yield return static () => ("parent-with-outcomes", "Success path taken");
        yield return static () => ("parent-version-fallback", "Published version executed");
    }

    [Test]
    public async Task PublishedWorkflowInputUsesExplicitDictionaryEvaluationInsteadOfLegacyMetadata()
    {
        const string legacyJson = """{"name":"Values","type":"ObjectMap","uiHint":"dictionary"}""";
        var serializer = _services.GetRequiredService<IPayloadSerializer>();
        var input = serializer.Deserialize<InputDefinition>(legacyJson);
        var definition = new WorkflowDefinition { Name = "Dictionary Workflow", Inputs = [input], IsPublished = true };
        var factory = new WorkflowDefinitionActivityDescriptorFactory();

        var defaultDescriptor = factory.CreateDescriptor(definition).Inputs.Single();
        await Assert.That(defaultDescriptor.Type).IsEqualTo(typeof(Dictionary<string, object>));
        await Assert.That(defaultDescriptor.EvaluatorType).IsNull();

        input.EvaluateDictionaryEntries = true;
        var json = serializer.SerializeToElement(input);
        await Assert.That(json.TryGetProperty("uiHint", out _)).IsFalse();
        definition.Inputs = [serializer.Deserialize<InputDefinition>(json)];
        var descriptor = factory.CreateDescriptor(definition).Inputs.Single();
        await Assert.That(descriptor.EvaluatorType).IsEqualTo(typeof(DictionaryValueEvaluator));
    }

    public ValueTask DisposeAsync() => TestResourceDisposal.DisposeAsync(_services);
}
