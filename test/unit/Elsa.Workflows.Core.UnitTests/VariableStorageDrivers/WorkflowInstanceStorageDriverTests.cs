using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Common.Serialization;
using Elsa.Expressions.Helpers;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Serialization.Converters;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;
using System.Threading.Tasks;

namespace Elsa.Workflows.Core.UnitTests.VariableStorageDrivers;

public class WorkflowInstanceStorageDriverTests
{
    private const string IsolatedTestMarker = "ELSA_WORKFLOW_INSTANCE_STORAGE_DRIVER_ISOLATED_TEST";
    [Test]
    public async Task WriteAsync_WhenSerializeFails_PreservesPreviousValue()
    {
        var harness = CreateHarness(new Variable<string>("name", "kept"));
        const string id = "nameVariable";
        await harness.Driver.WriteAsync(id, "kept", harness.Context);
        var storedBefore = GetVariables(harness.Properties)[id].ToJsonString();
        await harness.Driver.WriteAsync(id, CreateUnserializableValue(), harness.Context);
        var dictionary = GetVariables(harness.Properties);
        await Assert.That(dictionary.ContainsKey(id)).IsTrue();
        await Assert.That(dictionary[id].ToJsonString()).IsEqualTo(storedBefore);
        var read = await harness.Driver.ReadAsync(id, harness.Context);
        await Assert.That(read).IsEqualTo("kept");
    }

    [Test]
    public async Task WriteAsync_WhenSerializeFailsWithNoPriorValue_DoesNotCreateEntry()
    {
        var harness = CreateHarness(new Variable<string>("name", ""));
        const string id = "nameVariable";
        await harness.Driver.WriteAsync(id, CreateUnserializableValue(), harness.Context);
        await Assert.That(GetVariables(harness.Properties).ContainsKey(id)).IsFalse();
    }

    [Test]
    public async Task DeleteAsync_RemovesStoredValue()
    {
        var harness = CreateHarness(new Variable<string>("name", "kept"));
        const string id = "nameVariable";
        await harness.Driver.WriteAsync(id, "kept", harness.Context);
        await harness.Driver.DeleteAsync(id, harness.Context);
        await Assert.That(GetVariables(harness.Properties).ContainsKey(id)).IsFalse();
    }

    [Test]
    public async Task WriteThenRead_WhenValueHasOrdinaryDollarRefProperty_RoundTrips()
    {
        // Arrange
        var harness = CreateHarness(new Variable<RefPayload>("payload", new()));
        const string id = "payloadVariable";
        var value = new RefPayload
        {
            Ref = "ordinary"
        };
        // Act
        await harness.Driver.WriteAsync(id, value, harness.Context);
        var read = await harness.Driver.ReadAsync(id, harness.Context);
        // Assert
        var restored = (await Assert.That(read).IsTypeOf<RefPayload>())!;
        await Assert.That(restored.Ref).IsEqualTo("ordinary");
    }

    [Test]
    public async Task WriteThenRead_WhenObjectVariableHoldsAliasedType_RestoresConcreteClrType()
    {
        // Arrange
        var registry = SerializationTypeRegistry.CreateDefault();
        registry.RegisterType(typeof(AliasedPerson), nameof(AliasedPerson));
        var harness = CreateHarness(new Variable<object>("payload", new()), registry);
        const string id = "payloadVariable";
        var value = new AliasedPerson
        {
            Name = "Ada"
        };
        // Act
        await harness.Driver.WriteAsync(id, value, harness.Context);
        var stored = GetVariables(harness.Properties)[id].AsObject();
        var read = await harness.Driver.ReadAsync(id, harness.Context);
        // Assert
        await Assert.That(stored["_type"]?.GetValue<string>()).IsEqualTo(nameof(AliasedPerson));
        var restored = (await Assert.That(read).IsTypeOf<AliasedPerson>())!;
        await Assert.That(restored.Name).IsEqualTo("Ada");
    }

    [Test]
    public async Task WriteAsync_WhenValueIsArray_StoresJsonArrayReadableWithDefaultConverter()
    {
        // Arrange
        var harness = CreateHarness(new Variable<string[]>("elements", []));
        const string id = "elementsVariable";
        // Act
        await harness.Driver.WriteAsync(id, new[] { "Element 1", "Element 2" }, harness.Context);
        var node = GetVariables(harness.Properties)[id];
        var actual = node.ConvertTo<string[]>();
        // Assert
        await Assert.That(node.GetValueKind()).IsEqualTo(JsonValueKind.Array);
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual).IsEquivalentTo(["Element 1", "Element 2"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task WriteAsync_WhenValueIsProjectedEnumerable_StoresJsonArrayReadableWithDefaultConverter()
    {
        // Arrange
        var harness = CreateHarness(new Variable<string[]>("messages", []));
        const string id = "messagesVariable";
        var value = new[]
        {
            "a",
            "b",
            "c"
        }.Select(x => x.ToUpperInvariant());
        // Act
        await harness.Driver.WriteAsync(id, value, harness.Context);
        var node = GetVariables(harness.Properties)[id];
        var actual = node.ConvertTo<string[]>();
        // Assert
        await Assert.That(node.GetValueKind()).IsEqualTo(JsonValueKind.Array);
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual).IsEquivalentTo(["A", "B", "C"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ReadAsync_WhenConvertFails_DoesNotReturnUntypedJsonNode()
    {
        var harness = CreateHarness(new Variable<int>("count", 0));
        const string id = "countVariable";
        SeedIncompatibleNode(harness.Properties, id);
        var read = await harness.Driver.ReadAsync(id, harness.Context);
        await Assert.That(read).IsNull();
        await Assert.That(read is JsonNode).IsFalse();
        await Assert.That(GetVariables(harness.Properties).ContainsKey(id)).IsTrue();
    }

    [Test]
    public async Task ReadAsync_WhenConvertFailsAndStrictMode_Throws()
    {
        const string testName = nameof(ReadAsync_WhenConvertFailsAndStrictMode_Throws);
        if (!string.Equals(Environment.GetEnvironmentVariable(IsolatedTestMarker), testName, StringComparison.Ordinal))
        {
            await RunIsolatedTestAsync(testName);
            return;
        }

        var harness = CreateHarness(new Variable<int>("count", 0));
        const string id = "countVariable";
        SeedIncompatibleNode(harness.Properties, id);
        var originalStrictMode = ObjectConverter.StrictMode;
        try
        {
            ObjectConverter.StrictMode = true;
            await Assert.ThrowsAsync<Exception>(() => harness.Driver.ReadAsync(id, harness.Context).AsTask());
            await Assert.That(GetVariables(harness.Properties).ContainsKey(id)).IsTrue();
        }
        finally
        {
            ObjectConverter.StrictMode = originalStrictMode;
        }
    }

    private static async Task RunIsolatedTestAsync(string testName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(typeof(WorkflowInstanceStorageDriverTests).Assembly.Location);
        startInfo.ArgumentList.Add("--treenode-filter");
        startInfo.ArgumentList.Add($"/*/*/{nameof(WorkflowInstanceStorageDriverTests)}/{testName}");
        startInfo.Environment[IsolatedTestMarker] = testName;
        using var process = new Process
        {
            StartInfo = startInfo
        };
        if (!process.Start())
            throw new InvalidOperationException($"Could not start isolated test process for {testName}.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        await Assert.That(process.ExitCode).IsEqualTo(0).Because($"Isolated test process for {testName} failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
    }

    private static Harness CreateHarness(Variable variable, ISerializationTypeRegistry? typeRegistry = null)
    {
        var properties = new Dictionary<string, object>();
        var executionContext = Mock.Of<IExecutionContext>().Object;
        Mock.Get(executionContext).Properties.Returns(properties);
        var payloadSerializer = Mock.Of<IPayloadSerializer>().Object;
        Mock.Get(payloadSerializer).GetOptions().Returns(CreatePayloadSerializerOptions(typeRegistry));
        var driver = new WorkflowInstanceStorageDriver(payloadSerializer, NullLogger<WorkflowInstanceStorageDriver>.Instance);
        var context = new StorageDriverContext(executionContext, variable, CancellationToken.None);
        return new(driver, context, properties);
    }

    private static JsonSerializerOptions CreatePayloadSerializerOptions(ISerializationTypeRegistry? typeRegistry = null)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(typeRegistry is null ? new PolymorphicObjectConverterFactory() : new PolymorphicObjectConverterFactory(typeRegistry));
        return options;
    }

    private static VariablesDictionary GetVariables(IDictionary<string, object> properties) => (VariablesDictionary)properties[WorkflowInstanceStorageDriver.VariablesDictionaryStateKey];
    private static void SeedIncompatibleNode(IDictionary<string, object> properties, string id)
    {
        properties[WorkflowInstanceStorageDriver.VariablesDictionaryStateKey] = new VariablesDictionary
        {
            [id] = JsonNode.Parse("""{"foo":"bar"}""")!
        };
    }

    private static CyclicValue CreateUnserializableValue()
    {
        var value = new CyclicValue();
        value.Self = value;
        return value;
    }

    private sealed record Harness(WorkflowInstanceStorageDriver Driver, StorageDriverContext Context, IDictionary<string, object> Properties);
    private sealed class CyclicValue
    {
        public CyclicValue Self { get; set; } = null!;
    }

    private sealed class RefPayload
    {
        [JsonPropertyName("$ref")]
        public string Ref { get; set; } = "";
    }

    private sealed class AliasedPerson
    {
        public string Name { get; set; } = "";
    }
}
