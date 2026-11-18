using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Elsa.CodeGeneration.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

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
                static (node, _) => node is BaseObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: >= 1 and <= 3 } ||
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
            {
                var source = Generate(compilation, sites.Where(site => site!.Attribute != null).Select(site => site!).ToArray(), output);
                output.AddSource("ElsaFeatureInterceptors.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        });
    }

    private static RootSite? Observe(SemanticModel model, SyntaxNode node, CancellationToken token)
    {
        // The bootstrap generator already diagnoses an authored partial entry in Generated
        // mode. Avoid a second message inside its Dynamic-only implementation.
        if (node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText == "CreateDynamicModule"
            && model.GetEnclosingSymbol(node.SpanStart, token)?.ContainingType is { } host
            && host.ToDisplayString() == CompilerContract.HostMetadataName
            && host.GetMembers("CreateModule").OfType<IMethodSymbol>().Any(method => method.IsPartialDefinition))
            return null;
        if (node is BaseObjectCreationExpressionSyntax creation)
        {
            if (model.GetSymbolInfo(creation, token).Symbol is IMethodSymbol ctor &&
                IsType(ctor.ContainingType, "Elsa.Features", "Elsa.Features.Implementations.Module"))
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
        var catalog = method.Parameters.Any(parameter => IsType(parameter.Type, "Elsa.Features", "Elsa.Features.Services.ExplicitFeatureCatalog"));
        if (method.Name == "CreateModule" && (IsType(method.ContainingType, "Elsa.Features", "Elsa.Features.Services.FeatureModuleCatalogBuilder") ||
            catalog && IsType(method.ContainingType, "Elsa.Features", "Elsa.Extensions.DependencyInjectionExtensions")) ||
            method.Name is "AddElsa" or "ConfigureElsa" && catalog && IsType(method.ContainingType, "Elsa", "Elsa.Extensions.ModuleExtensions"))
            return new RootSite(Diagnostic.Create(CompilerDiagnostics.MutableComposition, name.GetLocation(), method.Name));
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
                locationData.GetInterceptsLocationAttributeSyntax(), invocation);
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

    private static string Generate(Compilation compilation, RootSite[] sites, SourceProductionContext output)
    {
        var producers = FixedProducerModel.Read(compilation, output.CancellationToken);
        var planner = new FixedHostPlanner(compilation, producers, output);
        var plans = planner.Build(sites);
        var source = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        foreach (var directive in producers.ExternAliases)
            source.AppendLine(directive);
        source.AppendLine("""
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
            """);
        for (var n = 0; n < plans.Count; n++)
        {
            var plan = plans[n];
            var name = "Plan" + n;
            var slots = SlotNames(plan.Features);
            source.Append("        private sealed class ").Append(name).AppendLine(" : FixedFeatureModule")
                .AppendLine("        {");
            for (var i = 0; i < plan.Features.Count; i++)
                source.Append("            private readonly FixedFeatureSlot ").Append(slots[i]).AppendLine(";");
            source.Append("            internal ").Append(name).AppendLine("(IServiceCollection services) : base(services)")
                .AppendLine("            {");
            for (var i = 0; i < plan.Features.Count; i++)
                source.Append("                ").Append(slots[i]).Append(" = ").Append(plan.Features[i].Bridge).AppendLine("(this);");
            foreach (var hosted in plan.HostedServices)
                source.Append("                ").Append(hosted).AppendLine("(this);");
            source.AppendLine("                SealDeclarations();").AppendLine("            }");
            foreach (var phase in new[] { "Configure", "Apply" })
            {
                source.Append("            protected override void ").Append(phase).AppendLine("Features()")
                    .AppendLine("            {");
                for (var i = 0; i < plan.Features.Count; i++)
                    source.Append("                ").Append(slots[i]).Append('.').Append(phase).AppendLine("();");
                source.AppendLine("            }");
            }
            source.AppendLine("        }");
            var needsSdk = plan.Sites.Any(site => site.Name != "CreateModule");
            if (needsSdk)
                source.Append("        private static readonly global::System.Func<IServiceCollection, FixedFeatureModule> Create")
                    .Append(name).Append(" = static services => new ").Append(name).AppendLine("(services);");
            foreach (var group in plan.Sites.GroupBy(site => (site.Name, site.HasPolicy)))
            {
                foreach (var site in group)
                    source.Append("        ").AppendLine(site.Attribute);
                var (method, hasPolicy) = group.Key;
                var create = method == "CreateModule";
                source.Append("        internal static IModule Intercept").Append(method).Append(n)
                    .Append("(this IServiceCollection services")
                    .Append(create ? (hasPolicy ? ", CompositionPolicy policy" : "") : ", global::System.Action<IModule>? configure").AppendLine(")")
                    .Append("            => ");
                if (create)
                    source.Append("new ").Append(name).AppendLine("(services);");
                else
                    source.Append("services.").Append(method)
                        .Append("(configure, Create").Append(name).AppendLine(");");
            }
        }
        return source.AppendLine("    }").AppendLine("}").ToString();
    }

    private static List<string> SlotNames(IReadOnlyList<FixedProducerFeature> features)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>(features.Count);
        foreach (var feature in features)
        {
            var type = feature.Type.Name;
            var prefix = "_" + char.ToLowerInvariant(type[0]) + type.Substring(1);
            var name = prefix;
            for (var suffix = 2; !used.Add(name); suffix++)
                name = prefix + suffix;
            names.Add(name);
        }
        return names;
    }

    internal sealed class RootSite
    {
        internal RootSite(string name, bool hasPolicy, string attribute, InvocationExpressionSyntax invocation)
            => (Name, HasPolicy, Attribute, Invocation) = (name, hasPolicy, attribute, invocation);
        internal RootSite(Diagnostic error) => Error = error;
        internal string? Name { get; }
        internal bool HasPolicy { get; }
        internal string? Attribute { get; }
        internal InvocationExpressionSyntax? Invocation { get; }
        internal Diagnostic? Error { get; }
    }
}
