using System.Text.Json;
using Flamme.Common.Serialization;
using Flamme.Expressions.Services;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Helpers;
using Flamme.Workflows.Management.Activities.WorkflowDefinitionActivity;
using Flamme.Workflows.Management.Services;
using Flamme.Workflows.Models;
using Flamme.Workflows.Serialization.Configurators;
using Flamme.Workflows.Serialization.Converters;
using Flamme.Workflows.Serialization.Helpers;
using Microsoft.Extensions.Logging;
using TUnit.Mocks;
using System.Threading.Tasks;

namespace Flamme.Workflows.Core.UnitTests.Serialization.Converters;

public sealed class ActivityJsonConverterTests
{
    [Test]
    public async Task When_DeserializeKnownActivity_And_TypeNameSpecified_Then_FindsAndInstantiatesActivity()
    {
        // Arrange
        var activityRegistry = CreateActivityRegistry(WriteLineActivityTypeName, WriteLineActivity);
        var sut = CreateSut(activityRegistry);

        // Act
        var result = Execute(sut, WriteLineActivityJson_WithoutVersion);

        // Assert
        await Assert.That(result).IsSameReferenceAs(WriteLineActivity);
    }

    [Test]
    public async Task When_DeserializeKnownActivity_And_TypeNameSpecified_And_VersionSpecified_Then_FindsAndInstantiatesActivity()
    {
        // Arrange
        var activityRegistry = CreateActivityRegistry(WriteLineActivityTypeName, WriteLineActivity, version: 1);
        var sut = CreateSut(activityRegistry);

        // Act
        var result = Execute(sut, WriteLineActivityJson_WithVersion);

        // Assert
        await Assert.That(result).IsSameReferenceAs(WriteLineActivity);
    }

    [Test]
    public async Task ExplicitUnknownMajorPreservesTheMissingContract()
    {
        var registry = CreateActivityRegistry(WriteLineActivityTypeName, WriteLineActivity);
        Mock.Get(registry).Find(NotFoundActivityTypeName).Returns(new ActivityDescriptor());

        var result = Execute(CreateSut(registry), """{ "type": "Console.WriteLine", "version": 42 }""");

        var missing = await Assert.That(result).IsTypeOf<NotFoundActivity>();
        await Assert.That(missing!.MissingTypeName).IsEqualTo("Console.WriteLine");
        await Assert.That(missing.MissingTypeVersion).IsEqualTo(42);
    }

    [Test]
    public async Task When_DeserializeUnknownActivity_Then_ReturnsNotFoundActivity()
    {
        // Arrange
        var activityRegistry = IActivityRegistry.Mock().Object;
        Mock.Get(activityRegistry).Find(NotFoundActivityTypeName)
                    .Returns(new ActivityDescriptor());

        var sut = CreateSut(activityRegistry);

        // Act
        var result = Execute(sut, UnknownActivityJson);

        // Assert
        await Assert.That(result).IsTypeOf<NotFoundActivity>();
        var notFoundActivity = (NotFoundActivity)result!;
        await Assert.That(notFoundActivity.MissingTypeName).IsEqualTo(UnknownActivityTypeName);
        await Assert.That(notFoundActivity.MissingTypeVersion).IsEqualTo(0);

        var expectedJsonDoc = JsonDocument.Parse(UnknownActivityJson);
        var actualJsonDoc = JsonDocument.Parse(notFoundActivity.OriginalActivityJson);
        await Assert.That(actualJsonDoc.RootElement.ToString()).IsEqualTo(expectedJsonDoc.RootElement.ToString());
        await Assert.That(notFoundActivity.Metadata.ContainsKey("displayText")).IsTrue();
        await Assert.That(notFoundActivity.Metadata.ContainsKey("description")).IsTrue();
    }

    [Test]
    public async Task When_DeserializeWorkflowAsActivity_And_WorkflowDefinitionIdSpecified_Then_FindsAndInstantiatesActivity()
    {
        // Arrange
        var activityRegistry = CreateActivityRegistry_FindByCustomProperty(
            WorkflowDefinitionIdCustomPropertyName,
            WorkflowAsActivityDefinitionId
        );

        var sut = CreateSut(activityRegistry);

        // Act
        var result = Execute(sut, WorkflowAsActivityJson_WithDefinitionId);

        // Assert
        await Assert.That(result).IsSameReferenceAs(WorkflowAsActivity);
    }

    [Test]
    public async Task When_DeserializeWorkflowAsActivity_And_WorkflowDefinitionVersionIdSpecified_Then_FindsAndInstantiatesActivity()
    {
        // Arrange
        var activityRegistry = CreateActivityRegistry_FindByCustomProperty(
            WorkflowDefinitionVersionIdCustomPropertyName,
            WorkflowAsActivityDefinitionVersionId
        );
        var sut = CreateSut(activityRegistry);

        // Act
        var result = Execute(sut, WorkflowAsActivityJson_WithVersionId);

        // Assert
        await Assert.That(result).IsSameReferenceAs(WorkflowAsActivity);
    }

    [Test]
    public async Task When_DeserializeWorkflowAsActivity_And_TypeNameSpecified_Then_FindsAndInstantiatesActivity()
    {
        // Arrange
        var activityRegistry = CreateActivityRegistry(WorkflowAsActivityTypeName, WorkflowAsActivity);
        var sut = CreateSut(activityRegistry);

        // Act
        var result = Execute(sut, WorkflowAsActivityJson_WithTypeNameOnly);

        // Assert
        await Assert.That(result).IsSameReferenceAs(WorkflowAsActivity);
    }

    [Test]
    public async Task ExactChildRevisionTakesPrecedenceOverItsCachedVersion()
    {
        var registry = CreateActivityRegistry_FindByCustomProperty(
            WorkflowDefinitionVersionIdCustomPropertyName, WorkflowAsActivityDefinitionVersionId);
        var json = $$"""{ "type": "Test-SubWorkflow", "version": 42, "workflowDefinitionVersionId": "{{WorkflowAsActivityDefinitionVersionId}}" }""";

        var result = Execute(CreateSut(registry), json);

        await Assert.That(result).IsSameReferenceAs(WorkflowAsActivity);
    }

    static IActivity? Execute(ActivityJsonConverter sut, string json) =>
        JsonSerializer.Deserialize(json, TestJsonTypeInfo.FromConverter<IActivity>(GetSerializerOptions(sut)));

    static IActivityRegistry CreateActivityRegistry(string typeName, IActivity activity, int? version = null)
    {
        var descriptor = new ActivityDescriptor { Constructor = _ => new(activity) };
        var activityRegistry = IActivityRegistry.Mock().Object;

        if (version.HasValue)
            Mock.Get(activityRegistry).Find(typeName, version.Value).Returns(descriptor);
        else
            Mock.Get(activityRegistry).Find(typeName).Returns(descriptor);

        return activityRegistry;
    }

    static IActivityRegistry CreateActivityRegistry_FindByCustomProperty(string customPropertyName, string customPropertyValue)
    {
        var descriptor = new ActivityDescriptor
        {
            Constructor = _ => new(WorkflowAsActivity),
            CustomProperties = { [customPropertyName] = customPropertyValue }
        };

        var activityRegistry = IActivityRegistry.Mock().Object;

        // Mock type name lookups to return null, ensuring the custom property lookup path is tested
        Mock.Get(activityRegistry).Find(Arg.Any<string>()).Returns((ActivityDescriptor?)null);
        Mock.Get(activityRegistry).Find(Arg.Any<string>(), Arg.Any<int>()).Returns((ActivityDescriptor?)null);
        Mock.Get(activityRegistry).Find(Arg.Any<Func<ActivityDescriptor, bool>>())
                    .Returns((global::System.Func<global::Flamme.Workflows.Models.ActivityDescriptor, bool> __arg0) => __arg0(descriptor) ? descriptor : null);

        return activityRegistry;
    }

    static JsonSerializerOptions GetSerializerOptions(ActivityJsonConverter sut) => new()
    {
        Converters =
        {
            sut,
            new TypeJsonConverter(),
            new PolymorphicObjectConverterFactory()
        },
        TypeInfoResolver = new ModifiableJsonTypeInfoResolver(new CustomConstructorConfigurator().GetModifiers())
    };

    private const string UnknownActivityTypeName = "Some.Unknown.Activity";
    private const string WorkflowAsActivityTypeName = "Test-SubWorkflow";
    private const string WorkflowDefinitionIdCustomPropertyName = "WorkflowDefinitionId";
    private const string WorkflowDefinitionVersionIdCustomPropertyName = "WorkflowDefinitionVersionId";
    private const string WorkflowAsActivityDefinitionId = "bd94124913202141";
    private const string WorkflowAsActivityDefinitionVersionId = "aaa1112fff2443";
    private static readonly WriteLine WriteLineActivity = new("Hello world!");
    private static readonly string WriteLineActivityTypeName = ActivityTypeNameHelper.GenerateTypeName<WriteLine>();
    private static readonly string NotFoundActivityTypeName = ActivityTypeNameHelper.GenerateTypeName<NotFoundActivity>();

    private const string WriteLineActivityJson_WithVersion =
"""
 {
     "text": {
         "typeName": "String",
         "expression": {
             "type": "Literal",
             "value": "Hello world!"
         }
     },
     "id": "b54b024aa7469287",
     "nodeId": "Workflow1:51139b0cc83d3b07:b54b024aa7469287",
     "name": "WriteLine1",
     "type": "Console.WriteLine",
     "version": 1,
     "customProperties": {
         "canStartWorkflow": false,
         "runAsynchronously": false
     },
     "metadata": {}
 }
 """;

    private const string UnknownActivityJson =
"""
{
   "text": {
       "typeName": "String",
       "expression": {
           "type": "Literal",
           "value": "Hello world!"
       }
   },
   "id": "b54b024aa7469287",
   "nodeId": "Workflow1:51139b0cc83d3b07:b54b024aa7469287",
   "name": "Unknown1",
   "type": "Some.Unknown.Activity",
   "customProperties": {
       "canStartWorkflow": false,
       "runAsynchronously": false
   },
   "metadata": {}
}
""";

    private const string WriteLineActivityJson_WithoutVersion =
"""
{
    "text": {
        "typeName": "String",
        "expression": {
            "type": "Literal",
            "value": "Hello world!"
        }
    },
    "id": "b54b024aa7469287",
    "nodeId": "Workflow1:51139b0cc83d3b07:b54b024aa7469287",
    "name": "WriteLine1",
    "type": "Console.WriteLine",
    "customProperties": {
        "canStartWorkflow": false,
        "runAsynchronously": false
    },
    "metadata": {}
}
""";

    private const string WorkflowAsActivityJson_WithVersionId =
"""
{
    "workflowDefinitionVersionId": "aaa1112fff2443",
    "id": "5fc551ed366fe9fd",
    "nodeId": "Workflow2:cad9d2186550a8d7:5fc551ed366fe9fd",
    "name": "Test-SubWorkflow1",
    "type": "Test-SubWorkflow",
    "customProperties": {
        "canStartWorkflow": false,
        "runAsynchronously": false,
        "logPersistenceMode": {
            "default": "Inherit",
            "inputs": {},
            "outputs": {}
        },
        "logPersistenceConfig": {
            "default": null,
            "internalState": null,
            "inputs": {},
            "outputs": {}
        }
    },
    "metadata": {}
}
""";

    private const string WorkflowAsActivityJson_WithDefinitionId =
"""
{
   "workflowDefinitionId": "bd94124913202141",
   "id": "5fc551ed366fe9fd",
   "nodeId": "Workflow2:cad9d2186550a8d7:5fc551ed366fe9fd",
   "name": "Test-SubWorkflow1",
   "type": "Test-SubWorkflow",
   "customProperties": {
       "canStartWorkflow": false,
       "runAsynchronously": false,
       "logPersistenceMode": {
           "default": "Inherit",
           "inputs": {},
           "outputs": {}
       },
       "logPersistenceConfig": {
           "default": null,
           "internalState": null,
           "inputs": {},
           "outputs": {}
       }
   },
   "metadata": {}
}
""";

    private const string WorkflowAsActivityJson_WithTypeNameOnly =
"""
{
   "id": "5fc551ed366fe9fd",
   "nodeId": "Workflow2:cad9d2186550a8d7:5fc551ed366fe9fd",
   "name": "Test-SubWorkflow1",
   "type": "Test-SubWorkflow",
   "customProperties": {
       "canStartWorkflow": false,
       "runAsynchronously": false,
       "logPersistenceMode": {
           "default": "Inherit",
           "inputs": {},
           "outputs": {}
       },
       "logPersistenceConfig": {
           "default": null,
           "internalState": null,
           "inputs": {},
           "outputs": {}
       }
   },
   "metadata": {}
}
""";

    private static readonly IActivity WorkflowAsActivity = new WorkflowDefinitionActivity
    {
        WorkflowDefinitionId = WorkflowAsActivityDefinitionId,
        WorkflowDefinitionVersionId = WorkflowAsActivityDefinitionVersionId
    };

    private static ActivityJsonConverter CreateSut(IActivityRegistry activityRegistry)
    {
        var expressionDescriptorRegistry = new ExpressionDescriptorRegistry([]);
        var serviceProvider = IServiceProvider.Mock().Object;
        Mock.Get(serviceProvider)
            .GetService(typeof(ILogger<ActivityJsonConverter>))
            .Returns(CreateLogger<ActivityJsonConverter>());

        return new(
            activityRegistry,
            expressionDescriptorRegistry,
            new(
                activityRegistry,
                new(expressionDescriptorRegistry),
                CreateLogger<ActivityWriter>()
            ),
            serviceProvider
        );
    }

    private static ILogger<T> CreateLogger<T>() => LoggerFactory.Create(_ => { }).CreateLogger<T>();
}
