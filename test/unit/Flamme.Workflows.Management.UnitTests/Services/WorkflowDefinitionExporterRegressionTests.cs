using System.IO.Compression;
using System.Text.Json;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Management.Mappers;
using Elsa.Workflows.Management.Services;
using Elsa.Workflows.Models;
using Elsa.Workflows.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;
using Elsa.Common.Serialization;

namespace Elsa.Workflows.Management.UnitTests.Services;

public class WorkflowDefinitionExporterRegressionTests
{
    private readonly IWorkflowDefinitionStore _store = IWorkflowDefinitionStore.Mock().Object;
    private readonly IApiSerializer _serializer = IApiSerializer.Mock().Object;
    private readonly IWorkflowDefinitionService _workflowDefinitionService = IWorkflowDefinitionService.Mock().Object;
    private readonly IWorkflowReferenceGraphBuilder _workflowReferenceGraphBuilder = IWorkflowReferenceGraphBuilder.Mock().Object;

    [Test]
    public async Task ExportManyAsync_WithSlashInWorkflowName_CreatesFlatZipEntry()
    {
        var definition = new WorkflowDefinition
        {
            Id = "slash-name-workflow-v1",
            DefinitionId = "slash-name-workflow",
            Name = "folder/child",
            CreatedAt = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Version = 1,
            IsLatest = true,
            IsPublished = true,
            MaterializerName = "Json"
        };
        Mock.Get(_serializer).GetOptions().Returns(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Mock.Get(_store).FindManyAsync(Arg.Any<WorkflowDefinitionFilter>(), Arg.Any<CancellationToken>())
                    .Returns(() => Task.FromResult<IEnumerable<WorkflowDefinition>>([definition]));
        Mock.Get(_workflowDefinitionService).MaterializeWorkflowAsync(definition, Arg.Any<CancellationToken>())
                    .Returns(CreateWorkflowGraph());

        var sut = CreateExporter();
        var result = await Assert.That(await sut.ExportManyAsync([definition.Id])).IsNotNull();
        await Assert.That(result.FileName).IsEqualTo("workflow-definitions.zip");

        using var zipStream = new MemoryStream(result.Data);
        await using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entry = await Assert.That(zipArchive.Entries).HasSingleItem();

        await Assert.That(entry.FullName).IsEqualTo(entry.Name);
        await Assert.That(entry.FullName).DoesNotContain('/');
        await Assert.That(entry.FullName).DoesNotContain('\\');
        await Assert.That(entry.FullName).IsEqualTo("workflow-definition-folder-child-slash-name-workflow-slash-name-workflow-v1.json");
    }

    private WorkflowDefinitionExporter CreateExporter()
    {
        var activitySerializer = IActivitySerializer.Mock().Object;
        var scopeFactory = IServiceScopeFactory.Mock().Object;
        var workflowJsonTypeRegistry = SerializationTypeRegistry.CreateDefault();
        var variableDefinitionMapper = new VariableDefinitionMapper(workflowJsonTypeRegistry, scopeFactory, NullLogger<VariableDefinitionMapper>.Instance);
        var workflowDefinitionMapper = new WorkflowDefinitionMapper(activitySerializer, _workflowDefinitionService, variableDefinitionMapper);
        var fileNameSanitizer = new DefaultFileNameSanitizer();

        return new(_store, _serializer, workflowDefinitionMapper, _workflowReferenceGraphBuilder, fileNameSanitizer);
    }

    private static WorkflowGraph CreateWorkflowGraph()
    {
        var root = new Sequence { Id = "root" };
        var rootNode = new ActivityNode(root, "Root");
        var workflow = new Workflow(root);

        return new(workflow, rootNode, [rootNode]);
    }
}
