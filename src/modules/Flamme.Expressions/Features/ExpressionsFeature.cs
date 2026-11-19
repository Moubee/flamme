using Flamme.Expressions.Contracts;
using Flamme.Expressions.Services;
using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Expressions.Features;

/// <summary>
/// Installs and configures the expressions feature.
/// </summary>
public class ExpressionsFeature : FeatureBase
{
    /// <inheritdoc />
    public ExpressionsFeature(IModule module) : base(module)
    {
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services
            .AddScoped<IExpressionEvaluator, ExpressionEvaluator>()
            .AddSingleton<IWellKnownTypeRegistry, WellKnownTypeRegistry>();
    }
}