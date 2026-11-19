using Flamme.Common.Models;
using Flamme.Common.Serialization;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Mappers;
using Flamme.Workflows.Management.Materializers;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Management.Services;
using Flamme.Workflows.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;

namespace Flamme.Workflows.Management.UnitTests.Services;

public class WorkflowDefinitionImporterOriginalSourceTests
{
    private readonly IActivitySerializer _serializer = IActivitySerializer.Mock().Object;
    private readonly IWorkflowDefinitionPublisher _publisher = IWorkflowDefinitionPublisher.Mock().Object;
    private readonly WorkflowDefinitionImporter _importer;
    private readonly WorkflowDefinitionMapper _mapper;

    public WorkflowDefinitionImporterOriginalSourceTests()
    {
        var scopeFactory = IServiceScopeFactory.Mock().Object;
        var variableDefinitionMapper = new VariableDefinitionMapper(
            SerializationTypeRegistry.CreateDefault(),
            scopeFactory,
            NullLogger<VariableDefinitionMapper>.Instance);
        _importer = new(_serializer, _publisher, variableDefinitionMapper);
        _mapper = new(_serializer, IWorkflowDefinitionService.Mock().Object, variableDefinitionMapper);
    }

    [Test]
    public async Task ImportAsync_WhenFileImportedDefinitionIsEdited_ClearsOriginalSourceAndMapUsesStringData()
    {
        var fileImportedDraft = new WorkflowDefinition
        {
            Id = "version-1",
            DefinitionId = "def-1",
            Version = 1,
            Name = "FromFile",
            StringData = "FILE_ROOT",
            OriginalSource = "FILE_JSON",
            MaterializerName = JsonWorkflowMaterializer.MaterializerName
        };
        var editedRoot = new Sequence { Id = "edited-root" };
        var mappedRoot = new WriteLine("from-studio");
        Mock.Get(_publisher).GetDraftAsync("def-1", VersionOptions.Latest, Arg.Any<CancellationToken>())
                    .Returns(fileImportedDraft);
        Mock.Get(_publisher).SaveDraftAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
                    .Returns((global::Flamme.Workflows.Management.Entities.WorkflowDefinition __arg0, global::System.Threading.CancellationToken __arg1) => __arg0);
        Mock.Get(_serializer).Serialize(Arg.Any<IActivity>()).Returns("EDITED_JSON");
        Mock.Get(_serializer).Deserialize("EDITED_JSON").Returns(mappedRoot);
        Mock.Get(_serializer).Deserialize<WorkflowDefinitionModel>("FILE_JSON")
                    .Returns(new WorkflowDefinitionModel
                    {
                        Name = "FromFile",
                        Root = new WriteLine("from-file")
                    });

        var result = await _importer.ImportAsync(new SaveWorkflowDefinitionRequest
        {
            Model = new WorkflowDefinitionModel
            {
                DefinitionId = "def-1",
                Name = "Edited",
                Root = editedRoot
            },
            Publish = false
        });

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.WorkflowDefinition.OriginalSource).IsNull();
        await Assert.That(result.WorkflowDefinition.StringData).IsEqualTo("EDITED_JSON");
        await Assert.That(result.WorkflowDefinition.Name).IsEqualTo("Edited");
        await Assert.That(result.WorkflowDefinition.MaterializerName).IsEqualTo(JsonWorkflowMaterializer.MaterializerName);

        var workflow = _mapper.Map(result.WorkflowDefinition);

        await Assert.That(workflow.Root).IsSameReferenceAs(mappedRoot);
        await Assert.That(workflow.WorkflowMetadata.Name).IsEqualTo("Edited");
        Mock.Get(_serializer).Deserialize<WorkflowDefinitionModel>("FILE_JSON").WasNeverCalled();
    }
}
