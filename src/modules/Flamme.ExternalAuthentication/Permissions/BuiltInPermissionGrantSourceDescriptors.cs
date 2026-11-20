using System.Text.Json;
using Flamme.ExternalAuthentication.Models;

namespace Flamme.ExternalAuthentication.Permissions;

internal static class BuiltInPermissionGrantSourceDescriptors
{
    public static IReadOnlyList<SettingFieldDescriptor> Mapping(string defaultClaimType) =>
    [
        Field(
            "claimType",
            "Projected claim type",
            "The normalized projected claim whose values are matched.",
            "string",
            JsonSerializer.SerializeToElement(defaultClaimType)),
        Field(
            "mappings",
            "Claim value mappings",
            "A JSON object or array mapping explicit claim values to arrays of Flamme permission strings.",
            "json",
            JsonSerializer.SerializeToElement(Array.Empty<object>()))
    ];

    public static IReadOnlyList<SettingFieldDescriptor> PassThrough() =>
    [
        Field(
            "claimType",
            "Projected permission claim type",
            "The normalized projected claim containing candidate Flamme permission strings.",
            "string",
            JsonSerializer.SerializeToElement("permissions")),
        Field(
            "allowedPermissions",
            "Allowed permission boundary",
            "Only these exact permission strings may pass through from the projected claim.",
            "string-array",
            JsonSerializer.SerializeToElement(Array.Empty<string>()))
    ];

    private static SettingFieldDescriptor Field(string name, string displayName, string description, string valueType, JsonElement defaultValue) =>
        new(
            name,
            displayName,
            description,
            valueType,
            true,
            defaultValue,
            [],
            new(MaximumLength: 16_384),
            false,
            false,
            null,
            null,
            false);
}
