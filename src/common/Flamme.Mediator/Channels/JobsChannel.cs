using Flamme.Mediator.Abstractions;
using Flamme.Mediator.Contracts;
using Flamme.Mediator.Models;

namespace Flamme.Mediator.Channels;

/// <inheritdoc cref="Flamme.Mediator.Contracts.IJobsChannel" />
public class JobsChannel : ChannelBase<EnqueuedJob>, IJobsChannel
{
}