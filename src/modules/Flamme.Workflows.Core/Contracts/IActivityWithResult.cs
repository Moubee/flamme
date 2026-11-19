using Flamme.Workflows.Models;

namespace Flamme.Workflows;

/// <summary>
/// Contract for custom activities that return a result.
/// </summary>
/// <typeparam name="T">The type of the result.</typeparam>
public interface IActivityWithResult<T> : IActivityWithResult
{
    /// <summary>
    /// The result of the activity.
    /// </summary>
    new Output<T>? Result { get; set; }
}

/// <summary>
/// Contract for custom activities that return a result.
/// </summary>
public interface IActivityWithResult
{
    /// <summary>
    /// The result of the activity.
    /// </summary>
    Output? Result { get; set; }
}