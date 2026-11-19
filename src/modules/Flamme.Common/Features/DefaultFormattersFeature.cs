using System.ComponentModel;
using Flamme.Common.Serialization;
using Flamme.Common.Services;
using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Common.Features;

public class DefaultFormattersFeature(IModule module) : FeatureBase(module)
{
    public override void Configure()
    {
        TypeDescriptor.AddAttributes(typeof(Type), new TypeConverterAttribute(typeof(TypeTypeConverter)));
        Module.Services.AddSingleton<IFormatter, JsonFormatter>();
    }
}