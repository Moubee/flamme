using System.Text.Json.Serialization;

namespace Flamme.Workflows.Runtime.UnitTests;

[JsonSerializable(typeof(global::Flamme.Workflows.Runtime.WorkflowInterruptedPayload), TypeInfoPropertyName = "WorkflowInterruptedPayload")]
[JsonSerializable(typeof(global::Flamme.Workflows.Models.WorkflowOptions), TypeInfoPropertyName = "WorkflowOptions")]
[JsonSerializable(typeof(string), TypeInfoPropertyName = "String")]
[JsonSerializable(typeof(global::Flamme.Workflows.Runtime.UnitTests.Comparers.WorkflowTriggerEqualityComparerTests.TestPayload))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(global::System.Text.Json.JsonElement))]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
