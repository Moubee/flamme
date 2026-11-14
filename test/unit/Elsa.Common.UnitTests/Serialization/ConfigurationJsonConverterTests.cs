using System.Text.Json;
using Elsa.Common.Serialization;
using Microsoft.Extensions.Configuration;

namespace Elsa.Common.UnitTests.Serialization;

public class ConfigurationJsonConverterTests
{
    [Test]
    public async Task Deserialize_UnicodeAndNestedValues_PreservesConfigurationKeys()
    {
        const string json = """
        {
            "Greeting": "Hello! 你好! مرحبا! Здравствуйте! 🎉🎊",
            "Nested": { "Enabled": true, "Count": 42 },
            "Items": ["first", "第二"]
        }
        """;

        var configuration = JsonSerializer.Deserialize<IConfiguration>(json, CreateOptions())!;

        await Assert.That(configuration["Greeting"]).IsEqualTo("Hello! 你好! مرحبا! Здравствуйте! 🎉🎊");
        await Assert.That(configuration["Nested:Enabled"]).IsEqualTo("True");
        await Assert.That(configuration["Nested:Count"]).IsEqualTo("42");
        await Assert.That(configuration["Items:0"]).IsEqualTo("first");
        await Assert.That(configuration["Items:1"]).IsEqualTo("第二");
    }

    [Test]
    public async Task Deserialize_CommentsAndTrailingCommas_RespectsReaderOptions()
    {
        const string json = """
        {
            // Retain support for reader-enabled JSON extensions.
            "Greeting": "你好 🎉",
            "Items": [1, 2,],
        }
        """;
        var options = CreateOptions();
        options.ReadCommentHandling = JsonCommentHandling.Skip;
        options.AllowTrailingCommas = true;

        var configuration = JsonSerializer.Deserialize<IConfiguration>(json, options)!;

        await Assert.That(configuration["Greeting"]).IsEqualTo("你好 🎉");
        await Assert.That(configuration["Items:1"]).IsEqualTo("2");
    }

    [Test]
    public async Task Deserialize_DuplicateConfigurationKeys_ThrowsFormatException()
    {
        await Assert.ThrowsAsync<FormatException>(() => Task.FromResult(JsonSerializer.Deserialize<IConfiguration>(
            """{"Greeting":"first","greeting":"second"}""", CreateOptions())));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ConfigurationJsonConverter());
        return options;
    }
}
