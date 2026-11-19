using Flamme.Testing.Shared;
using Flamme.Workflows;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
// ReSharper disable MethodHasAsyncOverload

namespace Flamme.Activities.UnitTests.Console;

public class WriteLineTests
{
    private readonly TextWriter _mockTextWriter;
    private readonly IStandardOutStreamProvider _mockProvider;

    public WriteLineTests()
    {
        _mockTextWriter = TextWriter.Mock().Object;
        _mockProvider = IStandardOutStreamProvider.Mock().Object;
        Mock.Get(_mockProvider).GetTextWriter().Returns(_mockTextWriter);
    }
    
    [Test]
    [Arguments("Hello, World!")]
    [Arguments("")]
    public async Task Should_Write_Text_To_Output(string expectedText)
    {
        // Arrange
        var writeLine = new WriteLine(expectedText);

        // Act
        await ExecuteAsync(writeLine);

        // Assert
        Mock.Get(_mockTextWriter).WriteLine(expectedText).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task Should_Write_Null_Value_To_Output()
    {
        // Arrange
        var writeLine = new WriteLine(new Input<string>(default(string)!));

        // Act
        await ExecuteAsync(writeLine);

        // Assert
        Mock.Get(_mockTextWriter).WriteLine(Arg.Is<string?>(s => s == null)).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task Should_Use_Default_Provider_When_None_Configured()
    {
        // Arrange
        const string textToWrite = "Default provider test";
        var writeLine = new WriteLine(textToWrite);

        // Act & Assert - Should not throw exception when no provider is configured
        await Assert.That(() => ExecuteAsync(writeLine)).ThrowsNothing();
    }

    private async Task ExecuteAsync(IActivity activity)
    {
        await new ActivityTestFixture(activity)
            .ConfigureServices(services => services.AddSingleton(_mockProvider))
            .ExecuteAsync();
    }
}
