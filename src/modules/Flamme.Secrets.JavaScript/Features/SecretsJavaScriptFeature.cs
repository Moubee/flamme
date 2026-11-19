using Flamme.Expressions.JavaScript.Extensions;
using Flamme.Expressions.JavaScript.Features;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Secrets.Features;
using Flamme.Secrets.JavaScript.Scripting.JavaScript;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Secrets.JavaScript.Features;

[DependsOn(typeof(JavaScriptFeature))]
[DependsOn(typeof(SecretsFeature))]
public class SecretsJavaScriptFeature(IModule module) : FeatureBase(module)
{
    public override void Apply()
    {
        Services
            .AddNotificationHandler<SecretsJavaScriptHandler>()
            .AddFunctionDefinitionProvider<SecretsJavaScriptHandler>();
    }
}
