using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Flamme.Dashboard.Api.UnitTests;

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Integration";

    public string ApplicationName { get; set; } = "Elsa.TestHost";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}
