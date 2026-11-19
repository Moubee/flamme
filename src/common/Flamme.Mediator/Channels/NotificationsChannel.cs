using Flamme.Mediator.Abstractions;
using Flamme.Mediator.Contracts;
using Flamme.Mediator.Middleware.Notification;

namespace Flamme.Mediator.Channels;

/// <inheritdoc cref="Flamme.Mediator.Contracts.INotificationsChannel" />
public class NotificationsChannel : ChannelBase<NotificationContext>, INotificationsChannel
{
}