using Flamme.Mediator.Contexts;
using Flamme.Mediator.Contracts;
using Flamme.Mediator.Extensions;

namespace Flamme.Mediator.CommandStrategies;

/// <summary>
/// Invokes command handlers using the default strategy. 
/// </summary>
public class DefaultStrategy : ICommandStrategy
{
    /// <inheritdoc />
    public async Task<TResult> ExecuteAsync<TResult>(CommandStrategyContext context)
    {
        var commandContext = context.CommandContext;
        var command = commandContext.Command;
        var commandType = command.GetType();
        var handleMethod = commandType.GetCommandHandlerMethod();
        var handler = context.Handler;
        
        return await handler.InvokeAsync<TResult>(handleMethod, commandContext);
    }
}