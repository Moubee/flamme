using System.Threading.Tasks;
using Elsa.Features.Services;
using Elsa.Mediator;
using Elsa.Mediator.Contracts;
using Elsa.Mediator.HostedServices;
using Elsa.Mediator.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Common.UnitTests.HostedServices;

public class MediatorHostedServiceTests
{
    [Test]
    public async Task StartAsync_StartsMediatorCommandProcessor()
    {
        // Arrange
        await using var serviceProvider = CreateServiceProvider();
        var backgroundTask = GetMediatorHostedService(serviceProvider);
        var commandSender = serviceProvider.GetRequiredService<ICommandSender>();
        var sink = serviceProvider.GetRequiredService<CommandSink>();

        // Act
        await backgroundTask.StartAsync(CancellationToken.None);

        try
        {
            await commandSender.SendAsync(new TestCommand("hello"), CommandStrategy.Background, CancellationToken.None);

            // Assert
            var completedTask = await Task.WhenAny(sink.Handled.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            await Assert.That(completedTask).IsSameReferenceAs(sink.Handled.Task);
            await Assert.That(await sink.Handled.Task).IsEqualTo("hello");
        }
        finally
        {
            await backgroundTask.StopAsync(CancellationToken.None);
        }
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var module = IModule.Mock().Object;
        Mock.Get(module).Services.Returns(services);
        new Elsa.Common.Features.MediatorFeature(module).Apply();
        services.AddSingleton<CommandSink>();
        services.AddCommandHandler<TestCommandHandler>();
        return services.BuildServiceProvider();
    }

    private static BackgroundCommandSenderHostedService GetMediatorHostedService(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<BackgroundCommandSenderHostedService>().Single();
    }

    private sealed record TestCommand(string Message) : ICommand;

    private sealed class CommandSink
    {
        public TaskCompletionSource<string> Handled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TestCommandHandler(CommandSink sink) : ICommandHandler<TestCommand>
    {
        public Task<Unit> HandleAsync(TestCommand command, CancellationToken cancellationToken)
        {
            sink.Handled.TrySetResult(command.Message);
            return Task.FromResult(Unit.Instance);
        }
    }
}
