using Flamme.Expressions.JavaScript.Notifications;
using Flamme.Expressions.JavaScript.TypeDefinitions.Abstractions;
using Flamme.Expressions.JavaScript.TypeDefinitions.Models;
using Flamme.Mediator.Contracts;
using Flamme.Secrets.Contracts;
using JetBrains.Annotations;

namespace Flamme.Secrets.JavaScript.Scripting.JavaScript;

[UsedImplicitly]
public class SecretsJavaScriptHandler(ISecretResolver secretResolver) : FunctionDefinitionProvider, INotificationHandler<EvaluatingJavaScript>
{
    public Task HandleAsync(EvaluatingJavaScript notification, CancellationToken cancellationToken)
    {
        var context = notification.Context;
        notification.Engine.SetValue("getSecret", (Func<string, Task<string>>)(name => secretResolver.ResolveAsync(name, context.CancellationToken)));
        return Task.CompletedTask;
    }

    protected override IEnumerable<FunctionDefinition> GetFunctionDefinitions(TypeDefinitionContext context)
    {
        yield return CreateFunctionDefinition(function => function
            .Name("getSecret")
            .ReturnType("Promise<string>")
            .Parameter("name", "string"));
    }
}
