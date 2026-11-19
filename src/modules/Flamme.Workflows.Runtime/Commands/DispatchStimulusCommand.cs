using Flamme.Mediator.Contracts;
using Flamme.Mediator.Models;
using Flamme.Workflows.Runtime.Requests;

namespace Flamme.Workflows.Runtime.Commands;

public record DispatchStimulusCommand(DispatchStimulusRequest Request) : ICommand<Unit>;