using System.Text.Json.Serialization;

namespace Flamme.Dashboard.Api.UnitTests;

[JsonSerializable(typeof(global::Flamme.Dashboard.Abstractions.Models.DashboardOverview), TypeInfoPropertyName = "DashboardOverview")]
[JsonSerializable(typeof(global::Flamme.Dashboard.Abstractions.Models.DashboardNeedsAttentionResponse), TypeInfoPropertyName = "DashboardNeedsAttentionResponse")]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
