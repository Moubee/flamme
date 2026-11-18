using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Elsa.CodeGeneration.Compiler;

internal readonly struct CompilerSettings : IEquatable<CompilerSettings>
{
    private CompilerSettings(bool hasIsHost, bool isHost, bool generateMetadata, string mode, string bootstrap, string schema)
    {
        HasIsHost = hasIsHost;
        IsHost = isHost;
        GenerateMetadata = generateMetadata;
        Mode = mode;
        Bootstrap = bootstrap;
        Schema = schema;
    }

    internal bool HasIsHost { get; }
    internal bool IsHost { get; }
    internal bool GenerateMetadata { get; }
    internal string Mode { get; }
    internal string Bootstrap { get; }
    internal string Schema { get; }
    internal bool IsGenerated => string.Equals(Mode, "Generated", StringComparison.OrdinalIgnoreCase);
    internal bool IsDynamic => string.Equals(Mode, "Dynamic", StringComparison.OrdinalIgnoreCase);
    internal bool IsCompiler => string.Equals(Bootstrap, "Compiler", StringComparison.OrdinalIgnoreCase);
    internal bool IsManual => string.Equals(Bootstrap, "Manual", StringComparison.OrdinalIgnoreCase);

    internal static CompilerSettings Read(AnalyzerConfigOptions options)
    {
        return new CompilerSettings(options.TryGetValue("build_property.ElsaIsHost", out _), Boolean("ElsaIsHost"), Boolean("ElsaGenerateMetadata"),
            Value("ElsaEffectiveExecutionMode", string.Empty), Value("ElsaHostBootstrap", string.Empty),
            Value("ElsaModuleExportSchemaVersion", string.Empty));

        bool Boolean(string name) => bool.TryParse(Value(name, "false"), out var value) && value;
        string Value(string name, string fallback) => options.TryGetValue("build_property." + name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    public bool Equals(CompilerSettings other) => HasIsHost == other.HasIsHost && IsHost == other.IsHost && GenerateMetadata == other.GenerateMetadata
        && string.Equals(Mode, other.Mode, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Bootstrap, other.Bootstrap, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Schema, other.Schema, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is CompilerSettings other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = HasIsHost ? 1 : 0;
            hash = hash * 31 + (IsHost ? 1 : 0);
            hash = hash * 31 + (GenerateMetadata ? 1 : 0);
            hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(Mode);
            hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(Bootstrap);
            return hash * 31 + StringComparer.Ordinal.GetHashCode(Schema);
        }
    }
}

internal enum HostFailure
{
    None,
    Host,
    Entry,
    Hook,
    ManualSelection
}

internal sealed class HostContractResult
{
    internal HostContractResult(HostFailure failure, string message, Location location, IMethodSymbol? entry = null)
    {
        Failure = failure;
        Message = message;
        Location = location;
        Entry = entry;
    }
    internal HostFailure Failure { get; }
    internal string Message { get; }
    internal Location Location { get; }
    internal IMethodSymbol? Entry { get; }
    internal bool Success => Failure == HostFailure.None;
}

internal static class CompilerContract
{
    internal static bool HasAuthoredHost(Compilation compilation) =>
        compilation.Assembly.GetTypeByMetadataName(HostMetadataName) is not null;

    internal const string HostMetadataName = "Elsa.CodeGeneration.ElsaHost";
    internal const string BootstrapHintName = "ElsaHost.Bootstrap.g.cs";
    private const string ModuleTypeName = "Elsa.Features.Services.IModule";
    private const string ServicesTypeName = "Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string PolicyTypeName = "Elsa.Features.Models.CompositionPolicy";
    private const string ExportTypeName = "Elsa.Features.Services.FeatureModuleExport";

    internal static HostContractResult Validate(Compilation compilation, CompilerSettings settings, CancellationToken cancellationToken)
    {
        var host = compilation.GetTypeByMetadataName(HostMetadataName);
        if (!settings.HasIsHost)
            return Fail(HostFailure.Host, "Authored ElsaHost requires an explicit compiler-visible ElsaIsHost setting. Import Elsa.CodeGeneration.Build and set ElsaIsHost=true for a host; missing build assets must not choose a default bootstrap.", SourceLocation(host));
        if ((!settings.IsGenerated && !settings.IsDynamic) || (!settings.IsCompiler && !settings.IsManual) || settings.Schema != "1")
            return Fail(HostFailure.Host, "ElsaHost requires compiler-visible ElsaEffectiveExecutionMode=Dynamic|Generated, ElsaHostBootstrap=Compiler|Manual, and ElsaModuleExportSchemaVersion=1. Import the Elsa.CodeGeneration.Build assets and resolve these settings before compilation.", SourceLocation(host));
        if (host is null || !SymbolEqualityComparer.Default.Equals(host.ContainingAssembly, compilation.Assembly)
            || host.TypeKind != TypeKind.Class || !host.IsStatic || host.Arity != 0 || host.DeclaredAccessibility != Accessibility.Internal
            || host.DeclaringSyntaxReferences.Length == 0
            || host.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken) is not ClassDeclarationSyntax declaration || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            return Fail(HostFailure.Host, "Declare an authored internal static partial class Elsa.CodeGeneration.ElsaHost in this compilation.", SourceLocation(host));
        var runtimeSchema = compilation.GetTypeByMetadataName("Elsa.CodeGeneration.ModuleExportSchema")?
            .GetMembers("CurrentVersion").OfType<IFieldSymbol>().FirstOrDefault(field => field.IsConst && field.Type.SpecialType == SpecialType.System_Int32);
        if (runtimeSchema?.ConstantValue is not int schema || schema != 1)
            return Fail(HostFailure.Host, $"ElsaHost requires referenced Elsa.CodeGeneration.ModuleExportSchema.CurrentVersion=1 matching ElsaModuleExportSchemaVersion={settings.Schema}; found {runtimeSchema?.ConstantValue?.ToString() ?? "no compatible runtime schema constant"}. Reference compatible Elsa.CodeGeneration.Abstractions and Elsa.Features runtime packages.", SourceLocation(host));

        var entries = host.GetMembers("CreateModule").OfType<IMethodSymbol>().Where(method => method.PartialDefinitionPart is null).ToArray();
        if (entries.Length != 1 || !ModuleSignature(compilation, entries[0], Accessibility.Internal))
            return Fail(HostFailure.Entry, "Declare exactly one internal static IModule CreateModule(IServiceCollection services, CompositionPolicy policy) with nonnullable reference types on ElsaHost.", SourceLocation(host));
        var entry = entries[0];
        var entrySyntax = entry.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken)).OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (entrySyntax is null)
            return Fail(HostFailure.Entry, "ElsaHost.CreateModule must be authored in source.", SourceLocation(entry));
        if (settings.IsCompiler)
        {
            if (!entry.IsPartialDefinition || !entrySyntax.Modifiers.Any(SyntaxKind.PartialKeyword) || HasBody(entrySyntax)
                || entry.IsExtensionMethod || entry.Parameters.Any(parameter => parameter.IsParams || parameter.ScopedKind != ScopedKind.None)
                || entrySyntax.Modifiers.Any(SyntaxKind.UnsafeKeyword) || entrySyntax.Modifiers.Any(SyntaxKind.NewKeyword) || entrySyntax.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)
                || entry.PartialImplementationPart is not null)
                return Fail(HostFailure.Entry, "Compiler bootstrap requires an authored internal static partial IModule CreateModule(IServiceCollection services, CompositionPolicy policy) declaration without a body, extension/params/scoped parameters, or unsafe/new/readonly modifiers; the Elsa generator supplies its implementation.", SourceLocation(entry));
        }
        else if (entrySyntax.Modifiers.Any(SyntaxKind.PartialKeyword) || !HasBody(entrySyntax))
            return Fail(HostFailure.Entry, "Manual bootstrap requires an ordinary authored internal static IModule CreateModule(IServiceCollection services, CompositionPolicy policy) method with a body.", SourceLocation(entry));

        var hookName = settings.IsGenerated ? "GetModuleExports" : "CreateDynamicModule";
        var hooks = host.GetMembers(hookName).OfType<IMethodSymbol>().Where(method => method.PartialDefinitionPart is null).ToArray();
        if (hooks.Length != 1 || !(settings.IsGenerated ? ExportSignature(compilation, hooks[0]) : ModuleSignature(compilation, hooks[0], Accessibility.Private))
            || !hooks[0].DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken) is MethodDeclarationSyntax declaration && HasBody(declaration)))
            return Fail(HostFailure.Hook, settings.IsGenerated
                ? "Generated composition requires private static FeatureModuleExport[] ElsaHost.GetModuleExports() with an authored body. Return every selected typed export; runtime catalog validation checks its coverage."
                : "Dynamic composition requires private static IModule ElsaHost.CreateDynamicModule(IServiceCollection services, CompositionPolicy policy) with an authored body.", SourceLocation(entry));

        if (settings.IsManual)
        {
            if (!ManualReturnsSelectedPath(compilation, entry, entrySyntax, hooks[0], settings, cancellationToken))
                return Fail(HostFailure.ManualSelection, settings.IsGenerated
                    ? "Manual Generated ElsaHost.CreateModule must consist of one returned FeatureModuleCatalogBuilder.CreateModule(services, GetModuleExports(), policy) invocation forwarding its authored parameters. Use ELSA_GENERATED/ELSA_DYNAMIC conditional compilation; discarded exports, additional statements, lambdas, local functions, and alternate bootstrap paths are not supported."
                    : "Manual Dynamic ElsaHost.CreateModule must consist of one returned CreateDynamicModule(services, policy) invocation forwarding its authored parameters. Use ELSA_GENERATED/ELSA_DYNAMIC conditional compilation; additional statements, lambdas, local functions, and alternate bootstrap paths are not supported.", SourceLocation(entry));
        }
        return new HostContractResult(HostFailure.None, string.Empty, SourceLocation(entry), entry);
    }

    private static bool ModuleSignature(Compilation compilation, IMethodSymbol method, Accessibility accessibility)
    {
        return OrdinaryStaticMethod(method, accessibility) && SameType(method.ReturnType, compilation, ModuleTypeName)
            && method.ReturnNullableAnnotation != NullableAnnotation.Annotated
            && method.Parameters.Length == 2 && method.Parameters[0].NullableAnnotation != NullableAnnotation.Annotated && method.Parameters.All(parameter => parameter.RefKind == RefKind.None)
            && SameType(method.Parameters[0].Type, compilation, ServicesTypeName)
            && SameType(method.Parameters[1].Type, compilation, PolicyTypeName);
    }

    private static bool ExportSignature(Compilation compilation, IMethodSymbol method)
    {
        return OrdinaryStaticMethod(method, Accessibility.Private) && method.Parameters.Length == 0
            && method.ReturnNullableAnnotation != NullableAnnotation.Annotated
            && method.ReturnType is IArrayTypeSymbol array && array.Rank == 1 && array.ElementNullableAnnotation != NullableAnnotation.Annotated
            && SameType(array.ElementType, compilation, ExportTypeName);
    }

    private static bool OrdinaryStaticMethod(IMethodSymbol method, Accessibility accessibility) => method.MethodKind == MethodKind.Ordinary
        && method.IsStatic && method.DeclaredAccessibility == accessibility && !method.IsGenericMethod && !method.IsAbstract && !method.IsExtern
        && !method.IsAsync && !method.ReturnsByRef && !method.ReturnsByRefReadonly;
    private static bool SameType(ITypeSymbol type, Compilation compilation, string metadataName) => SymbolEqualityComparer.Default.Equals(type, compilation.GetTypeByMetadataName(metadataName));
    private static bool HasBody(MethodDeclarationSyntax declaration) => declaration.Body is not null || declaration.ExpressionBody is not null;
    private static Location SourceLocation(ISymbol? symbol) => symbol?.Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;
    private static HostContractResult Fail(HostFailure failure, string message, Location location) => new(failure, message, location);

    private static bool ManualReturnsSelectedPath(Compilation compilation, IMethodSymbol entry, MethodDeclarationSyntax declaration,
        IMethodSymbol selectedHook, CompilerSettings settings, CancellationToken cancellationToken)
    {
        ExpressionSyntax? expression = declaration.ExpressionBody?.Expression;
        if (expression is null && declaration.Body?.Statements.Count == 1 && declaration.Body.Statements[0] is ReturnStatementSyntax returned)
            expression = returned.Expression;
        // Roslyn associates the operation with the expression inside the parentheses.
        while (expression is ParenthesizedExpressionSyntax parentheses)
            expression = parentheses.Expression;
        if (expression is null || Unwrap(compilation.GetSemanticModel(declaration.SyntaxTree).GetOperation(expression, cancellationToken)) is not IInvocationOperation invocation)
            return false;
        if (settings.IsDynamic)
            return SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, selectedHook) && invocation.Arguments.Length == 2
                && Forwarded(invocation, 0, entry.Parameters[0]) && Forwarded(invocation, 1, entry.Parameters[1]);

        var owner = compilation.GetTypeByMetadataName("Elsa.Features.Services.FeatureModuleCatalogBuilder");
        if (invocation.TargetMethod.Name != "CreateModule" || !invocation.TargetMethod.IsStatic
            || !SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, owner) || invocation.Arguments.Length != 3
            || !SameType(invocation.TargetMethod.ReturnType, compilation, ModuleTypeName)
            || !Forwarded(invocation, 0, entry.Parameters[0]) || !Forwarded(invocation, 2, entry.Parameters[1]))
            return false;
        var exports = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1);
        return Unwrap(exports?.Value) is IInvocationOperation exportsCall && exportsCall.Arguments.Length == 0
            && SymbolEqualityComparer.Default.Equals(exportsCall.TargetMethod, selectedHook);
    }

    private static bool Forwarded(IInvocationOperation invocation, int ordinal, IParameterSymbol parameter)
    {
        var argument = invocation.Arguments.FirstOrDefault(value => value.Parameter?.Ordinal == ordinal);
        return Unwrap(argument?.Value) is IParameterReferenceOperation reference && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);
    }

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (true)
        {
            if (operation is IConversionOperation conversion && conversion.IsImplicit)
                operation = conversion.Operand;
            else if (operation is IParenthesizedOperation parentheses)
                operation = parentheses.Operand;
            else
                return operation;
        }
    }
}