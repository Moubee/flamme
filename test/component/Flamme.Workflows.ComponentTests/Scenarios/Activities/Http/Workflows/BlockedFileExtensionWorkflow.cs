using System.Net;
using Flamme.Http;
using Flamme.Workflows.Activities.Flowchart.Activities;
using Flamme.Workflows.Activities.Flowchart.Models;
using Microsoft.AspNetCore.Http;
using Endpoint = Flamme.Workflows.Activities.Flowchart.Models.Endpoint;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Http.Workflows;

public class BlockedFileExtensionWorkflow : WorkflowBase
{
    public static readonly string DefinitionId = Guid.NewGuid().ToString();

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        
        var filesVariable = builder.WithVariable<IFormFile[]>("Files", default!);
        
        var httpEndpoint = new HttpEndpoint
        {
            Path = new("test/blocked-extensions"),
            SupportedMethods = new([HttpMethods.Post]),
            CanStartWorkflow = true,
            Files = new(filesVariable),
            BlockedFileExtensions = new([".exe", ".bat", ".sh"]),
            ExposeInvalidFileExtensionOutcome = true
        };
        
        var successResponse = new WriteHttpResponse
        {
            Content = new("File upload successful"),
            ContentType = new("text/plain"),
            StatusCode = new(HttpStatusCode.OK)
        };
        
        var errorResponse = new WriteHttpResponse
        {
            Content = new("Blocked file extension detected"),
            ContentType = new("text/plain"),
            StatusCode = new(HttpStatusCode.UnsupportedMediaType)
        };
        
        builder.Root = new Flowchart
        {
            Start = httpEndpoint,
            Activities = { httpEndpoint, successResponse, errorResponse },
            Connections = 
            {
                new Connection(new Endpoint(httpEndpoint, "Done"), new Endpoint(successResponse)),
                new Connection(new Endpoint(httpEndpoint, "Invalid file extension"), new Endpoint(errorResponse))
            }
        };
    }
}