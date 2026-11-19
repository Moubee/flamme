using System.Dynamic;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elsa.Common.Converters;
using Elsa.Expressions.JavaScript.Contracts;
using Elsa.Expressions.Models;
using Elsa.Testing.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.JavaScript.IntegrationTests;
public class JsonConverterTest : IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider = new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput).Build();

    public async ValueTask DisposeAsync()
    {
        if (_serviceProvider is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }

    [Test]
    [DisplayName("JavaScript BigInt mapping to BigInteger serialization")]
    public async Task Test1()
    {
        var javaScriptEvaluator = _serviceProvider.GetRequiredService<IJavaScriptEvaluator>();
        var script = @"return {
    'BigNumber': BigInt('7239948466988781569') 
}";
        var expressionExecutionContext = new ExpressionExecutionContext(_serviceProvider, new MemoryRegister());
        var result = (await javaScriptEvaluator.EvaluateAsync(script, typeof(ExpandoObject), expressionExecutionContext))!;
        var options = new JsonSerializerOptions
        {
            Converters = { new BigIntegerJsonConverter() }
        };
        var serializedText = JsonSerializer.Serialize((ExpandoObject)result, new TestJsonSerializerContext(options).ExpandoObject);

        await Assert.That(serializedText).IsEqualTo("{\"BigNumber\":7239948466988781569}");
    }

    [Test]
    [DisplayName("JavaScript BigInt mapping to BigInteger serialization")]
    public async Task Test2()
    {
        var javaScriptEvaluator = _serviceProvider.GetRequiredService<IJavaScriptEvaluator>();
        var script = @"return {
    'BigNumber': BigInt('7239948466988781569') 
}";
        var expressionExecutionContext = new ExpressionExecutionContext(_serviceProvider, new MemoryRegister());
        var result = (await javaScriptEvaluator.EvaluateAsync(script, typeof(ExpandoObject), expressionExecutionContext))!;
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = TestJsonSerializerContext.Default.WithAddedModifier(typeInfo =>
            {
                if (typeInfo.Type != typeof(BigInteger))
                    return;

                // Preserve the property order of the original reflection-based contract.
                foreach (var property in typeInfo.Properties)
                    property.Order = property.Name switch
                    {
                        nameof(BigInteger.IsPowerOfTwo) => 0,
                        nameof(BigInteger.IsZero) => 1,
                        nameof(BigInteger.IsOne) => 2,
                        nameof(BigInteger.IsEven) => 3,
                        nameof(BigInteger.Sign) => 4,
                        _ => 5
                    };
            })
        };
        var typeInfo = (JsonTypeInfo<ExpandoObject>)options.GetTypeInfo(typeof(ExpandoObject));
        var serializedText = JsonSerializer.Serialize((ExpandoObject)result, typeInfo);

        await Assert.That(serializedText).IsEqualTo("{\"BigNumber\":{\"IsPowerOfTwo\":false,\"IsZero\":false,\"IsOne\":false,\"IsEven\":false,\"Sign\":1}}");
    }

    [Test]
    [DisplayName("BigIntegerJsonConverter Deserialize")]
    public async Task Test3()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new BigIntegerJsonConverter() }
        };

        BigInteger bigInteger = JsonSerializer.Deserialize("7239948466988781569", new TestJsonSerializerContext(options).BigInteger);

        await Assert.That(bigInteger).IsEqualTo(BigInteger.Parse("7239948466988781569"));
    }
}
