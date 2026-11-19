using System.Security.Claims;
using Flamme.Expressions.Contracts;
using Flamme.UnitTests.Shared;
using Flamme.Workflows.Api.Security;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ImportEndpoint = Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Import.Import;

namespace Flamme.Workflows.Api.UnitTests.Endpoints.WorkflowDefinitions;

[NotInParallel(nameof(FastEndpointsCollection))]
public class WorkflowDefinitionImportTests
{
    [Test]
    public async Task RevisionConflictReturnsBadRequestBeforeMaterializingRejectedGraph()
    {
        var importer = IWorkflowDefinitionImporter.Mock().Object;
        var linker = IWorkflowDefinitionLinker.Mock().Object;
        var auth = IAuthorizationService.Mock().Object;
        Mock.Get(auth).AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>())
            .Returns(AuthorizationResult.Success());
        Mock.Get(importer).ImportAsync(Arg.Any<SaveWorkflowDefinitionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ImportWorkflowResult(false, new WorkflowDefinition(), [new("Revision already exists.")]));
        var context = new DefaultHttpContext();
        var endpoint = Factory.Create<ImportEndpoint>(context, IWorkflowDefinitionStore.Mock().Object,
            importer, linker, auth, new WorkflowDefinitionScriptAuthorizationService(IActivityVisitor.Mock().Object, IExpressionDescriptorRegistry.Mock().Object));

        await endpoint.HandleAsync(new WorkflowDefinitionModel { Id = "existing" }, CancellationToken.None);

        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        Mock.Get(linker).MapAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }
}
