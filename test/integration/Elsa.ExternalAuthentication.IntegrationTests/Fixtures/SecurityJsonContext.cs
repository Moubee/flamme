using System.Text.Json.Serialization;
using Elsa.ExternalAuthentication.IntegrationTests.Security;
using Elsa.ExternalAuthentication.Notifications;

namespace Elsa.ExternalAuthentication.IntegrationTests.Fixtures;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SensitiveDataLeakageTests.ErrorAndRedirect), TypeInfoPropertyName = "ErrorAndRedirect")]
[JsonSerializable(typeof(SensitiveDataLeakageTests.RedactedBoundary), TypeInfoPropertyName = "RedactedBoundary")]
[JsonSerializable(typeof(SensitiveDataLeakageTests.HealthSummary), TypeInfoPropertyName = "HealthSummary")]
[JsonSerializable(typeof(ExternalAuthenticationOutcomeRecorded))]
[JsonSerializable(typeof(string))]
internal partial class SecurityJsonContext : JsonSerializerContext;
