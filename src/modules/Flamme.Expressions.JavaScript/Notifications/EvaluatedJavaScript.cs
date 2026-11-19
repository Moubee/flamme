using Flamme.Expressions.Models;
using Flamme.Mediator.Contracts;
using Jint;

namespace Flamme.Expressions.JavaScript.Notifications;

/// <summary>
/// This notification is published every time a JavaScript expression has been evaluated.
/// </summary>
public record EvaluatedJavaScript(Engine Engine, ExpressionExecutionContext Context, string Expression, object? Result) : INotification;