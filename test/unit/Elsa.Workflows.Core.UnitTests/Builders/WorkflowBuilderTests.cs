using Elsa.Workflows.Builders;
using Elsa.Workflows.Memory;
using NSubstitute;

namespace Elsa.Workflows.Core.UnitTests.Builders;

public class WorkflowBuilderTests
{
    [Test]
    public async Task WithVariable_NameAndValue_UsesWorkflowInstanceStorage()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var variable = builder.WithVariable("message", "hello");

        // Assert
        await Assert.That(variable.Name).IsEqualTo("message");
        await Assert.That(variable.Value).IsEqualTo("hello");
        await Assert.That(variable.StorageDriverType).IsEqualTo(typeof(WorkflowInstanceStorageDriver));
        await Assert.That(builder.Variables).Contains(variable);
    }

    [Test]
    public async Task WithVariable_NameAndValue_UsesSameStorageAsParameterlessOverload()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
#pragma warning disable CS0618 // Parameterless overload is obsolete but remains the persistence baseline.
        var unnamed = builder.WithVariable<string>();
#pragma warning restore CS0618
        var named = builder.WithVariable("message", "hello");

        // Assert
        await Assert.That(unnamed.StorageDriverType).IsEqualTo(typeof(WorkflowInstanceStorageDriver));
        await Assert.That(named.StorageDriverType).IsEqualTo(unnamed.StorageDriverType);
    }

    [Test]
    public async Task WithVariable_ExistingVariable_DoesNotOverrideStorageDriver()
    {
        // Arrange
        var builder = CreateBuilder();
        var variable = new Variable<string>("message", "hello");

        // Act
        builder.WithVariable(variable);

        // Assert
        await Assert.That(variable.StorageDriverType).IsNull();
        await Assert.That(builder.Variables).Contains(variable);
    }

    private static WorkflowBuilder CreateBuilder() =>
        new(
            Substitute.For<IActivityVisitor>(),
            Substitute.For<IIdentityGraphService>(),
            Substitute.For<IActivityRegistry>());
}
