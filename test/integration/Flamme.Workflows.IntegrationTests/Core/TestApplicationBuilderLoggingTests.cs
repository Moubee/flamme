using Flamme.Testing.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flamme.Workflows.IntegrationTests.Core;

public class TestApplicationBuilderLoggingTests
{
    [Test]
    [Arguments(LogLevel.Debug)]
    [Arguments(LogLevel.Information)]
    public async Task LoggerMessagesReachTheCurrentTestOutput(LogLevel level)
    {
        var context = TestContext.Current!;
        await using var services = (ServiceProvider)new TestApplicationBuilder(context.Output.StandardOutput).Build();
        var logger = services.GetRequiredService<ILogger<TestApplicationBuilderLoggingTests>>();
        var message = $"Workflow test log {Guid.NewGuid():N}";

        logger.Log(level, "{Message}", message);

        await Assert.That(context.GetStandardOutput()).Contains(message);
    }
}
