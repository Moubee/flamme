using System.Threading.Tasks;
using Elsa.Common.Features;

namespace Elsa.Common.UnitTests;

public class ModuleTypeLoadingTests
{
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "This test deliberately enumerates the untrimmed production assembly to detect type-loading regressions.")]
    [Test]
    public async Task ElsaCommonTypesCanBeLoaded()
    {
        await Assert.That(() => typeof(MultitenancyFeature).Assembly.GetTypes()).ThrowsNothing();
    }
}
