using Flamme.Caching.Features;
using Flamme.Common.Features;
using Flamme.Expressions.CSharp.ActivityDescriptorModifiers;
using Flamme.Expressions.CSharp.Activities;
using Flamme.Expressions.CSharp.Contracts;
using Flamme.Expressions.CSharp.Options;
using Flamme.Expressions.CSharp.Providers;
using Flamme.Expressions.CSharp.Services;
using Flamme.Expressions.Features;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Expressions.CSharp.Features;

/// <summary>
/// Installs C# integration.
/// </summary>
[DependsOn(typeof(MediatorFeature))]
[DependsOn(typeof(ExpressionsFeature))]
[DependsOn(typeof(MemoryCacheFeature))]
public class CSharpFeature : FeatureBase
{
    /// <inheritdoc />
    public CSharpFeature(IModule module) : base(module)
    {
    }

    /// <summary>
    /// Configures the <see cref="CSharpOptions"/>.
    /// </summary>
    public Action<CSharpOptions> CSharpOptions { get; set; } = _ => { };

    /// <inheritdoc />
    public override void Apply()
    {
        Services.Configure(CSharpOptions);

        // C# services.
        Services
            .AddExpressionDescriptorProvider<CSharpExpressionDescriptorProvider>()
            .AddScoped<ICSharpEvaluator, CSharpEvaluator>()
            .AddSingleton<IActivityDescriptorModifier, CSharpActivityDescriptorModifier>()
            ;

        // Handlers.
        Services.AddNotificationHandlersFrom<CSharpFeature>();

        // Activities.
        Module.AddActivitiesFrom<CSharpFeature>();
        
        // UI property handlers.
        Services.AddScoped<IPropertyUIHandler, RunCSharpOptionsProvider>();
    }
}
