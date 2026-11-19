using System.Net;
using Flamme.Extensions;
using Flamme.Http;
using Flamme.Resilience.Extensions;
using Flamme.Resilience.Models;
using Flamme.Testing.Shared;
using Flamme.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Resilience.IntegrationTests;

public class FlowSendHttpRequestResilienceTests : IAsyncDisposable
{
    private readonly WorkflowTestFixture _fixture;
    private readonly TestRetryAttemptRecorder _recorder = new();

    public FlowSendHttpRequestResilienceTests()
    {
        var handler = new SequentialStatusHandler([HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK]);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resilience:Strategies:0:$type"] = "HttpResilienceStrategy",
                ["Resilience:Strategies:0:Id"] = "test",
                ["Resilience:Strategies:0:DisplayName"] = "Test",
                ["Resilience:Strategies:0:MaxRetryAttempts"] = "5",
                ["Resilience:Strategies:0:Delay"] = "00:00:00.001"
            })
            .Build();

        _fixture = new WorkflowTestFixture(TestContext.Current!.Output.StandardOutput)
            .ConfigureServices(s =>
            {
                s.AddSingleton<IConfiguration>(configuration);
            })
            .ConfigureFlamme(module =>
            {
                module.UseHttp(http =>
                {
                    http.HttpClientBuilder = builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler);
                });

                module.UseResilience(resilience =>
                {
                    resilience.WithRetryAttemptRecorder(_ => _recorder);
                });
            });
    }

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Test]
    [DisplayName("FlowSendHttpRequest retries using selected resilience strategy")]
    public async Task InvokesResilienceStrategy()
    {
        var activity = new FlowSendHttpRequest
        {
            Url = new(new Uri("http://localhost/test")),
            Method = new("GET"),
            ExpectedStatusCodes = new([200]),
            CustomProperties =
            {
                ["resilienceStrategy"] = new ResilienceStrategyConfig
                {
                    Mode = ResilienceStrategyConfigMode.Identifier,
                    StrategyId = "test"
                }
            }
        };

        var result = await _fixture.RunActivityAsync(activity);
        var statusCode = result.GetActivityOutput<int>(activity, nameof(SendHttpRequestBase.StatusCode));

        await Assert.That(statusCode).IsEqualTo(200);
        await Assert.That(_recorder.Attempts.Select(x => x.AttemptNumber)).IsEquivalentTo(
            [0, 1],
            EqualityComparer<int>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
