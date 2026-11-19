using Flamme.Api.Client.Resources.WorkflowInstances.Models;
using Flamme.Api.Client.Shared.Models;

namespace Flamme.Api.Client.Resources.Tests;

public class TestActivityResponse
{
    public IDictionary<string, object?> ActivityState { get; set; } = new Dictionary<string, object?>();
    public IDictionary<string, object>? Payload { get; set; }
    public IDictionary<string, object?>? Outputs { get; set; }
    public ExceptionState? Exception { get; set; }
    public ActivityStatus Status { get; set; }
}