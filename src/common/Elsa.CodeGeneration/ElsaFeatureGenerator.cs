using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Elsa.CodeGeneration.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using RegistrationCompiler;

namespace Elsa.CodeGeneration.Generators;

/// <summary>Compiles existing Features declarations and closed registration calls into producer bindings.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class ElsaFeatureGenerator : IIncrementalGenerator
{
    internal const string AdapterId = "Elsa.Features";
    internal const string BuilderType = "global::Elsa.Features.Services.ExplicitFeatureCatalogBuilder";
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Classes without a base list cannot introduce an IFeature implementation. For partial
        // classes the declaration carrying the base list sees attributes from all its parts.
        var declarations = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { BaseList: not null },
                static (ctx, ct) => ctx.SemanticModel.GetDeclaredSymbol((TypeDeclarationSyntax)ctx.Node, ct) as INamedTypeSymbol)
            .Where(static symbol => symbol is { TypeKind: TypeKind.Class, IsAbstract: false });
        var requests = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax invocation && IsRegistrationName(invocation),
                static (ctx, ct) => GetRequest(ctx, ct))
            .Where(static request => request is not null);
        var settings = context.AnalyzerConfigOptionsProvider.Select(static (options, _) => CompilerSettings.Read(options.GlobalOptions));
        var inputs = context.CompilationProvider.Combine(settings).Combine(declarations.Collect()).Combine(requests.Collect());
        context.RegisterSourceOutput(inputs, static (ctx, input) =>
        {
            var (((compilation, configuration), types), calls) = input;
            if (configuration.GenerateMetadata)
                Emit(ctx, compilation, configuration, types, calls);
        });
    }

    private static bool IsRegistrationName(InvocationExpressionSyntax invocation)
    {
        var name = invocation.Expression switch
        {
            SimpleNameSyntax simple => simple,
            MemberAccessExpressionSyntax access => access.Name,
            MemberBindingExpressionSyntax binding => binding.Name,
            _ => null
        };
        return name?.Identifier.ValueText is "ConfigureHostedService" or "Configure" or "Use";
    }

    private sealed class Request(INamedTypeSymbol type, bool hostedService, string requester, InvocationExpressionSyntax syntax)
    {
        public INamedTypeSymbol Type { get; } = type;
        public bool HostedService { get; } = hostedService;
        public string Requester { get; } = requester;
        public InvocationExpressionSyntax Syntax { get; } = syntax;
    }

    private static Request? GetRequest(GeneratorSyntaxContext context, CancellationToken cancellation)
    {
        if (context.SemanticModel.GetOperation(context.Node, cancellation) is not IInvocationOperation invocation)
            return null;

        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var features = context.SemanticModel.Compilation.GetTypeByMetadataName("Elsa.Features.Services.IFeature")?.ContainingAssembly;
        // Elsa.Common also declares Elsa.Extensions.DependencyInjectionExtensions. Resolve
        // the owner in the Features assembly when matching the module.Use<T>() extension.
        bool DeclaredOn(string name) => SymbolEqualityComparer.Default.Equals(method.ContainingType, features?.GetTypeByMetadataName(name));
        var isModule = DeclaredOn("Elsa.Features.Services.IModule") || DeclaredOn("Elsa.Features.Implementations.Module");
        var hosted = method.Name == "ConfigureHostedService" && (isModule || DeclaredOn("Elsa.Features.Abstractions.FeatureBase"));
        if (!hosted && !(method.Name == "Configure" && isModule) &&
            !(method.Name == "Use" && DeclaredOn("Elsa.Extensions.DependencyInjectionExtensions")))
            return null;

        var type = invocation.TargetMethod.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        if (hosted && type is null)
        {
            var argument = invocation.Arguments.FirstOrDefault(arg => arg.Parameter?.Ordinal == 0);
            if (argument?.Value is ITypeOfOperation { TypeOperand: INamedTypeSymbol service })
                type = service;
        }
        if (type is null || !FeatureBinding.IsClosed(type))
            return null;

        var requester = context.SemanticModel.GetEnclosingSymbol(context.Node.SpanStart, cancellation);
        return new Request(type, hosted, requester?.ToDisplayString() ?? "<module>", (InvocationExpressionSyntax)context.Node);
    }

    // An explicit catalog or injected module may have bindings unknown to the compiler.
    // Diagnose only when the receiver comes from an ordinary, interceptable source root.
    private static bool IsCompiledRoot(Request request, Compilation compilation, CancellationToken cancellation)
    {
        var model = compilation.GetSemanticModel(request.Syntax.SyntaxTree);
        if (model.GetOperation(request.Syntax, cancellation) is not IInvocationOperation invocation)
            return false;
        var receiver = invocation.Instance ?? invocation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0)?.Value;
        if (receiver is ILocalReferenceOperation local)
        {
            // Recognize the immediately following call only. Assignments, aliases and
            // arbitrary intervening code can change which provider a local refers to.
            var declaration = local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellation) as VariableDeclaratorSyntax;
            if (declaration?.Initializer is null || declaration.Parent is not VariableDeclarationSyntax { Variables.Count: 1 } ||
                declaration.Parent.Parent is not LocalDeclarationStatementSyntax statement ||
                request.Syntax.Parent is not ExpressionStatementSyntax use || use.GetFirstToken().GetPreviousToken() != statement.SemicolonToken)
                return false;
            receiver = model.GetOperation(declaration.Initializer.Value, cancellation);
        }
        if (receiver is IParameterReferenceOperation parameter && parameter.Parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } lambda)
        {
            var source = receiver.Syntax.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().FirstOrDefault();
            if (source is null || !(source.Body == request.Syntax || source.Body is BlockSyntax body && body.Statements.FirstOrDefault() is ExpressionStatementSyntax first && first.Expression == request.Syntax) ||
                model.GetOperation(source, cancellation) is not IAnonymousFunctionOperation function ||
                !SymbolEqualityComparer.Default.Equals(function.Symbol, lambda) || function.Parent?.Parent?.Parent is not IInvocationOperation configure)
                return false;
            receiver = configure;
        }
        if (receiver is not IInvocationOperation root)
            return false;
        var method = root.TargetMethod.ReducedFrom ?? root.TargetMethod;
        var owner = method.ContainingType;
        return method.Name == "CreateModule" && owner.ContainingAssembly.Name == "Elsa.Features" &&
               owner.ToDisplayString() == "Elsa.Extensions.DependencyInjectionExtensions" &&
               (method.Parameters.Length == 1 || method.Parameters.Length == 2 && method.Parameters[1].Type.ToDisplayString() == "Elsa.Features.Models.CompositionPolicy")
               || method.Name is "AddElsa" or "ConfigureElsa" && owner.ContainingAssembly.Name == "Elsa" &&
               owner.ToDisplayString() == "Elsa.Extensions.ModuleExtensions" && method.Parameters.Length == 2;
    }

    private static void Emit(SourceProductionContext context, Compilation compilation, CompilerSettings settings,
        ImmutableArray<INamedTypeSymbol?> declarations, ImmutableArray<Request?> requests)
    {
        var feature = compilation.GetTypeByMetadataName("Elsa.Features.Services.IFeature");
        if (feature is null)
            return;

        var binding = new FeatureBinding(compilation, feature);
        var pending = new Queue<INamedTypeSymbol>();
        var processed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var statements = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in declarations)
            if (type is not null && binding.IsFeature(type) && FeatureBinding.IsClosed(type))
                pending.Enqueue(type);

        var generatedHost = settings.IsHost && settings.IsGenerated && settings.IsCompiler;
        foreach (var request in requests)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (request is null)
                continue;
            var type = request.Type;
            // A referenced non-generic feature's metadata belongs to its defining assembly.
            // Its helper may legitimately refer to internal dependency types hidden from us.
            if (!request.HostedService && !type.IsGenericType && !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
            {
                if (generatedHost)
                {
                    var issue = RegistrationSource.HasProducer(type.ContainingAssembly, AdapterId)
                        ? binding.FeatureError(type, type.ContainingAssembly)
                        : $"compile '{type.ContainingAssembly.Name}' with the Elsa feature generator";
                    if (issue is not null)
                        ReportIfCompiled(request, issue);
                }
                continue;
            }

            var reason = request.HostedService ? binding.HostedServiceError(type) : binding.FeatureError(type, compilation.Assembly);
            if (reason is not null)
            {
                ReportIfCompiled(request, reason);
                continue;
            }
            if (request.HostedService)
                statements.Add($"target.AddHostedService<{binding.TypeName(type)}>();");
            else if (type.IsGenericType)
                pending.Enqueue(type);
        }

        while (pending.Count > 0)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var type = pending.Dequeue();
            if (!processed.Add(type) || binding.FeatureError(type, compilation.Assembly) is not null)
                continue;
            var (strong, conditional) = binding.Dependencies(type);
            statements.Add(binding.Registration(type, strong, conditional));
            // The producer that sees the closed specialization can name it, including when
            // the open feature was defined in a different assembly.
            foreach (var dependency in strong.Concat(conditional))
                if (dependency.IsGenericType && FeatureBinding.IsClosed(dependency))
                    pending.Enqueue(dependency);
        }

        if (statements.Count == 0 && !(settings.IsHost && settings.IsGenerated))
            return;
        var source = RegistrationSource.EmitProducer(compilation.AssemblyName!, AdapterId, BuilderType, statements, binding.ExternAliases);
        context.AddSource("ElsaFeatureRegistrations.g.cs", SourceText.From(source, Encoding.UTF8));

        void ReportIfCompiled(Request request, string reason)
        {
            if (generatedHost && IsCompiledRoot(request, compilation, context.CancellationToken))
                context.ReportDiagnostic(Diagnostic.Create(CompilerDiagnostics.InaccessibleRequest, request.Syntax.GetLocation(),
                    request.Requester, request.Type, reason));
        }
    }
}
