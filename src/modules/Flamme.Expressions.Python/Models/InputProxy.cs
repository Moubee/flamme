using Flamme.Expressions.Helpers;
using Flamme.Expressions.Models;
using Flamme.Extensions;
// ReSharper disable InconsistentNaming

namespace Flamme.Expressions.Python.Models;

/// <summary>
/// A wrapper for accessing activity input values from Python.
/// </summary>
public class InputProxy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InputProxy"/> class.
    /// </summary>
    public InputProxy(ExpressionExecutionContext context)
    {
        Context = context;
    }

    /// <summary>
    /// Gets the expression execution context.
    /// </summary>
    public ExpressionExecutionContext Context { get; set; }
    
    /// <summary>
    /// Gets the value of the specified input.
    /// </summary>
    public object? Get(string name) => Context.GetInput(name);
    
    /// <summary>
    /// Gets the value of the specified input.
    /// </summary>
    public object? Get(Type type, string name) => Context.GetInput(name).ConvertTo(type);
}