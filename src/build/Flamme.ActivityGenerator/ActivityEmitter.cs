using System.Text;
using Microsoft.CodeAnalysis;
using static Flamme.ActivityGenerator.ActivityGenerator;

namespace Flamme.ActivityGenerator;

internal sealed partial class ActivityEmitter(
    Compilation compilation, INamedTypeSymbol activity, string row, bool inferred = false)
{
    private const string ActivityInterface = Workflows + "IActivity";
    private const string InputAttribute = Attributes + "InputAttribute";
    private const string OutputAttribute = Attributes + "OutputAttribute";
    private const string Input = Models + "Input";
    private const string Output = Models + "Output";

    private readonly List<string> accessors = [];
    private bool emittingProducerBridge;

    private sealed record Property(IPropertySymbol Symbol, string Getter, string Setter,
        bool Child, bool ChildCollection);

    public void Emit(StringBuilder source)
    {
        var publicProperties = GetProperties().ToArray();
        var properties = publicProperties.Where(IsBoundProperty).Select(Bind).ToArray();
        var inputs = properties.Where(property => IsAssignableTo(property.Symbol.Type, Input) ||
            FindAttribute(property.Symbol, InputAttribute) is not null).GroupBy(property => property.Symbol.Name, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        var outputs = properties.Where(property => IsAssignableTo(property.Symbol.Type, Output)).ToArray();
        var ns = ActivityNamespace(activity);
        var name = WireName(activity);
        var fullName = ns is null ? name : ns + "." + name;
        var version = Constant(ActivityArgument(activity, "Version", "version") ?? 1);
        var identity = EmitRuntimeIdentity(source);
        var defaultsFactory = EmitServiceProviderActivation(source);
        EmitCallbacks(source, "Bookmark", "ActivityExecutionContext", "ExecuteActivityDelegate");
        EmitCallbacks(source, "Completion", "ActivityCompletedContext", "ActivityCompletionCallback");

        for (var i = 0; i < inputs.Length; i++)
            EmitMember(source, inputs[i], "Input" + i, true);
        for (var i = 0; i < outputs.Length; i++)
            EmitMember(source, outputs[i], "Output" + i, false);

        EmitPorts(source, properties);
        EmitDescriptor(source, inputs, outputs, properties, ns, name, fullName, version, identity);

        var propertyNames = publicProperties.Where(property => !property.IsStatic).Select(property => Quote(property.Name)).Distinct();
        source.AppendLine($"    internal static readonly GeneratedActivityBinding {row} = Create{row}();");
        source.AppendLine($"    private static GeneratedActivityBinding Create{row}() => new()");
        source.AppendLine("    {");
        source.AppendLine($"        ActivityType = {TypeOf(activity)},");
        source.AppendLine($"        Namespace = {(identity is null ? (ns is null ? "null" : Quote(ns)) : $"{identity}.Namespace")},");
        source.AppendLine($"        Name = {(identity is null ? Quote(name) : $"{identity}.Name")},");
        source.AppendLine($"        TypeName = {(identity is null ? Quote(fullName) : $"{identity}.TypeName")},");
        source.AppendLine($"        Version = {(identity is null ? version : $"{identity}.Version")},");
        source.AppendLine($"        IsInferred = {Boolean(inferred)},");
        source.AppendLine($"        CreateDescriptor = Describe{row},");
        if (defaultsFactory is not null)
            source.AppendLine($"        CreateDefaultValueProvider = {defaultsFactory},");
        source.AppendLine($"        Inputs = Array.AsReadOnly<ActivityMemberBinding>([{MemberNames("Input", inputs.Length)}]),");
        source.AppendLine($"        Outputs = Array.AsReadOnly<ActivityMemberBinding>([{MemberNames("Output", outputs.Length)}]),");
        source.AppendLine($"        PublicPropertyNamesFactory = Create{row}PublicPropertyNames,");
        source.AppendLine($"        GetActivityPorts = Get{row}Ports,");
        source.AppendLine($"        BindBookmarkCallback = Bind{row}Bookmark,");
        source.AppendLine($"        BindCompletionCallback = Bind{row}Completion,");
        source.AppendLine($"        GetOutcomeName = Get{row}OutcomeName,");
        source.AppendLine("    };");

        source.AppendLine($"    private static string[] Create{row}PublicPropertyNames() => [{string.Join(", ", propertyNames)}];");
        source.AppendLine($"    private static string Get{row}OutcomeName(string name) => name switch");
        source.AppendLine("    {");
        foreach (var property in properties.Where(property => property.Child).GroupBy(property => property.Symbol.Name).Select(group => group.First()))
        {
            var outcome = FindAttribute(property.Symbol, Attributes + "PortAttribute");
            source.AppendLine($"        {Quote(property.Symbol.Name)} => {PortOutcomeName(outcome)} ?? name,");
        }
        source.AppendLine("        _ => throw new InvalidOperationException($\"Activity does not have a child port named '{name}'.\"),");
        source.AppendLine("    };");
        foreach (var accessor in accessors)
            source.AppendLine(accessor);
    }

    private static bool IsBoundProperty(IPropertySymbol property) =>
        IsAssignableTo(property.Type, Input) || IsAssignableTo(property.Type, Output) ||
        FindAttribute(property, InputAttribute) is not null ||
        !property.IsStatic && (IsAssignableTo(property.Type, ActivityInterface) || IsActivityCollection(property.Type));

    private string MemberNames(string kind, int count) => string.Join(", ", Enumerable.Range(0, count).Select(index => row + kind + index));

    private IEnumerable<IPropertySymbol> GetProperties()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var owner in Hierarchy(activity))
        foreach (var property in owner.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.DeclaredAccessibility != Accessibility.Public || property.IsStatic && !Same(owner, activity))
                continue;
            var key = property.MetadataName + ":" + TypeName(property.Type) + ":" + property.IsStatic +
                "(" + string.Join(",", property.Parameters.Select(parameter => TypeName(parameter.Type))) + ")";
            if (seen.Add(key))
                yield return property;
        }
    }

    private Property Bind(IPropertySymbol property)
    {
        if (property.IsIndexer || property.RefKind != RefKind.None || property.Type.IsRefLikeType ||
            property.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer ||
            !compilation.IsSymbolAccessibleWithin(property.Type, compilation.Assembly))
            throw new BindingException("property " + property.Name, "Use an accessible property type with an ordinary getter and setter.",
                SourceLocations(property));

        var name = Quote(property.Name);
        var owner = TypeName(property.ContainingType);
        var instance = property.IsStatic ? owner : InstanceTarget(property.ContainingType, "a");
        var member = instance + "." + Identifier(property.Name);
        var value = property.Type.IsValueType ? $"(object?)v is null ? default : ({TypeName(property.Type)})v" : $"({TypeName(property.Type)})v!";
        var read = property.GetMethod is not { } getter ? null : Accessible(getter)
            ? member
            : CallAccessor(getter, instance, []);
        var write = property.SetMethod is not { } setter ? null : Accessible(setter) && !setter.IsInitOnly
            ? $"{member} = {value}"
            : CallAccessor(setter, instance, [value]);

        // Reflection wrapped exceptions thrown *inside* public or nonpublic accessors.
        if (!Same(property.ContainingAssembly, compilation.Assembly) && FindProducerBridge(property.ContainingType) is { } bridge)
        {
            var ownerGet = "Get_" + property.Name;
            var ownerSet = "Set_" + property.Name;
            if (bridge.GetMembers(ownerGet).Length != 0)
                read = $"({TypeName(property.Type)}){TypeName(bridge)}.{ownerGet}((IActivity)a)!";
            if (bridge.GetMembers(ownerSet).Length != 0)
                write = $"{TypeName(bridge)}.{ownerSet}((IActivity)a, v)";
        }

        var runtimeGetter = read is null ? $"static a => throw new ArgumentException(\"Property getter not found. \" + {name})"
            : $"static a => {{ try {{ return {read}; }} catch (Exception e) {{ throw new TargetInvocationException(e); }} }}";
        var valueType = NormalizeNullable(property.Type);
        var invalidValue = $"if (v is not null && v is not {TypeName(valueType)}) " +
            $"throw new ArgumentException($\"Object of type '{{v.GetType()}}' cannot be converted to type '{{{TypeOf(property.Type)}}}'.\");";
        var runtimeSetter = write is null ? $"static (a, v) => throw new ArgumentException(\"Property setter not found. \" + {name})"
            : $"static (a, v) => {{ {invalidValue} try {{ {write}; }} catch (Exception e) {{ throw new TargetInvocationException(e); }} }}";

        return new(property, runtimeGetter, runtimeSetter, !property.IsStatic && IsAssignableTo(property.Type, ActivityInterface),
            !property.IsStatic && IsActivityCollection(property.Type));
    }

    private static bool IsActivityCollection(ITypeSymbol type) => type.AllInterfaces.Prepend(type)
        .OfType<INamedTypeSymbol>().Any(iface => Is(iface, "System.Collections.Generic.IEnumerable`1") &&
            IsAssignableTo(iface.TypeArguments[0], ActivityInterface));

    private void EmitMember(StringBuilder source, Property property, string id, bool isInput)
    {
        var symbol = property.Symbol;
        source.AppendLine($"    private static readonly ActivityMemberBinding {row}{id} = Create{row}{id}();");
        source.AppendLine($"    private static ActivityMemberBinding Create{row}{id}() =>");
        source.Append($"        new({Quote(symbol.Name)}, {TypeOf(symbol.ContainingType)}, {TypeOf(symbol.Type)}, {property.Getter}, {property.Setter})");
        if (isInput && IsAssignableTo(symbol.Type, Input))
        {
            var wrapped = GetWrapper(symbol.Type, Input);
            if (wrapped is null || !Same(wrapped, symbol.Type))
                throw new BindingException("omitted input " + symbol.Name, "Declare the property as Input<T> to supply a typed default factory.",
                    SourceLocations(symbol));
            // Omitted defaults historically use the normalized descriptor type. Preserve
            // the incompatible-wrapper error for an omitted non-null nullable value default.
            var valueType = NormalizeNullable(wrapped.TypeArguments[0]);
            source.Append($" {{ CreateInput = static e => new global::{Input}<{TypeName(valueType)}>(e) }}");
        }
        source.AppendLine(";");
    }

    private void EmitPorts(StringBuilder source, Property[] properties)
    {
        source.AppendLine($"    private static IEnumerable<ActivityPort> Get{row}Ports(IActivity activity)");
        source.AppendLine("    {");
        source.AppendLine("        var ports = new List<ActivityPort>();");
        foreach (var property in properties.Where(property => property.Child || property.ChildCollection))
        {
            var kind = property.ChildCollection ? "Activities" : "Activity";
            var type = property.ChildCollection ? "IEnumerable<IActivity>" : "IActivity";
            source.AppendLine($"        if (((Func<IActivity, object?>)({property.Getter}))(activity) is {type} port{portsIndex++})");
            source.AppendLine($"            ports.Add(ActivityPort.From{kind}(port{portsIndex - 1}, {Quote(property.Symbol.Name)}));");
        }
        source.AppendLine("        return ports;");
        source.AppendLine("    }");
    }

    private int portsIndex;

    private void EmitDescriptor(StringBuilder source, Property[] inputs, Property[] outputs, Property[] properties, string? ns,
        string name, string fullName, string version, string? identity)
    {
        source.AppendLine($"    private static ActivityDescriptor Describe{row}(IPropertyDefaultValueResolver defaults)");
        source.AppendLine("    {");
        var attributes = string.Join(", ", GetInheritedAttributes().Select(NewAttribute));
        source.AppendLine($"        Attribute[] attributes = [{attributes}];");
        var descriptorIdentity = identity is null
            ? $"{Quote(fullName)}, {Quote(name)}, {Quote(ns ?? string.Empty)}, {version}"
            : $"{identity}.TypeName, {identity}.Name, {identity}.Namespace ?? string.Empty, {identity}.Version";
        source.AppendLine($"        var descriptor = GeneratedActivityDescriptorFactory.Create({TypeOf(activity)}, {descriptorIdentity}, " +
            $"{Quote(FriendlyName(activity))}, " +
            $"{Boolean(IsAssignableTo(activity, Workflows + "ITrigger"))}, {Boolean(IsAssignableTo(activity, Workflows + "Activities.Container"))}, " +
            $"{Boolean(IsAssignableTo(activity, Workflows + "IStartNode"))}, {Boolean(IsAssignableTo(activity, Workflows + "ITerminalNode"))}, attributes);");

        for (var i = 0; i < inputs.Length; i++)
            EmitInput(source, inputs[i].Symbol, i);
        var outputNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < outputs.Length; i++)
            if (outputNames.Add(outputs[i].Symbol.Name))
                EmitOutput(source, outputs[i].Symbol, i);
        foreach (var property in properties.Where(property => property.Child))
        {
            var symbol = property.Symbol;
            var port = AttributeExpression(symbol, Attributes + "PortAttribute");
            var browsable = port == "null" ? "null" : AttributeExpression(symbol, "System.ComponentModel.BrowsableAttribute");
            source.AppendLine($"        GeneratedActivityDescriptorFactory.AddEmbeddedPort(descriptor, {Quote(symbol.Name)}, {port}, {browsable});");
        }

        source.AppendLine("        GeneratedActivityDescriptorFactory.AddFlowPorts(descriptor, attributes);");
        source.AppendLine("        return descriptor;");
        source.AppendLine("    }");
    }

    private void EmitInput(StringBuilder source, IPropertySymbol property, int index)
    {
        var wrapped = GetWrapper(property.Type, Input);
        var propertyType = NormalizeNullable(wrapped?.TypeArguments[0] ?? property.Type);
        var input = AttributeExpression(property, InputAttribute);
        var description = AttributeExpression(property, "System.ComponentModel.DescriptionAttribute");
        source.AppendLine($"        GeneratedActivityDescriptorFactory.AddInput(descriptor, defaults, {row}Input{index}, " +
            $"{TypeOf(propertyType)}, {Boolean(wrapped is not null)}, {input}, {description});");
    }

    private void EmitOutput(StringBuilder source, IPropertySymbol property, int index)
    {
        var wrapper = GetWrapper(property.Type, Output);
        var propertyType = wrapper is null ? "typeof(object)" : TypeOf(wrapper.TypeArguments[0]);
        var output = AttributeExpression(property, OutputAttribute);
        var description = AttributeExpression(property, "System.ComponentModel.DescriptionAttribute");
        source.AppendLine($"        GeneratedActivityDescriptorFactory.AddOutput(descriptor, {row}Output{index}, " +
            $"{propertyType}, {output}, {description}, attributes);");
    }

    private string AttributeExpression(ISymbol member, string attributeName) =>
        FindAttribute(member, attributeName) is { } attribute ? NewAttribute(attribute) : "null";

    private static readonly (string Kind, string Context, string Delegate)[] CallbackKinds =
    [
        ("Bookmark", "ActivityExecutionContext", "ExecuteActivityDelegate"),
        ("Completion", "ActivityCompletedContext", "ActivityCompletionCallback")
    ];

    public void EmitProducerCallbacks(StringBuilder source)
    {
        emittingProducerBridge = true;
        var declarations = CallbackLookupMethods(activity).ToArray();
        var callbacks = CallbackKinds.SelectMany(kind => declarations.Where(method =>
                SupportsCallback(method, kind.Context) && method.DeclaredAccessibility != Accessibility.Public)
            .Select(method => (kind.Kind, kind.Delegate, Method: method))).ToArray();
        var lookupMetadata = CallbackLookupMetadata(declarations);
        var memberAccessors = activity.GetMembers().OfType<IPropertySymbol>()
            .Where(property => property.DeclaredAccessibility == Accessibility.Public && !property.IsIndexer && !property.IsStatic && IsBoundProperty(property))
            .SelectMany(property => new[] { (Method: property.GetMethod, Prefix: "Get_"), (Method: property.SetMethod, Prefix: "Set_") })
            .Where(accessor => accessor.Method is { DeclaredAccessibility: not Accessibility.Public } ||
                               accessor.Method is { IsInitOnly: true }).ToArray();
        if (callbacks.Length == 0 && memberAccessors.Length == 0 && lookupMetadata.Length == 0)
            return;

        var typeParameters = ContainingTypes(activity).SelectMany(owner => owner.TypeParameters).ToArray();
        var typeArguments = typeParameters.Length == 0 ? "" : $"<{string.Join(", ", typeParameters.Select(p => Identifier(p.Name)))}>";
        source.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        source.AppendLine($"public static class {row}{typeArguments} {string.Join(" ", typeParameters.Select(Constraints))}");
        source.AppendLine("{");
        for (var index = 0; index < lookupMetadata.Length; index++)
            source.AppendLine($"    public const string {CallbackLookupPrefix}{index} = {Quote(lookupMetadata[index])};");
        // A name can accept more than one callback signature through contravariance.
        // Keep one exported entry per kind and name and use the selected CLR signature to invoke it.
        foreach (var group in callbacks.GroupBy(callback => (callback.Kind, callback.Delegate, callback.Method.Name)))
        {
            var (kind, delegateType, name) = group.Key;
            source.AppendLine($"    public static {delegateType} {kind}_{name}(IActivity activity, string signature) => signature switch");
            source.AppendLine("    {");
            foreach (var callback in group)
                source.AppendLine($"        {Quote(CallbackSignature(callback.Method))} => {BindCallback(callback.Method)},");
            source.AppendLine("        _ => throw new ArgumentException(\"Unknown callback overload signature.\", nameof(signature)),");
            source.AppendLine("    };");
        }
        foreach (var (method, prefix) in memberAccessors)
        {
            var property = (IPropertySymbol)method!.AssociatedSymbol!;
            if (property.Type.IsRefLikeType || property.RefKind != RefKind.None || property.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
                throw new BindingException("property " + property.Name, "Use an ordinary property type for a callable inherited accessor.",
                    SourceLocations(property));
            var name = prefix + property.Name;
            var target = InstanceTarget(activity, "activity");
            if (method.MethodKind == MethodKind.PropertyGet)
                source.AppendLine($"    public static object? {name}(IActivity activity) => {CallAccessor(method, target, [])};");
            else
            {
                var value = $"value is null ? default : ({TypeName(property.Type)})value";
                source.AppendLine($"    public static void {name}(IActivity activity, object? value) => {CallAccessor(method, target, [value])};");
            }
        }
        foreach (var accessor in accessors)
            source.AppendLine(accessor);
        source.AppendLine("}");
    }

    private void EmitCallbacks(StringBuilder source, string kind, string context, string delegateType)
    {
        source.AppendLine($"    private static {delegateType}? Bind{row}{kind}(IActivity activity, string name) => name switch");
        source.AppendLine("    {");
        foreach (var group in CallbackCandidates(kind, context).GroupBy(candidate => candidate.Name, StringComparer.Ordinal))
        {
            var candidates = group.ToArray();
            // Reflection looked within each type's name scope before descending to the
            // next base. Delegate compatibility was checked only after choosing a method.
            var scope = candidates.Any(candidate => candidate.Depth == 0 || !candidate.IsPrivate)
                ? 0 : candidates.Min(candidate => candidate.Depth);
            var visible = candidates.Where(candidate => candidate.Depth == scope || !candidate.IsPrivate).ToArray();
            var firstDepth = visible[0].Depth;
            var ambiguous = visible.Any(candidate => candidate.Signature != visible[0].Signature) ||
                            visible.Count(candidate => candidate.Depth == firstDepth) > 1;
            if (!ambiguous && !candidates.Any(candidate => candidate.SupportsCallback))
                continue;
            var selected = visible[0];
            var binding = ambiguous
                ? $"throw new AmbiguousMatchException({Quote($"Ambiguous callback '{selected.Name}' on activity '{TypeName(activity)}'.")})"
                : selected.SupportsCallback
                  ? selected.Bridge ?? BindCallback(selected.Method!)
                  : "throw new ArgumentException(\"Cannot bind to the target method because its signature is not compatible.\")";
            source.AppendLine($"        {Quote(group.Key)} => {binding},");
        }
        source.AppendLine("        _ => null,");
        source.AppendLine("    };");
    }

    private INamedTypeSymbol? FindProducerBridge(INamedTypeSymbol owner)
    {
        var arguments = ContainingTypes(owner).SelectMany(type => type.TypeArguments).ToArray();
        var suffix = arguments.Length == 0 ? "" : "`" + arguments.Length;
        var bridge = compilation.GetTypeByMetadataName(Workflows + "Generated." + BridgeName(owner) + suffix);
        return bridge is null || arguments.Length == 0 ? bridge : bridge.Construct(arguments);
    }

    private string BindCallback(IMethodSymbol method)
    {
        var ownerName = TypeName(method.ContainingType);
        var target = method.IsStatic ? ownerName : InstanceTarget(method.ContainingType, "activity");
        if (!Accessible(method) || method.MethodKind != MethodKind.Ordinary)
            return $"context => {CallAccessor(method, target, ["context"])}";

        var member = target + "." + Identifier(method.Name);
        return UsesByReferenceReceiver(method) ? $"context => {member}(context)" : member;
    }

    private bool Accessible(ISymbol symbol) => compilation.IsSymbolAccessibleWithin(symbol, compilation.Assembly);

    private string CallAccessor(IMethodSymbol method, string instance, string[] arguments)
    {
        var owner = method.ContainingType;
        var definition = method.OriginalDefinition;
        var name = $"{row}Access{accessors.Count}";
        var methodKind = method.MethodKind == MethodKind.Constructor ? "Constructor" : method.IsStatic ? "StaticMethod" : "Method";
        var attribute = method.MethodKind == MethodKind.Constructor ? "" : $", Name = {Quote(method.MetadataName)}";
        var returnType = method.MethodKind == MethodKind.Constructor ? owner.OriginalDefinition : definition.ReturnType;
        var parameters = definition.Parameters.Select(parameter => $"{TypeName(parameter.Type)} {Identifier(parameter.Name)}");
        if (method.MethodKind != MethodKind.Constructor)
            parameters = parameters.Prepend((UsesByReferenceReceiver(method) ? "ref " : "") + TypeName(owner.OriginalDefinition) + " owner");
        var declaration = $"        [UnsafeAccessor(UnsafeAccessorKind.{methodKind}{attribute})]\n" +
            $"        internal static extern {TypeName(returnType)} {name}({string.Join(", ", parameters)});";
        if (method.MethodKind != MethodKind.Constructor)
            arguments = arguments.Prepend(AccessorReceiverArgument(method, instance)).ToArray();

        var owners = ContainingTypes(owner).ToArray();
        var typeParameters = owners.SelectMany(type => type.OriginalDefinition.TypeParameters).ToArray();
        if (typeParameters.Length == 0 || emittingProducerBridge)
        {
            accessors.Add(declaration);
            return $"{name}({string.Join(", ", arguments)})";
        }

        // UnsafeAccessor requires generic parameters in their definition slots, even for a
        // row representing a closed type. This nested class mirrors the declaring constraints.
        if (typeParameters.Select(parameter => parameter.Name).Distinct().Count() != typeParameters.Length)
            throw new BindingException(method.MetadataName, "Use distinct generic parameter names in the declaring type and its enclosing types.");
        var bridge = name + "Bridge";
        var bridgeParameters = string.Join(", ", typeParameters.Select(parameter => Identifier(parameter.Name)));
        var constraints = string.Join(" ", typeParameters.Select(Constraints));
        accessors.Add($"        private static class {bridge}<{bridgeParameters}> {constraints}\n        {{\n{declaration}\n        }}");
        var typeArguments = string.Join(", ", owners.SelectMany(type => type.TypeArguments).Select(TypeName));
        return $"{bridge}<{typeArguments}>.{name}({string.Join(", ", arguments)})";
    }

    private static string Constraints(ITypeParameterSymbol parameter)
    {
        var constraints = new List<string>();
        if (parameter.HasUnmanagedTypeConstraint)
            constraints.Add("unmanaged");
        else if (parameter.HasValueTypeConstraint)
            constraints.Add("struct");
        else if (parameter.HasReferenceTypeConstraint)
            constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
        else if (parameter.HasNotNullConstraint)
            constraints.Add("notnull");
        constraints.AddRange(parameter.ConstraintTypes.Select(TypeName));
        if (parameter.HasConstructorConstraint && !parameter.HasValueTypeConstraint)
            constraints.Add("new()");
        if (parameter.AllowsRefLikeType)
            constraints.Add("allows ref struct");
        return constraints.Count == 0 ? "" : $"where {Identifier(parameter.Name)} : {string.Join(", ", constraints)}";
    }

    private static ITypeSymbol NormalizeNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? nullable.TypeArguments[0]
            : type;

    private static INamedTypeSymbol? GetWrapper(ITypeSymbol type, string name) => type is INamedTypeSymbol named
        ? Hierarchy(named).FirstOrDefault(candidate => Is(candidate, name + "`1")) : null;

    private static bool Same(ISymbol? first, ISymbol? second) => SymbolEqualityComparer.Default.Equals(first, second);

    private static IEnumerable<INamedTypeSymbol> ContainingTypes(INamedTypeSymbol type)
    {
        if (type.ContainingType is { } container)
            foreach (var ancestor in ContainingTypes(container))
                yield return ancestor;
        yield return type;
    }

    private static string? ActivityNamespace(INamedTypeSymbol type) =>
        ActivityArgument(type, "Namespace", "namespace") as string ??
        (type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString());

    private static string WireName(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return WireName(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
        if (type is not INamedTypeSymbol named)
            return type is IDynamicTypeSymbol ? nameof(Object) : type.Name;
        var name = ActivityArgument(named, "Type", "type") as string ?? named.Name;
        var args = ContainingTypes(named).SelectMany(owner => owner.TypeArguments).ToArray();
        return args.Length == 0 ? name : $"{name}<{string.Join(",", args.Select(WireName))}>";
    }

    private static string FriendlyName(INamedTypeSymbol type)
    {
        var args = ContainingTypes(type).SelectMany(owner => owner.TypeArguments).ToArray();
        return args.Length == 0 ? type.Name : $"{type.Name}<{string.Join(", ", args.Select(argument => argument is INamedTypeSymbol named ? named.MetadataName : WireName(argument)))}>";
    }

    private static object? ActivityArgument(INamedTypeSymbol type, string property, string parameter)
    {
        var attribute = EffectiveActivityAttribute(type);
        if (attribute is null || IsDerivedActivityAttribute(attribute))
            return null;
        foreach (var pair in attribute.NamedArguments)
            if (pair.Key == property)
                return pair.Value.Value;
        var parameters = attribute.AttributeConstructor?.Parameters ?? [];
        for (var index = 0; index < parameters.Length; index++)
            if (parameters[index].Name == parameter)
                return attribute.ConstructorArguments[index].Value;
        return null;
    }
}
