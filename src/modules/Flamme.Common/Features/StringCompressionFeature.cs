using Flamme.Common.Codecs;
using Flamme.Common.Services;
using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Common.Features;

[UsedImplicitly]
public class StringCompressionFeature(IModule module) : FeatureBase(module)
{
    public override void Apply()
    {
        Services
            .AddSingleton<ICompressionCodecResolver, CompressionCodecResolver>()
            .AddSingleton<ICompressionCodec, None>()
            .AddSingleton<ICompressionCodec, GZip>()
            .AddSingleton<ICompressionCodec, Zstd>();
    }
}