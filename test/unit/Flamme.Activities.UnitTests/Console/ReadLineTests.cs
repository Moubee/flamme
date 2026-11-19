using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Workflows;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Flamme.Activities.UnitTests.Console;

/// <summary>
/// Unit tests for the <see cref="ReadLine"/> activity.
/// </summary>
public class ReadLineTests
{
    private readonly TextReader _mockTextReader;
    private readonly IStandardInStreamProvider _mockProvider;

    public ReadLineTests()
    {
        _mockTextReader = TextReader.Mock().Object;
        _mockProvider = IStandardInStreamProvider.Mock().Object;
        Mock.Get(_mockProvider).GetTextReader().Returns(_mockTextReader);
    }

    [Test]
    [DisplayName("ReadLine reads text from input and completes successfully")]
    [Arguments("Hello, World!")]
    [Arguments("Test input")]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("\t")]
    public async Task Should_Read_Text_From_Input_And_Complete(string inputText)
    {
        // Arrange
        var readLine = new ReadLine();

        // Act
        var context = await ExecuteAsync(readLine, inputText);

        // Assert
        await Assert.That(context.Status).IsEqualTo(ActivityStatus.Completed);
        var result = (string)context.GetActivityOutput(() => readLine.Result)!;
        await Assert.That(result).IsEqualTo(inputText);
    }

    [Test]
    [DisplayName("ReadLine uses provided stream provider")]
    public async Task Should_Use_Provided_Stream_Provider()
    {
        // Act
        await ExecuteAsync(new ReadLine());

        // Assert
        Mock.Get(_mockProvider).GetTextReader().WasCalled(Times.Exactly(1));
        Mock.Get(_mockTextReader).ReadLine().WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("ReadLine handles null return value from stream")]
    public async Task Should_Handle_Null_Return_Value()
    {
        // Arrange
        var readLine = new ReadLine();

        // Act
        var context = await ExecuteAsync(readLine, null);

        // Assert - The null-forgiving operator in ReadLine implementation means it expects non-null,
        // but null can occur at end of stream. Verify the behavior stores null.
        var result = context.GetActivityOutput(() => readLine.Result);
        await Assert.That(result).IsNull();
    }

    [Test]
    [DisplayName("ReadLine uses default provider when none is registered")]
    public async Task Should_Use_Default_Provider_When_None_Registered()
    {
        // Arrange
        var readLine = new ReadLine();
        const string expectedInput = "Default provider test";
        var originalConsoleIn = System.Console.In;

        try
        {
            // Redirect Console.In to our StringReader
            System.Console.SetIn(new StringReader(expectedInput));

            // Act
            var context = await new ActivityTestFixture(readLine).ExecuteAsync();

            // Assert
            var result = (string)context.GetActivityOutput(() => readLine.Result)!;
            await Assert.That(result).IsEqualTo(expectedInput);
        }
        finally
        {
            // Restore original Console.In
            System.Console.SetIn(originalConsoleIn);
        }
    }

    private Task<ActivityExecutionContext> ExecuteAsync(ReadLine readLine, string? inputText = "Test")
    {
        Mock.Get(_mockTextReader).ReadLine().Returns(inputText);
        return new ActivityTestFixture(readLine)
            .ConfigureServices(services => services.AddSingleton(_mockProvider))
            .ExecuteAsync();
    }
}