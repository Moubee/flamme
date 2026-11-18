using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Elsa.CodeGeneration.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RegistrationCompiler;

namespace Elsa.CodeGeneration.Generators;

/// <summary>
/// Supplies compiled registrations at the application's existing module creation calls.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ElsaCompositionHostGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var enabled = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
        {
            var settings = CompilerSettings.Read(options.GlobalOptions);
            return settings.IsHost && settings.IsGenerated && settings.IsCompiler && settings.GenerateMetadata;
        });
        var roots = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is BaseObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 1 or 2 } ||
                    node is IdentifierNameSyntax name && name.Identifier.ValueText is "AddElsa" or "ConfigureElsa" or "CreateModule",
                static (syntax, _) => syntax.Node)
            .Combine(enabled)
            .Where(static input => input.Right)
            .Combine(context.CompilationProvider)
            .Select(static (input, token) =>
            {
                var node = input.Left.Left;
                return Observe(input.Right.GetSemanticModel(node.SyntaxTree), node, token);
            })
            .Where(static site => site is not null)
            .Collect();
        context.RegisterSourceOutput(roots.Combine(context.CompilationProvider), static (output, input) =>
        {
            var (sites, compilation) = input;
            foreach (var site in sites)
            {
                if (site!.Error != null)
                    output.ReportDiagnostic(site.Error);
            }
            if (sites.Any(site => site!.Attribute != null))
                output.AddSource("ElsaFeatureInterceptors.g.cs", SourceText.From(Emit(compilation, sites), Encoding.UTF8));
        });
    }

    private static RootSite? Observe(SemanticModel model, SyntaxNode node, CancellationToken token)
    {
        // An authored legacy dynamic hook owns its choice of discovery. Other source in the
        // same application still receives compiled registrations at ordinary creation roots.
        if (node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText == "CreateDynamicModule"
            && model.GetEnclosingSymbol(node.SpanStart, token)?.ContainingType?.ToDisplayString() == CompilerContract.HostMetadataName)
            return null;
        if (node is BaseObjectCreationExpressionSyntax creation)
        {
            if (model.GetSymbolInfo(creation, token).Symbol is IMethodSymbol ctor &&
                IsType(ctor.ContainingType, "Elsa.Features", "Elsa.Features.Implementations.Module") &&
                ctor.Parameters.All(parameter => !IsType(parameter.Type, "Elsa.Features", "Elsa.Features.Services.ExplicitFeatureCatalog")))
            {
                var location = creation is ObjectCreationExpressionSyntax explicitCreation
                    ? explicitCreation.Type.GetLocation() : creation.NewKeyword.GetLocation();
                return new RootSite(Diagnostic.Create(CompilerDiagnostics.DirectConstruction, location));
            }
            return null;
        }

        var name = (IdentifierNameSyntax)node;
        ExpressionSyntax expression = name.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == name => access,
            MemberBindingExpressionSyntax binding => binding,
            _ => name
        };
        var invocation = expression.Parent as InvocationExpressionSyntax;
        if (invocation?.Expression != expression)
            invocation = null;
        if (model.GetSymbolInfo((SyntaxNode?)invocation ?? expression, token).Symbol is not IMethodSymbol method)
            return null;
        method = method.ReducedFrom ?? method;
        if (!method.IsExtensionMethod || !IsRoot(method))
            return null;
        if (invocation == null)
        {
            if (model.GetTypeInfo(expression, token).ConvertedType?.TypeKind is TypeKind.Delegate or TypeKind.FunctionPointer)
                return new RootSite(Diagnostic.Create(CompilerDiagnostics.IndirectCreation, name.GetLocation(), method.Name));
            return null;
        }

        var locationData = model.GetInterceptableLocation(invocation, token);
        return locationData == null ? new RootSite(Diagnostic.Create(CompilerDiagnostics.IndirectCreation, name.GetLocation(), method.Name)) :
            new RootSite(method.Name, method.Name == "CreateModule" && method.Parameters.Length == 2,
                locationData.GetInterceptsLocationAttributeSyntax());
    }

    private static bool IsRoot(IMethodSymbol method)
    {
        if (method.Name == "CreateModule")
            return IsType(method.ContainingType, "Elsa.Features", "Elsa.Extensions.DependencyInjectionExtensions") &&
                (method.Parameters.Length == 1 || method.Parameters.Length == 2 &&
                    IsType(method.Parameters[1].Type, "Elsa.Features", "Elsa.Features.Models.CompositionPolicy"));
        return method.Name is "AddElsa" or "ConfigureElsa" && method.Parameters.Length == 2 &&
            IsType(method.ContainingType, "Elsa", "Elsa.Extensions.ModuleExtensions");
    }

    private static bool IsType(ITypeSymbol symbol, string assembly, string qualifiedName) =>
        symbol.ContainingAssembly?.Name == assembly && symbol.ToDisplayString() == qualifiedName;

    private static string Emit(Compilation compilation, ImmutableArray<RootSite?> sites)
    {
        var imports = RegistrationSource.GetImports(compilation, ElsaFeatureGenerator.AdapterId, "builder", includeLocalProducer: true);
        var source = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        foreach (var directive in imports.ExternAliases)
            source.Append(directive).Append('\n');
        source.Append("""
            namespace System.Runtime.CompilerServices
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
                file sealed class InterceptsLocationAttribute : global::System.Attribute
                {
                    public InterceptsLocationAttribute(int version, string data) { }
                }
            }

            namespace Elsa.CodeGeneration.Interceptors
            {
                using global::Elsa.Extensions;
                using global::Elsa.Features.Models;
                using global::Elsa.Features.Services;
                using global::Microsoft.Extensions.DependencyInjection;

                internal static class ElsaFeatureInterceptors
                {
                    internal static readonly ExplicitFeatureCatalog Catalog = BuildCatalog();

                    private static ExplicitFeatureCatalog BuildCatalog()
                    {
                        var builder = new ExplicitFeatureCatalogBuilder();

            """);
        foreach (var call in imports.Calls)
            source.Append("            ").Append(call).Append('\n');
        source.Append("            return builder.BuildAvailable();\n        }\n");
        foreach (var group in sites.Where(site => site!.Attribute != null).Select(site => site!)
                     .GroupBy(site => (site.Name, site.HasPolicy)).OrderBy(group => group.Key.Name, StringComparer.Ordinal)
                     .ThenBy(group => group.Key.HasPolicy))
        {
            foreach (var site in group.OrderBy(site => site.Attribute, StringComparer.Ordinal))
                source.Append("        ").Append(site.Attribute).Append('\n');
            var (name, hasPolicy) = group.Key;
            var create = name == "CreateModule";
            source.Append("        internal static IModule Intercept").Append(name)
                .Append("(this IServiceCollection services")
                .Append(create ? (hasPolicy ? ", CompositionPolicy policy" : "") : ", global::System.Action<IModule>? configure").Append(")\n")
                .Append("            => services.").Append(name)
                .Append(create ? "(Catalog" : "(configure, Catalog")
                .Append(hasPolicy ? ", policy" : "").Append(");\n");
        }
        return source.Append("    }\n}\n").ToString();
    }

    private sealed class RootSite
    {
        internal RootSite(string name, bool hasPolicy, string attribute) => (Name, HasPolicy, Attribute) = (name, hasPolicy, attribute);
        internal RootSite(Diagnostic error) => Error = error;
        internal string? Name { get; }
        internal bool HasPolicy { get; }
        internal string? Attribute { get; }
        internal Diagnostic? Error { get; }
    }
}
