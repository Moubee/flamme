using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Flamme.Workflows.Activities.Flowchart.Attributes;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Helpers;
using Flamme.Workflows.Models;
using Humanizer;

namespace Flamme.Workflows;

/// <summary>
/// Materializes the fresh catalog objects used by producer-generated activity bindings.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedActivityDescriptorFactory
{
    /// <summary>Reads raw metadata when an activity annotation is inaccessible to the generated module.</summary>
    public static Attribute[] ReadActivityAttributes(Type activityType) =>
        Attribute.GetCustomAttributes(activityType, inherit: true)
            .Where(attribute => attribute is not (NullableAttribute or NullableContextAttribute)).ToArray();

    /// <summary>Reads property metadata whose concrete attribute type or constructor is inaccessible to the generated module.</summary>
    public static T ReadPropertyAttribute<T>([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type declaringType,
        string propertyName) where T : Attribute
    {
        var property = declaringType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
        return property.GetCustomAttribute<T>(inherit: true)!;
    }

    public static ActivityDescriptor Create(
        Type type,
        string typeName,
        string name,
        string @namespace,
        int version,
        string friendlyName,
        bool isTrigger,
        bool isContainer,
        bool isStart,
        bool isTerminal,
        Attribute[] attributes)
    {
        var activity = AttributeOfType<ActivityAttribute>(attributes);
        return new ActivityDescriptor
        {
            ClrType = type,
            TypeName = typeName,
            Name = name,
            Namespace = @namespace,
            Version = version,
            DisplayName = AttributeOfType<DisplayNameAttribute>(attributes)?.DisplayName
                ?? activity?.DisplayName ?? friendlyName.Humanize(LetterCasing.Title),
            Description = AttributeOfType<DescriptionAttribute>(attributes)?.Description ?? activity?.Description,
            Category = AttributeOfType<CategoryAttribute>(attributes)?.Category ?? activity?.Category
                ?? ActivityTypeNameHelper.GetCategoryFromNamespace(@namespace) ?? "Miscellaneous",
            Kind = isTrigger ? ActivityKind.Trigger : activity?.Kind ?? ActivityKind.Action,
            IsContainer = isContainer,
            IsStart = isStart,
            IsTerminal = isTerminal,
            IsBrowsable = AttributeOfType<BrowsableAttribute>(attributes)?.Browsable ?? true,
            Attributes = new List<Attribute>(attributes),
            CustomProperties = { ["Type"] = type }
        };
    }

    public static void AddInput(
        ActivityDescriptor descriptor,
        IPropertyDefaultValueResolver defaults,
        ActivityMemberBinding member,
        Type logicalType,
        bool isWrapped,
        InputAttribute? input,
        DescriptionAttribute? description)
    {
        var context = new ActivityPropertyDefaultValueContext(
            descriptor.ClrType, member.ClrName, member.DeclaringType, member.PropertyType);

        descriptor.Inputs.Add(new InputDescriptor(
            input?.Name ?? member.ClrName,
            logicalType,
            member.GetValue,
            member.SetValue,
            isWrapped,
            input?.DisplayName ?? member.ClrName.Humanize(LetterCasing.Title),
            description?.Description ?? input?.Description,
            input?.Category,
            input?.Order ?? 0,
            defaults.GetDefaultValue(context, input?.DefaultValueProvider, input?.DefaultValue),
            input?.DefaultSyntax,
            input?.IsReadOnly ?? false,
            input?.IsBrowsable ?? true,
            input?.IsSerializable ?? true,
            autoEvaluate: input?.AutoEvaluate ?? true,
            evaluatorType: input?.EvaluatorType)
        {
            IsSensitive = input?.CanContainSecrets ?? false,
            MemberBinding = member
        });
    }

    public static void AddOutput(
        ActivityDescriptor descriptor,
        ActivityMemberBinding member,
        Type logicalType,
        OutputAttribute? output,
        DescriptionAttribute? description,
        Attribute[] attributes)
    {
        var name = (output?.Name ?? member.ClrName).Pascalize();
        var serializable = name == ActivityOutputRegister.DefaultOutputName
            ? AttributeOfType<OutputAttribute>(attributes)?.IsSerializable
            : output?.IsSerializable;
        descriptor.Outputs.Add(new OutputDescriptor(
            name,
            output?.DisplayName ?? member.ClrName.Humanize(LetterCasing.Title),
            logicalType,
            member.GetValue,
            member.SetValue,
            description: description?.Description ?? output?.Description,
            isBrowsable: output?.IsBrowsable ?? true,
            isSerializable: serializable)
        {
            MemberBinding = member
        });
    }

    public static void AddEmbeddedPort(ActivityDescriptor descriptor, string clrName, PortAttribute? port, BrowsableAttribute? browsable)
    {
        var name = port?.Name ?? clrName;
        descriptor.Ports.Add(new Port
        {
            Name = name,
            DisplayName = port?.DisplayName ?? name,
            Type = PortType.Embedded,
            IsBrowsable = port is not null && (browsable?.Browsable ?? true)
        });
    }

    public static void AddFlowPorts(ActivityDescriptor descriptor, Attribute[] attributes)
    {
        if (AttributeOfType<FlowNodeAttribute>(attributes) is not { } flow)
            return;

        foreach (var outcome in flow.Outcomes)
            descriptor.Ports.Add(new Port { Name = outcome, DisplayName = outcome, Type = PortType.Flow });
    }

    private static T? AttributeOfType<T>(Attribute[] attributes) where T : Attribute
    {
        foreach (var attribute in attributes)
            if (attribute is T match)
                return match;

        return null;
    }
}
