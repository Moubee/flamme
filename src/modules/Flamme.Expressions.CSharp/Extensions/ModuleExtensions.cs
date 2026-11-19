using Flamme.Expressions.CSharp.Features;
using Flamme.Expressions.CSharp.Options;
using Flamme.Features.Services;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

/// <summary>
/// Adds extensions to <see cref="IModule"/> that installs the <see cref="CSharpFeature"/> feature.
/// </summary>
public static class ModuleExtensions
{
    /// <summary>
    /// Setup the <see cref="CSharpFeature"/> feature.
    /// </summary>
    public static IModule UseCSharp(this IModule module, Action<CSharpFeature>? configure = default)
    {
        module.Configure(configure);
        return module;
    }
    
    /// <summary>
    /// Setup the <see cref="CSharpFeature"/> feature.
    /// </summary>
    public static IModule UseCSharp(this IModule module, Action<CSharpOptions> configureOptions)
    {
        return module.UseCSharp(csharp => csharp.CSharpOptions += configureOptions);
    }
}