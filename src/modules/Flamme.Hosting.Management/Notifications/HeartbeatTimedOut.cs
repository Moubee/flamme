using Flamme.Mediator.Contracts;

namespace Flamme.Hosting.Management.Notifications;

public record HeartbeatTimedOut(string InstanceName) : INotification;
