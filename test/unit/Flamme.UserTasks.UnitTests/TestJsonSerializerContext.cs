using System.Text.Json.Serialization;
using Elsa.UserTasks.Models;

namespace Elsa.UserTasks.UnitTests;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(UserTaskSummary))]
[JsonSerializable(typeof(UserTaskEventsResult))]
[JsonSerializable(typeof(UserTaskFormProjection))]
[JsonSerializable(typeof(List<UserTaskEvent>))]
[JsonSerializable(typeof(UserTaskInvitationIssueResult))]
[JsonSerializable(typeof(List<UserTaskInvitation>))]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
