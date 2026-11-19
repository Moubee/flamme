using Flamme.Expressions.Models;
using Flamme.Mediator.Contracts;
using Fluid;

namespace Flamme.Expressions.Liquid.Notifications;

public record RenderingLiquidTemplate(TemplateContext TemplateContext, ExpressionExecutionContext Context) : INotification;