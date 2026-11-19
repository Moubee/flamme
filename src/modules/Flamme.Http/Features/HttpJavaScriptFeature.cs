using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Http.Scripting.JavaScript;
using Flamme.Expressions.JavaScript.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Http.Features;

/// <summary>
/// Enabled when both <see cref="HttpFeature"/> and <see cref="JavaScriptFeature"/> are enabled.
/// </summary>
[DependencyOf(typeof(HttpFeature))]
[DependencyOf(typeof(JavaScriptFeature))]
public class HttpJavaScriptFeature : FeatureBase
{
    /// <inheritdoc />
    public HttpJavaScriptFeature(IModule module) : base(module)
    {
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services.AddNotificationHandler<HttpJavaScriptHandler>();
    }
}