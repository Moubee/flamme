using System.Threading.Tasks;
using Elsa.Common.Features;

namespace Elsa.Common.UnitTests;

public class ModuleTypeLoadingTests
{
    [Test]
    public async Task ElsaCommonTypesCanBeLoaded()
    {
        await Assert.That(() => typeof(MultitenancyFeature).Assembly.GetTypes()).ThrowsNothing();
    }
}
