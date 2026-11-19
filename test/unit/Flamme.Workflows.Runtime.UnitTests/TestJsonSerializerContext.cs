using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.UnitTests;

[JsonSerializable(typeof(global::Elsa.Workflows.Runtime.WorkflowInterruptedPayload), TypeInfoPropertyName = "WorkflowInterruptedPayload")]
[JsonSerializable(typeof(global::Elsa.Workflows.Models.WorkflowOptions), TypeInfoPropertyName = "WorkflowOptions")]
[JsonSerializable(typeof(string), TypeInfoPropertyName = "String")]
[JsonSerializable(typeof(global::Elsa.Workflows.Runtime.UnitTests.Comparers.WorkflowTriggerEqualityComparerTests.TestPayload))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(global::System.Text.Json.JsonElement))]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
