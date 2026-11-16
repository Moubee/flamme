using Elsa.Workflows.Activities;
using Elsa.Workflows.Management.Handlers.Notifications;
using Elsa.Workflows.Management.Models;
using Elsa.Workflows.Management.Notifications;
using Elsa.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Mocks;

namespace Elsa.Workflows.Management.UnitTests.Handlers.Notifications;

public class ValidateOutputConvertersTests
{
    [Test]
    public async Task HandleAsync_WhenConverterHasNoDestination_AddsValidationError()
    {
        var fixture = new Fixture();
        Mock.Get(fixture.DestinationResolver).Resolve(fixture.Graph, fixture.Node, fixture.Activity.Result).Returns((OutputBindingDestination?)null);

        var errors = await fixture.ValidateAsync();

        var error = await Assert.That(errors).HasSingleItem();
        await Assert.That(error.ActivityId).IsEqualTo(fixture.Activity.Id);
        await Assert.That(error.Message).Contains("only be configured").WithComparison(StringComparison.CurrentCulture);
    }

    [Test]
    public async Task HandleAsync_WhenConverterIsUnknown_AddsValidationError()
    {
        var fixture = new Fixture();

        var errors = await fixture.ValidateAsync();

        await Assert.That(errors).Contains(x => x.Message.Contains("is not registered", StringComparison.CurrentCulture));
    }

    [Test]
    public async Task HandleAsync_WhenBindingAndSettingsAreValid_AddsNoValidationErrors()
    {
        var fixture = new Fixture();
        var descriptor = new OutputConverterDescriptor("test", typeof(int), typeof(string), "Test");
        Mock.Get(fixture.Registry).FindRegistration("test").Returns(new OutputConverterRegistration(descriptor, "test", ServiceLifetime.Singleton));
        Mock.Get(fixture.SettingsValidator).Validate(descriptor, Arg.Any<IOutputConverter>(), null).Returns([]);

        var errors = await fixture.ValidateAsync();

        await Assert.That(errors).IsEmpty();
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Activity = new TestActivity
            {
                Id = "activity-1",
                Result = new()
                {
                    Converter = new("test")
                }
            };
            Node = new(Activity, "Body");
            var workflow = new Workflow();
            Graph = new(workflow, Node, [Node]);
            Mock.Get(GraphBuilder).BuildAsync(workflow, Arg.Any<CancellationToken>()).Returns(Graph);
            Mock.Get(ActivityRegistry).FindAsync(Activity.Type, Activity.Version).Returns(new ActivityDescriptor
            {
                Outputs =
                            [
                                new(
                        nameof(TestActivity.Result),
                        "Result",
                        typeof(int),
                        activity => ((TestActivity)activity).Result,
                        (activity, value) => ((TestActivity)activity).Result = (Output<int>)value!)
                            ]
            });
            Mock.Get(DestinationResolver).Resolve(Graph, Node, Activity.Result).Returns(
                            new OutputBindingDestination("workflow-result", typeof(string), true, OutputBindingDestinationKind.WorkflowOutput));

            var services = new ServiceCollection();
            services.AddKeyedSingleton<IOutputConverter, TestConverter>("test");
            ServiceProvider = services.BuildServiceProvider();
        }

        public TestActivity Activity { get; }
        public ActivityNode Node { get; }
        public WorkflowGraph Graph { get; }
        public IWorkflowGraphBuilder GraphBuilder { get; } = IWorkflowGraphBuilder.Mock().Object;
        public IActivityRegistryLookupService ActivityRegistry { get; } = IActivityRegistryLookupService.Mock().Object;
        public IOutputConverterRegistry Registry { get; } = IOutputConverterRegistry.Mock().Object;
        public IOutputBindingDestinationResolver DestinationResolver { get; } = IOutputBindingDestinationResolver.Mock().Object;
        public IOutputConverterSettingsValidator SettingsValidator { get; } = IOutputConverterSettingsValidator.Mock().Object;
        public ServiceProvider ServiceProvider { get; }

        public async Task<ICollection<WorkflowValidationError>> ValidateAsync()
        {
            var errors = new List<WorkflowValidationError>();
            var notification = new WorkflowDefinitionValidating(Graph.Workflow, errors);
            var handler = new ValidateOutputConverters(
                GraphBuilder,
                ActivityRegistry,
                Registry,
                DestinationResolver,
                SettingsValidator,
                ServiceProvider);

            await handler.HandleAsync(notification, CancellationToken.None);
            return errors;
        }
    }

    private sealed class TestActivity : CodeActivity
    {
        public Output<int> Result { get; set; } = new();

        protected override void Execute(ActivityExecutionContext context)
        {
        }
    }

    private sealed class TestConverter : IOutputConverter
    {
        public object Convert(OutputConversionContext context) => context.Value.ToString()!;
    }
}
