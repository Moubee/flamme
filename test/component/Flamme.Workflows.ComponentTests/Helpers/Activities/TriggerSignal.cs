using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Flamme.Expressions.Models;
using Flamme.Extensions;
using Flamme.Testing.Shared.Services;
using Flamme.Workflows.Memory;
using Flamme.Workflows.Models;

namespace Flamme.Workflows.ComponentTests.Activities;

public class TriggerSignal : CodeActivity
{
    /// <inheritdoc />
    [JsonConstructor]
    private TriggerSignal(string? source = null, int? line = null) : base(source, line)
    {
    }

    /// <inheritdoc />
    public TriggerSignal(string eventName, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : this(new Literal<string>(eventName), source, line)
    {
    }

    /// <inheritdoc />
    public TriggerSignal(Func<string> eventName, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) 
        : this(Expression.DelegateExpression(eventName), source, line)
    {
    }

    /// <inheritdoc />
    public TriggerSignal(Func<ExpressionExecutionContext, string?> eventName, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) 
        : this(Expression.DelegateExpression(eventName), source, line)
    {
    }

    /// <inheritdoc />
    public TriggerSignal(Variable<string> variable, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : this(source, line) => EventName = new Input<string>(variable);

    /// <inheritdoc />
    public TriggerSignal(Literal<string> literal, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : this(source, line) => EventName = new Input<string>(literal);

    /// <inheritdoc />
    public TriggerSignal(Expression expression, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : this(source, line) => EventName = new Input<string>(expression, new MemoryBlockReference());

    /// <inheritdoc />
    public TriggerSignal(Input<string> eventName, [CallerFilePath] string? source = null, [CallerLineNumber] int? line = null) : this(source, line) => EventName = eventName;

    public Input<string> EventName { get; set; } = null!;

    protected override void Execute(ActivityExecutionContext context)
    {
        var testEventManager = context.GetRequiredService<SignalManager>();
        var eventName = EventName.Get(context);
        testEventManager.Trigger(eventName);
    }
}