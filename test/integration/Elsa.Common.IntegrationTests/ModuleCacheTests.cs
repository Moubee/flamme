using Elsa.Extensions;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Common.IntegrationTests;

public class ModuleCacheTests
{
    [Test]
    public async Task ApplicationCallbackCanAccessItsModuleFromAnotherThread()
    {
        var services = new ServiceCollection();
        IModule? observed = null;
        var module = services.AddElsa(_ =>
        {
            var otherThread = Task.Run(() => services.ConfigureElsa());
            observed = otherThread.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        });

        await Assert.That(observed).IsSameReferenceAs(module);
    }
}
