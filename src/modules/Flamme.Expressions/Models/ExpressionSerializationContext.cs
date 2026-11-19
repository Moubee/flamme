using System.Text.Json;

namespace Flamme.Expressions.Models;

/// <summary>
/// Defines the context for expression serialization.
/// </summary>
public record ExpressionSerializationContext(string ExpressionType, JsonElement JsonElement, JsonSerializerOptions Options, Type MemoryBlockType);