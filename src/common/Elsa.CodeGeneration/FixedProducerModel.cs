using System.Runtime.CompilerServices;
using static Elsa.CodeGeneration.Generators.FixedProducerModel.ArgumentKind;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RegistrationCompiler;

namespace Elsa.CodeGeneration.Generators;

internal sealed class FixedProducerFeature(INamedTypeSymbol type, string bridge, INamedTypeSymbol[] strong,
    INamedTypeSymbol[] conditional, bool hasDefaultFactory)
{
    public INamedTypeSymbol Type { get; } = type;
    public string Bridge { get; } = bridge;
    public INamedTypeSymbol[] Strong { get; } = strong;
    public INamedTypeSymbol[] Conditional { get; } = conditional;
    public INamedTypeSymbol[] Introduced { get; internal set; } = [];
    public INamedTypeSymbol[] ConditionalIntroduced { get; internal set; } = [];
    public bool HasDefaultFactory { get; } = hasDefaultFactory;
    public string? ConstructorError { get; internal set; }
    public string? Error { get; internal set; }
    internal INamedTypeSymbol[] Hosted { get; set; } = [];
}

internal sealed class FixedProducerSelector
{
    public INamedTypeSymbol[] Features { get; internal set; } = [];
    public INamedTypeSymbol[] ConditionalFeatures { get; internal set; } = [];
    public string[] ConfigurationParameters { get; internal set; } = [];
    public string[] ConditionalConfigurationParameters { get; internal set; } = [];
    internal string[] VirtualCalls { get; set; } = [];
    internal string[] ConditionalVirtualCalls { get; set; } = [];
    public string? Error { get; internal set; }
    internal INamedTypeSymbol[] Hosted { get; set; } = [];
}

/// <summary>Compile-time facts, shared by the producer and the host within one compilation.</summary>
internal sealed partial class FixedProducerModel
{
    private static readonly ConditionalWeakTable<Compilation, FixedProducerModel> Models = new();
    private readonly Compilation _compilation;
    private readonly FeatureBinding _binding;
    private readonly Dictionary<INamedTypeSymbol, FixedProducerFeature> _features = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, INamedTypeSymbol> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FixedProducerSelector> _selectors = new(StringComparer.Ordinal);
    private readonly Dictionary<INamedTypeSymbol, string> _services = new(SymbolEqualityComparer.Default);
    private readonly List<FixedProducerFeature> _local = [];
    private readonly Dictionary<string, FixedProducerSelector> _exportedSelectors = new(StringComparer.Ordinal);
    private readonly Dictionary<INamedTypeSymbol, string> _localServices = new(SymbolEqualityComparer.Default);
    private readonly string _namespace;
    private readonly string _bridge;
    private readonly string? _source;
    private const string Runtime = "global::Elsa.Features.Services.";

    internal IReadOnlyList<FixedProducerFeature> Features => _features.Values.ToArray();
    internal IReadOnlyList<string> ExternAliases { get; }
    internal static FixedProducerModel Read(Compilation compilation, CancellationToken token) =>
        Models.GetValue(compilation, c => new FixedProducerModel(c, token));

    private FixedProducerModel(Compilation compilation, CancellationToken token)
    {
        _compilation = compilation;
        _binding = new FeatureBinding(compilation, compilation.GetTypeByMetadataName("Elsa.Features.Services.IFeature")!);
        _namespace = Namespace(compilation.Assembly);
        _bridge = "global::" + _namespace + ".Bindings";

        var imported = new List<(INamedTypeSymbol Bridge, IAssemblySymbol Assembly)>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (assembly.GetTypeByMetadataName(Namespace(assembly) + ".Bindings") is not {} helper || !HasCurrentSchema(helper))
                continue;
            imported.Add((helper, assembly));
            foreach (var member in helper.GetMembers().OfType<IMethodSymbol>())
            {
                var attribute = member.GetAttributes().FirstOrDefault(a => IsAttribute(a, helper, "SlotAttribute") || IsAttribute(a, helper, "ServiceAttribute"));
                if (attribute?.ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol type)
                    continue;
                var bridge = _binding.TypeName(helper) + "." + member.Name;
                Remember(type);
                if (attribute.AttributeClass!.Name == "ServiceAttribute")
                    _services[type] = bridge;
                else if (!_features.ContainsKey(type) || SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, assembly))
                {
                    var (strong, conditional) = _binding.Dependencies(type);
                    _features[type] = new(type, bridge, strong, conditional, (bool)attribute.ConstructorArguments[1].Value!);
                }
            }
        }

        var localTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var methods = new List<IMethodSymbol>();
        var closedServices = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            token.ThrowIfCancellationRequested();
            var semantic = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot(token).DescendantNodes())
            {
                if (node is TypeDeclarationSyntax { BaseList: not null } decl &&
                    semantic.GetDeclaredSymbol(decl, token) is INamedTypeSymbol { IsAbstract: false } type && _binding.IsFeature(type) && FeatureBinding.IsClosed(type))
                    localTypes.Add(type);
                if (node is TypeDeclarationSyntax typeDecl && semantic.GetDeclaredSymbol(typeDecl, token) is INamedTypeSymbol constructorOwner &&
                    _binding.IsFeature(constructorOwner))
                    methods.AddRange(constructorOwner.InstanceConstructors);
                if (node is MethodDeclarationSyntax declaration &&
                    semantic.GetDeclaredSymbol(declaration, token) is IMethodSymbol method && IsCompositionMethod(method))
                    methods.Add(method);
                if (node is AccessorDeclarationSyntax accessor && semantic.GetDeclaredSymbol(accessor, token) is
                    IMethodSymbol { MethodKind: MethodKind.PropertySet } setter && CompositionType(setter.ContainingType))
                    methods.Add(setter);
                if (node is not InvocationExpressionSyntax call || !IsRegistrationName(call) ||
                    semantic.GetOperation(call, token) is not IInvocationOperation operation)
                    continue;
                var (requested, hosted) = Registration(operation);
                if (requested is not INamedTypeSymbol closed || !FeatureBinding.IsClosed(closed)) continue;
                if (hosted && _binding.HostedServiceError(closed) is null)
                    closedServices.Add(closed);
                else if (!hosted && closed.IsGenericType && _binding.IsFeature(closed))
                    localTypes.Add(closed);
            }
        }
        var pending = new Queue<INamedTypeSymbol>(localTypes.OrderBy(TypeKey, StringComparer.Ordinal));
        while (pending.Count > 0)
        {
            var type = pending.Dequeue();
            var error = _binding.FeatureError(type, compilation.Assembly);
            var (strong, conditional) = _binding.Dependencies(type);
            var name = BridgeName("Declare", type);
            var item = new FixedProducerFeature(type, _bridge + "." + name, strong, conditional, _binding.Factory(type) != "null") { Error = error };
            _features[type] = item;
            Remember(type);
            // Unnameable symbols still give a local diagnostic, but cannot appear in generated C#.
            if (error is not null) continue;
            _local.Add(item);
            foreach (var dependency in strong.Concat(conditional))
                if (dependency.IsGenericType && FeatureBinding.IsClosed(dependency) && localTypes.Add(dependency))
                    pending.Enqueue(dependency);
        }
        foreach (var service in closedServices.OrderBy(TypeKey, StringComparer.Ordinal))
        {
            Remember(service);
            var name = BridgeName("DeclareHosted", service);
            _localServices[service] = name;
            _services[service] = _bridge + "." + name;
        }
        foreach (var (helper, assembly) in imported)
        {
            foreach (var attr in helper.GetAttributes().Where(a => IsAttribute(a, helper, "SelectorAttribute")))
            {
                var key = assembly.Identity.Name + "|" + (string)attr.ConstructorArguments[0].Value!;
                var effects = ReadEffects(attr.ConstructorArguments, 1, 6);
                effects.ConfigurationParameters = attr.ConstructorArguments[4].Values.Select(a => (string)a.Value!).ToArray();
                effects.VirtualCalls = Strings(attr, 5);
                effects.ConditionalVirtualCalls = Strings(attr, 7);
                effects.ConditionalConfigurationParameters = Strings(attr, 8);
                _selectors[key] = effects;
            }
            foreach (var method in helper.GetMembers().OfType<IMethodSymbol>())
            {
                var attr = method.GetAttributes().FirstOrDefault(a => IsAttribute(a, helper, "SlotAttribute"));
                if (attr?.ConstructorArguments[0].Value is not INamedTypeSymbol type ||
                    !_features.TryGetValue(type, out var item) || item.Bridge != _binding.TypeName(helper) + "." + method.Name) continue;
                var effects = ReadEffects(attr.ConstructorArguments, 2, 5);
                item.Introduced = effects.Features;
                item.ConditionalIntroduced = effects.ConditionalFeatures;
                item.ConstructorError = attr.ConstructorArguments[6].Value as string;
                item.Error = effects.Error;
            }
        }
        var effectsReader = new ProducerEffects(this, token);
        foreach (var method in methods.Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
        {
            var facts = effectsReader.Read(method);
            var key = MethodKey(method);
            _selectors[key] = facts;
            if (method.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.Protected or Accessibility.ProtectedAndInternal or Accessibility.ProtectedOrInternal)
                _exportedSelectors[key.Substring(key.IndexOf('|') + 1)] = facts;
        }
        foreach (var item in _local)
        {
            if (_binding.DefaultConstructor(item.Type) is { } ctor)
            {
                var construction = effectsReader.Read(ctor);
                if (construction.Error is not null || construction.Features.Length + construction.ConditionalFeatures.Length + construction.Hosted.Length
                    + construction.VirtualCalls.Length + construction.ConditionalVirtualCalls.Length > 0)
                    item.ConstructorError = $"'{ctor}' changes or may change feature composition during construction. Move feature selection to Configure and declare required dependencies."
                        + (construction.Error is null ? "" : " " + construction.Error);
            }
            var lifecycle = new[] { "Configure", "ConfigureHostedServices", "Apply" }
                .Select(name => LifecycleMethod(item.Type, name)).Where(m => m is not null)
                .SelectMany(m => effectsReader.ReadLifecycle(m!, item.Type, new HashSet<string>(StringComparer.Ordinal))).ToArray();
            item.Introduced = Types(lifecycle.SelectMany(s => s.Features));
            item.ConditionalIntroduced = Types(lifecycle.SelectMany(s => s.ConditionalFeatures));
            item.Hosted = Types(lifecycle.SelectMany(s => s.Hosted));
            item.Error = lifecycle.Select(s => s.Error).FirstOrDefault(e => e != null);
        }
        // Rendering formats alias-qualified type names. Finish it before sharing the model
        // across the producer and host generators, so their source output can run concurrently.
        _source = RenderProducer();
        ExternAliases = _binding.ExternAliases.ToArray();
    }

    internal FixedProducerSelector? GetSelector(IMethodSymbol method) =>
        _selectors.TryGetValue(MethodKey(method), out var selector) ? selector : null;
    internal string? GetHostedServiceBridge(INamedTypeSymbol type) =>
        _services.TryGetValue(type, out var bridge) ? bridge : null;

    private FixedProducerSelector ReadEffects(System.Collections.Immutable.ImmutableArray<TypedConstant> args, int start, int conditional)
    {
        var result = new FixedProducerSelector { Error = args[start + 2].Value as string };
        result.Features = ReadTypes(args[start]);
        result.Hosted = ReadTypes(args[start + 1]);
        result.ConditionalFeatures = args.Length > conditional ? ReadTypes(args[conditional]) : [];
        return result;

        INamedTypeSymbol[] ReadTypes(TypedConstant array)
        {
            var types = new List<INamedTypeSymbol>();
            foreach (var value in array.Values)
            {
                var key = (string)value.Value!;
                if (_types.TryGetValue(key, out var type)) types.Add(type);
                else result.Error ??= $"no compiled registration for '{key}'";
            }
            return types.ToArray();
        }
    }

    internal enum ArgumentKind { TypeArgument, BooleanArgument, Text, TextArray }
    private static readonly ArgumentKind[] SelectorArguments = [Text, TextArray, TextArray, Text, TextArray, TextArray, TextArray, TextArray, TextArray];
    private static readonly ArgumentKind[] SlotArguments = [TypeArgument, BooleanArgument, TextArray, TextArray, Text, TextArray, Text];
    private static readonly ArgumentKind[] ServiceArguments = [TypeArgument];

    // Metadata belongs to a NuGet producer outside the current compilation. Reject
    // incompatible or malformed exports as a unit, before decoding any slot/selector.
    private static bool HasCurrentSchema(INamedTypeSymbol bridge)
    {
        if (bridge.GetMembers("SchemaVersion").OfType<IFieldSymbol>().FirstOrDefault()?.ConstantValue is not 1) return false;
        foreach (var attr in bridge.GetAttributes().Where(a => IsAttribute(a, bridge, "SelectorAttribute")))
            if (!Matches(attr, SelectorArguments)) return false;
        foreach (var method in bridge.GetMembers().OfType<IMethodSymbol>())
        foreach (var attr in method.GetAttributes())
        {
            if (IsAttribute(attr, bridge, "SlotAttribute") && !Matches(attr, SlotArguments)) return false;
            if (IsAttribute(attr, bridge, "ServiceAttribute") && !Matches(attr, ServiceArguments)) return false;
        }
        return true;

        static bool Matches(AttributeData attr, ArgumentKind[] shape)
        {
            if (attr.ConstructorArguments.Length != shape.Length) return false;
            for (var i = 0; i < shape.Length; i++)
            {
                var arg = attr.ConstructorArguments[i];
                var valid = shape[i] switch
                {
                    TypeArgument => arg.Kind == TypedConstantKind.Type && arg.Value is INamedTypeSymbol,
                    BooleanArgument => arg.Kind == TypedConstantKind.Primitive && arg.Value is bool,
                    TextArray => arg.Kind == TypedConstantKind.Array && !arg.IsNull && arg.Values.All(v => v.Kind == TypedConstantKind.Primitive && v.Value is string),
                    _ => arg.Kind == TypedConstantKind.Primitive && (arg.IsNull || arg.Value is string)
                };
                if (!valid) return false;
            }
            return true;
        }
    }

    private static string[] Strings(AttributeData data, int ordinal) => data.ConstructorArguments.Length > ordinal
        ? data.ConstructorArguments[ordinal].Values.Select(a => (string)a.Value!).ToArray() : [];

    private void Remember(INamedTypeSymbol type) => _types[TypeKey(type)] = type;
    private static INamedTypeSymbol[] Types(IEnumerable<INamedTypeSymbol> types) =>
        types.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToArray();
    private static string TypeKey(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return TypeKey(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
        if (type is not INamedTypeSymbol named) return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var parent = named.ContainingType is {} declaring ? TypeKey(declaring) + "+" :
            named.ContainingAssembly.Identity.Name + "|" + named.ContainingNamespace.ToDisplayString() + ".";
        return parent + named.MetadataName + (named.TypeArguments.Length == 0 ? "" :
            "[" + string.Join(",", named.TypeArguments.Select(TypeKey)) + "]");
    }
    private static string MethodKey(IMethodSymbol method)
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        definition = definition.AssociatedExtensionImplementation ?? definition;
        return definition.ContainingAssembly.Identity.Name + "|" + definition.GetDocumentationCommentId();
    }

    // The readable prefix supports navigation from the host's generated plan. A stable
    // hash also distinguishes nested types and closed generic specializations with the same name.
    private static string BridgeName(string prefix, INamedTypeSymbol type)
    {
        using var hash = System.Security.Cryptography.SHA256.Create();
        var digest = hash.ComputeHash(Encoding.UTF8.GetBytes(TypeKey(type)));
        return prefix + type.Name + "_" + BitConverter.ToString(digest, 0, 8).Replace("-", "");
    }

    private static string Namespace(IAssemblySymbol assembly) => "Elsa.FixedFeatures.V1.P" +
        RegistrationSource.EscapeIdentifier(assembly.Identity.Name);
    private static bool IsAttribute(AttributeData attr, INamedTypeSymbol bridge, string name) =>
        attr.AttributeClass?.Name == name && SymbolEqualityComparer.Default.Equals(attr.AttributeClass.ContainingNamespace, bridge.ContainingNamespace);
    private static IMethodSymbol? LifecycleMethod(INamedTypeSymbol feature, string name)
    {
        var contract = feature.AllInterfaces.FirstOrDefault(i => i.ContainingAssembly.Name == "Elsa.Features" && i.ToDisplayString() == "Elsa.Features.Services.IFeature");
        if (contract?.GetMembers(name).OfType<IMethodSymbol>().SingleOrDefault() is not {} member ||
            feature.FindImplementationForInterfaceMember(member) is not IMethodSymbol implementation) return null;
        // FindImplementationForInterfaceMember can point to the original virtual declaration.
        // Follow the override chain just as an IFeature call on this concrete instance does.
        if (!implementation.IsVirtual && !implementation.IsOverride) return implementation;
        for (var type = feature; type is not null; type = type.BaseType)
            foreach (var candidate in type.GetMembers(implementation.Name).OfType<IMethodSymbol>())
                for (var ancestor = candidate; ancestor is not null; ancestor = ancestor.OverriddenMethod)
                    if (SymbolEqualityComparer.Default.Equals(ancestor.OriginalDefinition, implementation.OriginalDefinition))
                        return candidate;
        return implementation;
    }

    internal string? EmitProducer() => _source;

    private string? RenderProducer()
    {
        if (_local.Count == 0 && _localServices.Count == 0 && _exportedSelectors.Count == 0) return null;
        var body = new StringBuilder();
        foreach (var p in _exportedSelectors.OrderBy(e => e.Key, StringComparer.Ordinal))
            body.AppendLine($"[Selector({Literal(p.Key)}, {Keys(p.Value.Features)}, {Keys(p.Value.Hosted)}, {Literal(p.Value.Error)}, {StringArray(p.Value.ConfigurationParameters)}, {StringArray(p.Value.VirtualCalls)}, {Keys(p.Value.ConditionalFeatures)}, {StringArray(p.Value.ConditionalVirtualCalls)}, {StringArray(p.Value.ConditionalConfigurationParameters)})]");
        body.AppendLine("public static class Bindings").AppendLine("{").AppendLine("    public const int SchemaVersion = 1;");
        foreach (var item in _local)
        {
            foreach (var requirement in _binding.ConstructorRequirements(item.Type)) body.Append("    ").AppendLine(requirement);
            body.AppendLine($"    [Slot(typeof({_binding.TypeName(item.Type)}), {(item.HasDefaultFactory ? "true" : "false")}, {Keys(item.Introduced)}, {Keys(item.Hosted)}, {Literal(item.Error)}, {Keys(item.ConditionalIntroduced)}, {Literal(item.ConstructorError)})]");
            body.AppendLine($"    public static {Runtime}FixedFeatureSlot {item.Bridge.Substring(_bridge.Length + 1)}({Runtime}FixedFeatureModule module)")
                .AppendLine("    {");
            foreach (var hosted in item.Hosted)
                if (_services.TryGetValue(hosted, out var bridge)) body.AppendLine($"        {bridge}(module);");
            body.AppendLine($"        return {_binding.Declaration(item.Type)};").AppendLine("    }");
        }
        foreach (var service in _localServices)
            body.AppendLine($"    [Service(typeof({_binding.TypeName(service.Key)}))]")
                .AppendLine($"    public static void {service.Value}({Runtime}FixedFeatureModule module) => module.DeclareHostedService<{_binding.TypeName(service.Key)}>();");
        body.AppendLine("}");
        var source = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        foreach (var directive in _binding.ExternAliases) source.AppendLine(directive);
        source.Append("namespace ").Append(_namespace).AppendLine(";").AppendLine("""
            [global::System.AttributeUsage(global::System.AttributeTargets.Method)]
            internal sealed class SlotAttribute : global::System.Attribute
            {
                public SlotAttribute(global::System.Type type, bool hasFactory, string[] introduced, string[] hosted, string? error, string[] conditional, string? constructorError) { }
            }
            [global::System.AttributeUsage(global::System.AttributeTargets.Method)]
            internal sealed class ServiceAttribute : global::System.Attribute
            {
                public ServiceAttribute(global::System.Type type) { }
            }
            [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = true)]
            internal sealed class SelectorAttribute : global::System.Attribute
            {
                public SelectorAttribute(string method, string[] features, string[] hosted, string? error, string[] configurationParameters, string[] virtualCalls, string[] conditional, string[] conditionalVirtualCalls, string[] conditionalConfigurationParameters) { }
            }
            """).Append(body);
        return source.ToString();
    }

    private static string Keys(IEnumerable<INamedTypeSymbol> types) => StringArray(types.Select(TypeKey));
    private static string StringArray(IEnumerable<string> values) => "new string[] { " + string.Join(", ", values.Select(Literal)) + " }";
    private static string Literal(string? value) => value is null ? "null" : SymbolDisplay.FormatLiteral(value, true);
}
