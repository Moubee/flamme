using System.Text.Json.Serialization;

namespace Elsa.Dashboard.Api.UnitTests;

[JsonSerializable(typeof(global::Elsa.Dashboard.Abstractions.Models.DashboardOverview), TypeInfoPropertyName = "DashboardOverview")]
[JsonSerializable(typeof(global::Elsa.Dashboard.Abstractions.Models.DashboardNeedsAttentionResponse), TypeInfoPropertyName = "DashboardNeedsAttentionResponse")]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
