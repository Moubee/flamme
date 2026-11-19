using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Flamme.Workflows.Core.UnitTests.ObjectConversion;
using Flamme.Workflows.State;

namespace Flamme.Workflows.Core.UnitTests;

internal static class TestJsonTypeInfo
{
    // These tests exercise a configured converter, including factory selection, rather than
    // reflection-based metadata discovery for the root value.
    public static JsonTypeInfo<T> FromConverter<T>(JsonSerializerOptions options)
    {
        options.TypeInfoResolver ??= TestJsonSerializerContext.Default;
        var converter = options.Converters.First(converter => converter.CanConvert(typeof(T)));
        if (converter is JsonConverterFactory factory)
            converter = factory.CreateConverter(typeof(T), options)
                ?? throw new InvalidOperationException($"The configured factory returned no converter for {typeof(T)}.");
        return JsonMetadataServices.CreateValueInfo<T>(options, (JsonConverter<T>)converter);
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Person))]
[JsonSerializable(typeof(ExceptionState))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(global::System.Text.Json.JsonElement))]
[JsonSerializable(typeof(global::System.Dynamic.ExpandoObject))]
[JsonSerializable(typeof(IDictionary<string, object>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(HashSet<string>))]
[JsonSerializable(typeof(global::Flamme.Expressions.Models.Expression))]
[JsonSerializable(typeof(global::Flamme.Workflows.Models.Input<bool>))]
[JsonSerializable(typeof(global::Flamme.Workflows.Models.Input<string>))]
[JsonSerializable(typeof(global::Flamme.Workflows.Models.Output<string>))]
[JsonSerializable(typeof(global::Flamme.Workflows.Core.UnitTests.Serialization.Converters.SerializationTypeResolverTests.RegisteredPayload))]
[JsonSerializable(typeof(global::Flamme.Workflows.Core.UnitTests.Serialization.Converters.SerializationTypeResolverTests.UnregisteredPayload))]
[JsonSerializable(typeof(global::Flamme.Workflows.Core.UnitTests.Serialization.Converters.SerializationTypeResolverTests.AbstractPayload))]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
