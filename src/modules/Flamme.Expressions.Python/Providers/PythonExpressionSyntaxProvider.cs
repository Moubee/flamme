using Flamme.Expressions.Contracts;
using Flamme.Expressions.Models;
using Flamme.Extensions;
using Flamme.Expressions.Python.Expressions;
using Flamme.Expressions.Python.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flamme.Expressions.Python.Providers;

internal class PythonExpressionDescriptorProvider(IOptions<PythonOptions> options) : IExpressionDescriptorProvider
{
    private const string TypeName = "Python";

    public IEnumerable<ExpressionDescriptor> GetDescriptors()
    {
        yield return new()
        {
            Type = TypeName,
            DisplayName = "Python",
            IsBrowsable = options.Value.AllowHostCodeExecution,
            Properties = new { MonacoLanguage = "python" }.ToDictionary(),
            HandlerFactory = ActivatorUtilities.GetServiceOrCreateInstance<PythonExpressionHandler>
        };
    }
}
