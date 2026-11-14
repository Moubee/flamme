using System.Text.Json;
using System.Text.Json.Serialization;
using CShells.Lifecycle;
using Elsa.Workflows;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;
using TUnit.AspNetCore;

namespace Elsa.Shells.Api.Tests;

public abstract class ShellsApiTestBase : WebApplicationTest<ShellsApiWebApplicationFactory, ShellsApiTestEntryPoint>
{
    private HttpClient? _httpClient;

    protected ShellsApiTestBase()
    {
        Mock.Get(ShellRegistry).ReloadActiveAsync(Arg.Any<ReloadOptions?>(), Arg.Any<CancellationToken>())
            .Returns(() => Task.FromResult<IReadOnlyList<ReloadResult>>(Array.Empty<ReloadResult>()));
        Mock.Get(ShellRegistry).ReloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string shellId, CancellationToken _) => new ReloadResult(shellId, null, null, null));
    }

    protected IShellRegistry ShellRegistry { get; } = Mock.Of<IShellRegistry>().Object;

    protected HttpClient HttpClient => _httpClient ??= Factory.CreateClient();

    protected JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        var apiSerializer = Mock.Of<IApiSerializer>().Object;
        Mock.Get(apiSerializer).GetOptions().Returns(JsonOptions);

        services.AddSingleton(ShellRegistry);
        services.AddSingleton(apiSerializer);
    }

    protected record ShellReloadResult(string Status, string? Message, string? RequestedShellId);
}
