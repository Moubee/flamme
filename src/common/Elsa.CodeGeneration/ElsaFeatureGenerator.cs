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

    private sealed class Request(INamedTypeSymbol type, bool hostedService, string requester, Location location)
    {
        public INamedTypeSymbol Type { get; } = type;
        public bool HostedService { get; } = hostedService;
        public string Requester { get; } = requester;
        public Location Location { get; } = location;
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
        return new Request(type, hosted, requester?.ToDisplayString() ?? "<module>", context.Node.GetLocation());
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

        var diagnose = settings.IsHost && settings.IsGenerated && settings.IsCompiler && !CompilerContract.HasAuthoredHost(compilation);
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
                if (diagnose && !RegistrationSource.HasProducer(type.ContainingAssembly, AdapterId))
                    context.ReportDiagnostic(Diagnostic.Create(CompilerDiagnostics.InaccessibleRequest, request.Location,
                        request.Requester, type, $"compile '{type.ContainingAssembly.Name}' with the Elsa feature generator"));
                continue;
            }

            var reason = request.HostedService ? binding.HostedServiceError(type) : binding.FeatureError(type);
            if (reason is not null)
            {
                if (diagnose)
                    context.ReportDiagnostic(Diagnostic.Create(CompilerDiagnostics.InaccessibleRequest, request.Location, request.Requester, type, reason));
                continue;
            }
            if (request.HostedService)
                statements.Add($"target.AddHostedService<{FeatureBinding.TypeName(type)}>();");
            else if (type.IsGenericType)
                pending.Enqueue(type);
        }

        while (pending.Count > 0)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var type = pending.Dequeue();
            if (!processed.Add(type) || binding.FeatureError(type) is not null)
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
        var source = RegistrationSource.EmitProducer(compilation.AssemblyName!, AdapterId, BuilderType, statements);
        context.AddSource("ElsaFeatureRegistrations.g.cs", SourceText.From(source, Encoding.UTF8));
    }
}
