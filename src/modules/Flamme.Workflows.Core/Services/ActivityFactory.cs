using Flamme.Workflows.Models;

namespace Flamme.Workflows;

/// <inheritdoc />
public class ActivityFactory : IActivityFactory
{
    /// <inheritdoc />
    public IActivity Create(Type type, ActivityConstructorContext context)
    {
        return context.CreateActivity(type).Activity;
    }
}