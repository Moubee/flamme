using Elsa.Common.Serialization;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Mappers;
using Elsa.Workflows.Management.Materializers;
using Elsa.Workflows.Management.Models;
using Elsa.Workflows.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;

namespace Elsa.Workflows.Management.UnitTests.Mappers;

public class WorkflowDefinitionMapperOriginalSourceTests
{
    private readonly IActivitySerializer _activitySerializer = IActivitySerializer.Mock().Object;
    private readonly WorkflowDefinitionMapper _mapper;

    public WorkflowDefinitionMapperOriginalSourceTests()
    {
        var scopeFactory = IServiceScopeFactory.Mock().Object;
        var variableDefinitionMapper = new VariableDefinitionMapper(
            SerializationTypeRegistry.CreateDefault(),
            scopeFactory,
            NullLogger<VariableDefinitionMapper>.Instance);
        var workflowDefinitionService = IWorkflowDefinitionService.Mock().Object;
        _mapper = new(_activitySerializer, workflowDefinitionService, variableDefinitionMapper);
    }

    [Test]
    public async Task Map_WhenStringDataAndOriginalSourceBothPresent_UsesEditedStringData()
    {
        var fileRoot = new WriteLine("from-file");
        var editedRoot = new WriteLine("from-studio");
        Mock.Get(_activitySerializer).Deserialize<WorkflowDefinitionModel>("FILE_JSON")
                    .Returns(new WorkflowDefinitionModel
                    {
                        Name = "FromFile",
                        Root = fileRoot
                    });
        Mock.Get(_activitySerializer).Deserialize("EDITED_JSON").Returns(editedRoot);

        var definition = CreateDefinition(
            name: "Edited",
            stringData: "EDITED_JSON",
            originalSource: "FILE_JSON");

        var workflow = _mapper.Map(definition);

        await Assert.That(workflow.Root).IsSameReferenceAs(editedRoot);
        await Assert.That(workflow.WorkflowMetadata.Name).IsEqualTo("Edited");
        Mock.Get(_activitySerializer).Deserialize<WorkflowDefinitionModel>("FILE_JSON").WasNeverCalled();
    }

    [Test]
    public async Task Map_WhenOnlyOriginalSourceIsPresent_UsesOriginalSource()
    {
        var sourceRoot = new WriteLine("from-original-source");
        Mock.Get(_activitySerializer).Deserialize<WorkflowDefinitionModel>("SOURCE")
                    .Returns(new WorkflowDefinitionModel
                    {
                        Name = "FromSource",
                        Root = sourceRoot
                    });

        var definition = CreateDefinition(
            name: "EntityName",
            stringData: null,
            originalSource: "SOURCE");

        var workflow = _mapper.Map(definition);

        await Assert.That(workflow.Root).IsSameReferenceAs(sourceRoot);
        await Assert.That(workflow.WorkflowMetadata.Name).IsEqualTo("FromSource");
        Mock.Get(_activitySerializer).Deserialize(Arg.Any<string>()).WasNeverCalled();
    }

    private static WorkflowDefinition CreateDefinition(string name, string? stringData, string? originalSource)
    {
        return new()
        {
            Id = "version-1",
            DefinitionId = "def-1",
            Version = 1,
            Name = name,
            StringData = stringData,
            OriginalSource = originalSource,
            MaterializerName = JsonWorkflowMaterializer.MaterializerName
        };
    }
}
