using System.Reflection;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Expressions.JavaScript.Features;

namespace Flamme.Expressions.JavaScript.Libraries;

[DependsOn(typeof(JavaScriptFeature))]
public abstract class ScriptModuleFeatureBase(string moduleName, IModule module) : FeatureBase(module)
{
    public override void Configure()
    {
        Module.UseJavaScript(jintOptions =>
        {
            jintOptions.ConfigureEngine((engine, context) =>
            {
                var resourceName = $"Flamme.Expressions.JavaScript.Libraries.ClientLib.dist.{moduleName}.js";
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;
                using var reader = new StreamReader(stream);
                var script = reader.ReadToEnd();
                engine.Execute(script);
            });
        });
    }
}