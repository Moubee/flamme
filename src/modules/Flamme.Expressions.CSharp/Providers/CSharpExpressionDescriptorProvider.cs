using Flamme.Expressions.CSharp.Expressions;
using Flamme.Expressions.CSharp.Options;
using Flamme.Expressions.Contracts;
using Flamme.Expressions.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flamme.Expressions.CSharp.Providers;

internal class CSharpExpressionDescriptorProvider(IOptions<CSharpOptions> options) : IExpressionDescriptorProvider
{
    private const string TypeName = "CSharp";

    public IEnumerable<ExpressionDescriptor> GetDescriptors()
    {
        yield return new()
        {
            Type = TypeName,
            DisplayName = "C#",
            IsBrowsable = options.Value.AllowHostCodeExecution,
            HandlerFactory = ActivatorUtilities.GetServiceOrCreateInstance<CSharpExpressionHandler>
        };
    }
}
