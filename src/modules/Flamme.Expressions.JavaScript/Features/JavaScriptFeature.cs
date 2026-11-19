using Flamme.Caching.Features;
using Flamme.Expressions.Options;
using Flamme.Common.Features;
using Flamme.Expressions.Features;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Expressions.JavaScript.Activities;
using Flamme.Expressions.JavaScript.Contracts;
using Flamme.Expressions.JavaScript.Extensions;
using Flamme.Expressions.JavaScript.HostedServices;
using Flamme.Expressions.JavaScript.Options;
using Flamme.Expressions.JavaScript.Providers;
using Flamme.Expressions.JavaScript.Services;
using Flamme.Expressions.JavaScript.TypeDefinitions.Contracts;
using Flamme.Expressions.JavaScript.TypeDefinitions.Services;
using Flamme.Workflows;
using Flamme.Workflows.Options;
using Microsoft.Extensions.DependencyInjection;
using Flamme.Common.Serialization;

namespace Flamme.Expressions.JavaScript.Features;

/// <summary>
/// Installs JavaScript integration.
/// </summary>
[DependsOn(typeof(MediatorFeature))]
[DependsOn(typeof(ExpressionsFeature))]
[DependsOn(typeof(MemoryCacheFeature))]
public class JavaScriptFeature : FeatureBase
{
    /// <inheritdoc />
    public JavaScriptFeature(IModule module) : base(module)
    {
    }

    /// <summary>
    /// Configures the Jint options.
    /// </summary>
    private Action<JintOptions> JintOptions { get; set; } = _ => { };

    public JavaScriptFeature ConfigureJintOptions(Action<JintOptions> configure)
    {
        JintOptions += configure;
        return this;
    }

    /// <inheritdoc />
    public override void ConfigureHostedServices()
    {
        ConfigureHostedService<RegisterVariableTypesWithJavaScriptHostedService>();
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Module.AddFastEndpointsAssembly<JavaScriptFeature>();
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services.Configure(JintOptions);
        Services.Configure<ExpressionOptions>(JavaScriptExceptionTypeAliasRegistrar.Register);
        Services.Configure<SerializationTypeOptions>(JavaScriptExceptionTypeAliasRegistrar.Register);

        // JavaScript services.
        Services
            .AddScoped<IJavaScriptEvaluator, JintJavaScriptEvaluator>()
            .AddScoped<ITypeDefinitionService, TypeDefinitionService>()
            .AddExpressionDescriptorProvider<JavaScriptExpressionDescriptorProvider>()
            ;

        // Type definition services.
        Services
            .AddScoped<ITypeDefinitionService, TypeDefinitionService>()
            .AddScoped<ITypeDescriber, TypeDescriber>()
            .AddScoped<ITypeDefinitionDocumentRenderer, TypeDefinitionDocumentRenderer>()
            .AddSingleton<ITypeAliasRegistry, Services.TypeAliasRegistry>()
            .AddFunctionDefinitionProvider<CommonFunctionsDefinitionProvider>()
            .AddFunctionDefinitionProvider<ActivityOutputFunctionsDefinitionProvider>()
            .AddFunctionDefinitionProvider<RunJavaScriptFunctionsDefinitionProvider>()
            .AddTypeDefinitionProvider<CommonTypeDefinitionProvider>()
            .AddTypeDefinitionProvider<VariableTypeDefinitionProvider>()
            .AddTypeDefinitionProvider<WorkflowVariablesTypeDefinitionProvider>()
            .AddVariableDefinitionProvider<WorkflowVariablesVariableProvider>()
            ;

        // Handlers.
        Services.AddNotificationHandlersFrom<JavaScriptFeature>();

        // Activities.
        Module.UseWorkflowManagement(management => management.AddActivity<RunJavaScript>());

        // Type Script definitions.
        Services.AddFunctionDefinitionProvider<InputFunctionsDefinitionProvider>();

        // UI property handlers.
        Services.AddScoped<IPropertyUIHandler, RunJavaScriptOptionsProvider>();
    }
}
