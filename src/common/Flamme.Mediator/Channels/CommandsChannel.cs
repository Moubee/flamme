using Flamme.Mediator.Abstractions;
using Flamme.Mediator.Contracts;
using Flamme.Mediator.Middleware.Command;

namespace Flamme.Mediator.Channels;

/// <inheritdoc cref="Flamme.Mediator.Contracts.ICommandsChannel" />
public class CommandsChannel : ChannelBase<CommandContext>, ICommandsChannel
{
}