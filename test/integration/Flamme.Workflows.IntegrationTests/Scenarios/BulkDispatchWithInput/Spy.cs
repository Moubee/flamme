using Flamme.Workflows.Runtime.Requests;

namespace Flamme.Workflows.IntegrationTests.Scenarios.BulkDispatchWithInput;

public class Spy
{
    public List<IDictionary<string, object>?> CapturedInputReferences { get; } = [];
    public List<IDictionary<string, object>?> CapturedInputSnapshots { get; } = [];

    public void CaptureDispatch(DispatchWorkflowDefinitionRequest request)
    {
        CapturedInputReferences.Add(request.Input);
        CapturedInputSnapshots.Add(request.Input != null 
            ? new Dictionary<string, object>(request.Input) 
            : null);
    }
}
