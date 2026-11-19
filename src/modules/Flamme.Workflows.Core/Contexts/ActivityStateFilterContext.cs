using System.Text.Json;
using Flamme.Workflows.Models;

namespace Flamme.Workflows;

public record ActivityStateFilterContext(ActivityExecutionContext ActivityExecutionContext, InputDescriptor InputDescriptor, JsonElement Value, CancellationToken CancellationToken);