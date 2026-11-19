using System.Threading.Tasks;
using Flamme.Common.Features;

namespace Flamme.Common.UnitTests;

public class ModuleTypeLoadingTests
{
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "This test deliberately enumerates the untrimmed production assembly to detect type-loading regressions.")]
    [Test]
    public async Task FlammeCommonTypesCanBeLoaded()
    {
        await Assert.That(() => typeof(MultitenancyFeature).Assembly.GetTypes()).ThrowsNothing();
    }
}
