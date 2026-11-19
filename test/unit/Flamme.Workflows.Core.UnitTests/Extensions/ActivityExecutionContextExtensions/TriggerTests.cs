using Flamme.Extensions;
using static Flamme.Workflows.Core.UnitTests.Extensions.ActivityExecutionContextExtensions.TestHelpers;
using System.Threading.Tasks;

namespace Flamme.Workflows.Core.UnitTests.Extensions.ActivityExecutionContextExtensions;

public class TriggerTests
{
    [Test]
    [Arguments(true, null)] // null means use context.Activity.Id
    [Arguments(false, "different-id")]
    public async Task IsTriggerOfWorkflow_ReturnsExpectedResult(bool expectedResult, string? triggerActivityId)
    {
        // Arrange
        var context = await CreateContextAsync();
        context.WorkflowExecutionContext.TriggerActivityId = triggerActivityId ?? context.Activity.Id;

        // Act
        var result = context.IsTriggerOfWorkflow();

        // Assert
        await Assert.That(result).IsEqualTo(expectedResult);
    }
}