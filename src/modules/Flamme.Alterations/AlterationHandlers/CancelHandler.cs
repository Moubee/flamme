using Flamme.Alterations.AlterationTypes;
using Flamme.Alterations.Core.Abstractions;
using Flamme.Alterations.Core.Contexts;
using JetBrains.Annotations;

namespace Flamme.Alterations.AlterationHandlers;

/// <summary>
/// Upgrades the version of the workflow instance.
/// </summary>
[UsedImplicitly]
public class CancelHandler : AlterationHandlerBase<Cancel>
{
    /// <inheritdoc />
    protected override ValueTask HandleAsync(AlterationContext context, Cancel alteration)
    {
        context.WorkflowExecutionContext.Cancel();
        
        context.Succeed();
        return ValueTask.CompletedTask;
    }
}