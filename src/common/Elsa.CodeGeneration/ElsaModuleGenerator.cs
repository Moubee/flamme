using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Elsa.CodeGeneration.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Elsa.CodeGeneration.Generators;

/// <summary>
/// Compiles explicit producer declarations into immutable, host-independent type manifests.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ElsaModuleGenerator : IIncrementalGenerator
{
    private const string AttributeName = "Elsa.CodeGeneration.ElsaModuleAttribute";
    private static readonly DiagnosticDescriptor Shell = Rule("ELSAGEN005", "Declare a partial module export");
    private static readonly DiagnosticDescriptor Identity = Rule("ELSAGEN006", "Declare a valid module identity and revision");
    private static readonly DiagnosticDescriptor Shape = Rule("ELSAGEN007", "Declare an accessible closed type");
    private static readonly DiagnosticDescriptor RepeatedType = Rule("ELSAGEN008", "Declare each module type once");
    private static readonly DiagnosticDescriptor RepeatedId = Rule("ELSAGEN009", "Declare a unique module identity");

    // Emit framework and tuple types by CLR name as well, independent of aliases in the producer.
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.ExpandNullable | SymbolDisplayMiscellaneousOptions.ExpandValueTuple);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var enabled = context.AnalyzerConfigOptionsProvider.Select(static (options, _) => CompilerSettings.Read(options.GlobalOptions).GenerateMetadata);
        var modules = context.SyntaxProvider.ForAttributeWithMetadataName(AttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (input, token) => ReadModule(input, token))
            .Where(static module => module is not null)
            .Combine(enabled).Where(static pair => pair.Right)
            .Select(static (pair, _) => pair.Left!);

        // Only a changed set of identities needs a new cross-export collision check.
        var duplicateIds = modules.Select(static (module, _) => module.Id)
            .Where(static id => id is not null)
            .Collect()
            .Select(static (ids, _) => ids.GroupBy(id => id!, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToImmutableHashSet(StringComparer.Ordinal));
        var checkedModules = modules.Combine(duplicateIds);
        context.RegisterSourceOutput(checkedModules, static (output, input) =>
        {
            var (module, duplicates) = input;
            foreach (var diagnostic in module.Diagnostics)
                output.ReportDiagnostic(diagnostic);
            if (module.Id is not null && duplicates.Contains(module.Id))
                output.ReportDiagnostic(Diagnostic.Create(RepeatedId, module.IdLocation, $"Module ID '{module.Id}' is declared by multiple exports in this assembly. Give each export its own ID."));
        });

        // Project away syntax locations before comparing generated outputs across edits.
        var sources = checkedModules.Where(static pair => pair.Left.Source is not null && !pair.Right.Contains(pair.Left.Id!))
            .Select(static (pair, _) => (pair.Left.HintName, Source: pair.Left.Source!));
        context.RegisterSourceOutput(sources, static (output, input) =>
            output.AddSource(input.HintName, SourceText.From(input.Source, Encoding.UTF8)));
    }

    private static ModuleDeclaration? ReadModule(GeneratorAttributeSyntaxContext input, CancellationToken token)
    {
        var shell = (INamedTypeSymbol)input.TargetSymbol;
        var declaration = (TypeDeclarationSyntax)input.TargetNode;
        // The compiler already diagnoses a repeated non-repeatable attribute, including across partial parts.
        if (shell.GetAttributes().Count(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, input.Attributes[0].AttributeClass)) != 1)
            return null;
        var attribute = input.Attributes[0];
        if (attribute.ApplicationSyntaxReference?.GetSyntax(token) is not AttributeSyntax syntax ||
            input.SemanticModel.GetOperation(syntax, token) is not IAttributeOperation { Operation: IObjectCreationOperation creation } ||
            attribute.ConstructorArguments.Length != 3)
            return null; // C# reports the failed attribute binding.

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        if (declaration is not ClassDeclarationSyntax || shell.ContainingType is not null || shell.Arity != 0 || !shell.IsStatic ||
            shell.DeclaredAccessibility != Accessibility.Public || shell.Name == "Manifest" ||
            shell.DeclaringSyntaxReferences.Any(part => part.GetSyntax(token) is not ClassDeclarationSyntax cls || !cls.Modifiers.Any(SyntaxKind.PartialKeyword)))
            diagnostics.Add(Diagnostic.Create(Shell, declaration.Identifier.GetLocation(),
                "Declare the export as a top-level public static partial class without type parameters. Choose a class name other than Manifest."));

        foreach (var member in shell.GetMembers().Where(member => member.Name == "Manifest" ||
                     member is IMethodSymbol { Name: "get_Manifest", Arity: 0, Parameters.Length: 0 }))
        {
            diagnostics.Add(Diagnostic.Create(Shell, member.Locations.FirstOrDefault(location => location.IsInSource),
                "The Elsa generator supplies the Manifest property. Rename the conflicting member."));
        }

        var id = attribute.ConstructorArguments[0].Value as string;
        var idLocation = Argument(0).Syntax.GetLocation();
        var version = attribute.ConstructorArguments[1].Value is int revision ? revision : 0;
        if (string.IsNullOrWhiteSpace(id) || id != id!.Trim())
        {
            diagnostics.Add(Diagnostic.Create(Identity, idLocation, "Give the module a nonblank ID without leading or trailing whitespace."));
            id = null;
        }
        if (version <= 0)
            diagnostics.Add(Diagnostic.Create(Identity, Argument(1).Syntax.GetLocation(), "Give the module a positive contract revision."));

        var types = ReadTypes(input.SemanticModel, shell, declaration, attribute.ConstructorArguments[2], Argument(2), diagnostics, token);
        var source = diagnostics.Count == 0 ? Render(shell, id!, version, types) : null;
        return new ModuleDeclaration(id, idLocation, diagnostics.ToImmutable(), HintName(shell), source);

        IOperation Argument(int ordinal) => creation.Arguments.First(argument => argument.Parameter?.Ordinal == ordinal).Value;
    }

    private static SortedSet<string> ReadTypes(SemanticModel model, INamedTypeSymbol shell, TypeDeclarationSyntax declaration,
        TypedConstant types, IOperation argument, ImmutableArray<Diagnostic>.Builder diagnostics, CancellationToken token)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        if (types.Kind != TypedConstantKind.Array || types.IsNull)
        {
            diagnostics.Add(Diagnostic.Create(Shape, argument.Syntax.GetLocation(), "Supply a list of typeof expressions or an empty type list."));
            return names;
        }

        // The bound params array maps both expanded arguments and explicit array initializers back to source.
        var elements = (argument as IArrayCreationOperation)?.Initializer?.ElementValues ?? [];
        for (var i = 0; i < types.Values.Length; ++i)
        {
            token.ThrowIfCancellationRequested();
            var location = (i < elements.Length ? elements[i] : argument).Syntax.GetLocation();
            if (types.Values[i].Value is not INamedTypeSymbol type || !CanName(type, model.Compilation, shell))
            {
                diagnostics.Add(Diagnostic.Create(Shape, location, "Declare a closed named type accessible from the generated export. Open, file-local, void and non-named types cannot be exported."));
                continue;
            }

            var name = type.ToDisplayString(TypeFormat);
            // Extern-alias-only and ambiguous metadata names cannot be referenced from a separate generated file.
            var bound = model.GetSpeculativeTypeInfo(declaration.Identifier.SpanStart, SyntaxFactory.ParseTypeName(name), SpeculativeBindingOption.BindAsTypeOrNamespace);
            if (!SymbolEqualityComparer.Default.Equals(bound.Type, type))
                diagnostics.Add(Diagnostic.Create(Shape, location, $"Expose '{name}' under an unambiguous, globally accessible name."));
            else if (!names.Add(name))
                diagnostics.Add(Diagnostic.Create(RepeatedType, location, $"Type '{name}' is already declared by this module. Remove the repeated type."));
        }
        return names;
    }

    private static bool CanName(ITypeSymbol type, Compilation compilation, INamedTypeSymbol shell) => type switch
    {
        IArrayTypeSymbol array => CanName(array.ElementType, compilation, shell),
        INamedTypeSymbol named => named.TypeKind != TypeKind.Error && named.SpecialType != SpecialType.System_Void &&
            !named.IsUnboundGenericType && !named.IsFileLocal && compilation.IsSymbolAccessibleWithin(named, shell) &&
            (named.ContainingType is null || CanName(named.ContainingType, compilation, shell)) &&
            named.TypeArguments.All(argument => CanName(argument, compilation, shell)),
        _ => false
    };

    private static string Render(INamedTypeSymbol shell, string id, int version, SortedSet<string> types)
    {
        var ns = shell.ContainingNamespace.IsGlobalNamespace ? "" : $"namespace {shell.ContainingNamespace.ToDisplayString()};\n\n";
        var className = SyntaxFacts.GetKeywordKind(shell.Name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(shell.Name) != SyntaxKind.None
            ? "@" + shell.Name : shell.Name;
        return "// <auto-generated/>\n#nullable enable\n" + ns + "public static partial class " + className + "\n{\n"
            + "    public static global::Elsa.CodeGeneration.ModuleTypeManifest Manifest { get; } = new(\n"
            + "        new global::Elsa.CodeGeneration.ModuleExportDescriptor(" + SymbolDisplay.FormatLiteral(id, true) + ", " + version.ToString(CultureInfo.InvariantCulture) + "),\n"
            + "        [" + string.Join(", ", types.Select(type => $"typeof({type})")) + "]);\n}\n";
    }

    private static string HintName(INamedTypeSymbol shell)
    {
        // Roslyn compares hint names ignoring case. Hash the qualified identity so case-only differences stay distinct.
        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(shell.ToDisplayString(TypeFormat)));
        return "ElsaModule." + BitConverter.ToString(digest).Replace("-", "") + ".g.cs";
    }

    private static DiagnosticDescriptor Rule(string id, string title) =>
        new(id, title, "{0}", "Elsa.CodeGeneration", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private sealed class ModuleDeclaration(string? id, Location idLocation, ImmutableArray<Diagnostic> diagnostics, string hintName, string? source)
    {
        internal string? Id { get; } = id;
        internal Location IdLocation { get; } = idLocation;
        internal ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
        internal string HintName { get; } = hintName;
        internal string? Source { get; } = source;
    }
}
