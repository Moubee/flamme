using Flamme.Expressions.CSharp.Notifications;
using Flamme.Expressions.CSharp.Options;
using Flamme.Mediator.Contracts;
using JetBrains.Annotations;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Options;

namespace Flamme.Expressions.CSharp.Handlers;

/// <summary>
/// This handler configures the <see cref="ScriptOptions"/> and <see cref="Script"/> from the <see cref="CSharpOptions"/>.
/// </summary>
[UsedImplicitly]
public class ConfigureScriptsFromOptions : INotificationHandler<EvaluatingCSharp>
{
    private readonly CSharpOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureScriptsFromOptions"/> class.
    /// </summary>
    public ConfigureScriptsFromOptions(IOptions<CSharpOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task HandleAsync(EvaluatingCSharp notification, CancellationToken cancellationToken)
    {
        foreach (var scriptOptionsCallback in _options.ConfigureScriptOptionsCallbacks) 
            notification.ScriptOptions = scriptOptionsCallback(notification.ScriptOptions, notification.Context);

        foreach (var scriptCallback in _options.ConfigureScriptCallbacks)
            notification.Script = scriptCallback(notification.Script, notification.Context);
        
        return Task.CompletedTask;
    }
}