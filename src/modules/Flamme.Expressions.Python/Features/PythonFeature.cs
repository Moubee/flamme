using Flamme.Common.Features;
using Flamme.Expressions.Python.ActivityDescriptorModifiers;
using Flamme.Expressions.Features;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Expressions.Python.Activities;
using Flamme.Expressions.Python.Contracts;
using Flamme.Expressions.Python.HostedServices;
using Flamme.Expressions.Python.Options;
using Flamme.Expressions.Python.Providers;
using Flamme.Expressions.Python.Services;
using Flamme.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Expressions.Python.Features;

/// <summary>
/// Installs Python integration.
/// </summary>
[DependsOn(typeof(MediatorFeature))]
[DependsOn(typeof(ExpressionsFeature))]
public class PythonFeature : FeatureBase
{
    /// <inheritdoc />
    public PythonFeature(IModule module) : base(module)
    {
    }

    /// <summary>
    /// Configures the <see cref="Options.PythonOptions"/>.
    /// </summary>
    public Action<PythonOptions> PythonOptions { get; set; } = _ => { };

    /// <inheritdoc />
    public override void ConfigureHostedServices()
    {
        Module.ConfigureHostedService<PythonGlobalInterpreterManager>();
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services.Configure(PythonOptions);

        // Python services.
        Services
            .AddScoped<IPythonEvaluator, PythonNetPythonEvaluator>()
            .AddExpressionDescriptorProvider<PythonExpressionDescriptorProvider>()
            .AddSingleton<IActivityDescriptorModifier, PythonActivityDescriptorModifier>()
            ;

        // Handlers.
        Services.AddNotificationHandlersFrom<PythonFeature>();

        // Activities.
        Module.AddActivitiesFrom<PythonFeature>();
        
        // UI property handlers.
        Services.AddScoped<IPropertyUIHandler, RunPythonOptionsProvider>();
    }
}
