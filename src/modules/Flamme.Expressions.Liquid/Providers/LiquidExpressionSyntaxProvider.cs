using Flamme.Expressions.Contracts;
using Flamme.Expressions.Models;
using Flamme.Extensions;
using Flamme.Expressions.Liquid.Expressions;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Expressions.Liquid.Providers;

/// <summary>
/// Provides Liquid expression descriptors.
/// </summary>
public class LiquidExpressionDescriptorProvider : IExpressionDescriptorProvider
{
    /// <summary>
    /// Gets the name of the expression type.
    /// </summary>
    public const string TypeName = "Liquid";

    /// <inheritdoc />
    public IEnumerable<ExpressionDescriptor> GetDescriptors()
    {
        yield return new()
        {
            Type = TypeName,
            DisplayName = "Liquid",
            Properties = new { MonacoLanguage = "liquid" }.ToDictionary(),
            HandlerFactory = ActivatorUtilities.GetServiceOrCreateInstance<LiquidExpressionHandler> 
        };
    }
}