using Flamme.Workflows.Runtime.Requests;
using Flamme.Workflows.Runtime.Responses;

namespace Flamme.Workflows.IntegrationTests.Scenarios.WorkflowDispatchNotifications;

public class Spy
{
    public bool WorkflowDefinitionDispatchingWasCalled { get; set; }
    public bool WorkflowDefinitionDispatchedWasCalled { get; set; }
    public bool WorkflowInstanceDispatchingWasCalled { get; set; }
    public bool WorkflowInstanceDispatchedWasCalled { get; set; }
    
    public DispatchWorkflowDefinitionRequest? CapturedDefinitionRequest { get; set; }
    public DispatchWorkflowInstanceRequest? CapturedInstanceRequest { get; set; }
    public DispatchWorkflowResponse? CapturedResponse { get; set; }

}
