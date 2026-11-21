using Flamme.Expressions.Contracts;
using Flamme.Expressions.Models;
using Flamme.Expressions.JavaScript.Expressions;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Expressions.JavaScript.Providers;

[UsedImplicitly]
internal class JavaScriptExpressionDescriptorProvider : IExpressionDescriptorProvider
{
    private const string TypeName = "JavaScript";

    public IEnumerable<ExpressionDescriptor> GetDescriptors()
    {
        yield return new()
        {
            Type = TypeName,
            DisplayName = "JavaScript",
            HandlerFactory = ActivatorUtilities.GetServiceOrCreateInstance<JavaScriptExpressionHandler>
        };
    }
}