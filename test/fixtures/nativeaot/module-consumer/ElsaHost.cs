using Elsa.Features.Models;
using Elsa.Features.Services;
using Elsa.NativeAot.ModuleConsumer;
using Elsa.NativeAot.Modules.Producer;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.CodeGeneration;

internal static partial class ElsaHost
{
    internal static IModule CreateModule(IServiceCollection services, CompositionPolicy policy)
        => FeatureModuleCatalogBuilder.CreateModule(services, GetModuleExports(), policy);

    // Selecting the same producer twice exercises idempotent imports in the published consumer.
    private static FeatureModuleExport[] GetModuleExports()
        => [ModuleConsumerScenarios.ConsumerExport, ProducerExports.Foundation, ProducerExports.Foundation];
}
