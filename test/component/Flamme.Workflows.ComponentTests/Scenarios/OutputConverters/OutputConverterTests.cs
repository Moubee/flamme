using Flamme.Expressions.Models;
using Flamme.Extensions;
using Flamme.Workflows.Activities;
using Flamme.Workflows.ComponentTests.Abstractions;
using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.Exceptions;
using Flamme.Workflows.IncidentStrategies;
using Flamme.Workflows.Memory;
using Flamme.Workflows.Models;
using Flamme.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.ComponentTests.Scenarios.OutputConverters;

public class OutputConverterTests(App app) : AppComponentTest(app)
{
    [Test]
    public async Task ConfiguredConverter_WritesConvertedValueToVariableAndRetainsNativeActivityOutput()
    {
        var builder = Scope.ServiceProvider.GetRequiredService<IWorkflowBuilderFactory>().CreateBuilder();
        var destination = builder.WithVariable<string>("ConvertedValue", "unchanged").WithWorkflowStorage();
        var observed = builder.WithOutput<string>("ObservedValue");
        var activity = new NativeStringActivity
        {
            Result = new(destination)
            {
                Converter = new(TestOutputConverter.Descriptor.Id)
            }
        };
        builder.Root = new Sequence
        {
            Activities =
            [
                activity,
                new Inline<string>(
                    context => destination.Get(context.ExpressionExecutionContext)!,
                    new MemoryBlockReference(observed.Name))
            ]
        };
        var workflow = await builder.BuildWorkflowAsync();

        var result = await Scope.ServiceProvider.GetRequiredService<IWorkflowInvoker>().InvokeAsync(workflow);

        await Assert.That(result.WorkflowExecutionContext.Output[observed.Name]).IsEqualTo("converted:native value");
        await Assert.That(result.WorkflowExecutionContext.GetActivityOutputRegister().FindOutputByActivityId(activity.Id)).IsEqualTo("native value");
    }

    [Test]
    public async Task ConfiguredConverter_WritesConvertedValueToWorkflowOutputAndRetainsNativeActivityOutput()
    {
        var builder = Scope.ServiceProvider.GetRequiredService<IWorkflowBuilderFactory>().CreateBuilder();
        var destination = builder.WithOutput<string>("ConvertedOutput");
        var activity = new NativeStringActivity
        {
            Result = new(new MemoryBlockReference(destination.Name))
            {
                Converter = new(TestOutputConverter.Descriptor.Id)
            }
        };
        builder.Root = activity;
        var workflow = await builder.BuildWorkflowAsync();

        var result = await Scope.ServiceProvider.GetRequiredService<IWorkflowInvoker>().InvokeAsync(workflow);

        await Assert.That(result.WorkflowExecutionContext.Output[destination.Name]).IsEqualTo("converted:native value");
        await Assert.That(result.WorkflowExecutionContext.GetActivityOutputRegister().FindOutputByActivityId(activity.Id)).IsEqualTo("native value");
    }

    [Test]
    public async Task MissingConverter_FaultsWithSafeMetadataAndLeavesDestinationUnchanged()
    {
        var builder = Scope.ServiceProvider.GetRequiredService<IWorkflowBuilderFactory>().CreateBuilder();
        builder.WorkflowOptions.IncidentStrategyType = typeof(FaultStrategy);
        var destination = builder.WithVariable<string>("ConvertedValue", "unchanged").WithWorkflowStorage();
        var activity = new NativeStringActivity
        {
            Result = new(destination)
            {
                Converter = new("tests.component.missing")
            }
        };
        builder.Root = activity;
        var workflow = await builder.BuildWorkflowAsync();

        var result = await Scope.ServiceProvider.GetRequiredService<IWorkflowInvoker>().InvokeAsync(workflow);

        await Assert.That(result.WorkflowExecutionContext.SubStatus).IsEqualTo(WorkflowSubStatus.Faulted);
        await Assert.That(result.WorkflowExecutionContext.GetActivityOutputRegister().FindOutputByActivityId(activity.Id)).IsEqualTo("native value");
        var incident = await Assert.That(result.WorkflowExecutionContext.Incidents).HasSingleItem();
        await Assert.That(incident.Exception!.Type).IsEqualTo(typeof(OutputConversionException));
        await Assert.That(incident.Exception.Metadata![nameof(OutputConversionException.ConverterId)]).IsEqualTo("tests.component.missing");
        await Assert.That(incident.Exception.Metadata[nameof(OutputConversionException.Stage)]).IsEqualTo("Resolution");
        await Assert.That(incident.Message).DoesNotContain("native value");
    }

    private sealed class NativeStringActivity : CodeActivity<string>
    {
        protected override void Execute(ActivityExecutionContext context) => Result!.Set(context, "native value");
    }
}

public sealed class TestOutputConverter : IOutputConverter
{
    public static OutputConverterDescriptor Descriptor { get; } = new(
        "tests.component.to-text",
        typeof(string),
        typeof(string),
        "Component test converter");

    public object? Convert(OutputConversionContext context) => $"converted:{context.Value}";
}