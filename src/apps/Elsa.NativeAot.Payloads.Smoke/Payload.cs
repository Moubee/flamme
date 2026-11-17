using System.Text.Json.Serialization;

public sealed class Payload
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public int? Optional { get; set; }
    public Child Child { get; set; } = new();
}

public sealed class Child
{
    public string Label { get; set; } = "";
}

[JsonSerializable(typeof(Payload))]
internal partial class PayloadJsonContext : JsonSerializerContext;
