using System.Reflection;
using Elsa.Common.Serialization;
using Elsa.Extensions;
using Elsa.Features.Attributes;
using Elsa.Features.Services;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Features;
using Elsa.Workflows.Options;
using Elsa.Workflows.Runtime.ActivationValidators;
using Elsa.Workflows.Runtime.Options;
using Elsa.Workflows.Runtime.Providers;
using TUnit.Mocks;
using RuntimeFeature = Elsa.Workflows.Runtime.Features.WorkflowRuntimeFeature;

namespace Elsa.Workflows.Runtime.UnitTests.Features;

public class WorkflowRuntimeFeatureTests
{
    private readonly RuntimeFeature _feature = new(Mock.Of<IModule>().Object);

    [Test]
    public void AddWorkflow_Throws_WhenTypeDoesNotImplementWorkflow()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _feature.AddWorkflow(typeof(NotAWorkflow)));
    }

    [Test]
    [MethodDataSource(nameof(NonInstantiableWorkflowTypes))]
    public void AddWorkflow_Throws_WhenWorkflowTypeIsNotInstantiable(Type workflowType)
    {
        Assert.ThrowsExactly<ArgumentException>(() => _feature.AddWorkflow(workflowType));
    }

    [Test]
    public async Task AddWorkflow_AllowsClosedGenericWorkflowType()
    {
        var workflowType = typeof(GenericWorkflow<int>);

        _feature.AddWorkflow(workflowType);

        await Assert.That(_feature.Workflows.Keys).Contains(workflowType.GetSimpleAssemblyQualifiedName());
        await Assert.That(_feature.Workflows.Keys).Contains(workflowType.FullName!);
    }

    [Test]
    public async Task WorkflowsAdd_RegistersWorkflowTypeAlias()
    {
        var workflowType = typeof(GenericWorkflow<int>);
        var options = new SerializationTypeOptions();

        _feature.Workflows.Add(workflowType);

        RegisterWorkflowTypeAliases(_feature, options);

        await Assert.That(options.AliasTypeDictionary[workflowType.GetSimpleAssemblyQualifiedName()]).IsEqualTo(workflowType);
    }

    [Test]
    public async Task WorkflowsAdd_DoesNotThrow_WhenLegacyKeyAlreadyExists()
    {
        var workflowType = typeof(GenericWorkflow<int>);
        _feature.Workflows.Add(workflowType.FullName!, _ => new ValueTask<IWorkflow>(new GenericWorkflow<int>()));

        _feature.Workflows.Add(workflowType);

        await Assert.That(_feature.Workflows.Keys).Contains(workflowType.GetSimpleAssemblyQualifiedName());
        await Assert.That(_feature.Workflows.Keys).Contains(workflowType.FullName!);
        var canonicalFactory = _feature.Workflows[workflowType.GetSimpleAssemblyQualifiedName()];
        var legacyFactory = _feature.Workflows[workflowType.FullName!];
        await Assert.That(ReferenceEquals(canonicalFactory, legacyFactory)).IsTrue();
    }

    [Test]
    public async Task ClrWorkflowsProvider_MaterializesWorkflowOnce_WhenCanonicalAndLegacyKeysExist()
    {
        var createdCount = 0;
        var builder = Mock.Of<IWorkflowBuilder>().Object;
        var builderFactory = Mock.Of<IWorkflowBuilderFactory>().Object;
        var provider = new ClrWorkflowsProvider(
            Microsoft.Extensions.Options.Options.Create(new RuntimeOptions { Workflows = _feature.Workflows }),
            builderFactory,
            Mock.Of<IServiceProvider>().Object);
        Mock.Get(builderFactory).CreateBuilder().Returns(builder);
        Mock.Get(builder).BuildWorkflowAsync(Arg.Any<CancellationToken>()).Returns(new Workflow());

        var workflowType = typeof(CountingWorkflow);
        Func<IServiceProvider, ValueTask<IWorkflow>> factory = _ =>
        {
            Interlocked.Increment(ref createdCount);
            return new(new CountingWorkflow());
        };
        _feature.Workflows.Add(workflowType.GetSimpleAssemblyQualifiedName(), factory);
        _feature.Workflows.Add(workflowType.FullName!, factory);
        var workflows = await provider.GetWorkflowsAsync();

        await Assert.That(workflows).HasSingleItem();
        await Assert.That(createdCount).IsEqualTo(1);
    }

    [Test]
    public void WorkflowsAdd_Throws_WhenTypeDoesNotImplementWorkflow()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _feature.Workflows.Add(typeof(NotAWorkflow)));
    }

    [Test]
    public async Task RuntimeFeature_DependsOnWorkflowsFeature()
    {
        var dependencyTypes = typeof(RuntimeFeature)
            .GetCustomAttributes<Elsa.Features.Attributes.DependsOnAttribute>()
            .Select(x => x.Type);

        await Assert.That(dependencyTypes).Contains(typeof(WorkflowsFeature));
    }

    [Test]
    public async Task RegisterWorkflowTypeAliases_RegistersBuiltInActivationStrategyAliases()
    {
        var options = new SerializationTypeOptions();

        RegisterWorkflowTypeAliases(_feature, options);
        var registry = new SerializationTypeRegistry(Microsoft.Extensions.Options.Options.Create(options));

        await AssertActivationStrategyAlias(options, registry, typeof(SingletonStrategy), nameof(SingletonStrategy));
        await AssertActivationStrategyAlias(options, registry, typeof(CorrelatedSingletonStrategy), nameof(CorrelatedSingletonStrategy));
        await AssertActivationStrategyAlias(options, registry, typeof(CorrelationStrategy), nameof(CorrelationStrategy));
    }

    [Test]
    public async Task RegisterWorkflowTypeAliases_RegistersOnlyTrackedWorkflowTypes()
    {
        var workflowType = typeof(GenericWorkflow<int>);
        var options = new SerializationTypeOptions();
        _feature.AddWorkflow(workflowType);
        _feature.Workflows[typeof(NotAWorkflow).AssemblyQualifiedName!] = _ => new ValueTask<IWorkflow>(new GenericWorkflow<int>());

        RegisterWorkflowTypeAliases(_feature, options);

        await Assert.That(options.AliasTypeDictionary[workflowType.GetSimpleAssemblyQualifiedName()]).IsEqualTo(workflowType);
        await Assert.That(options.AliasTypeDictionary.Keys).DoesNotContain(typeof(NotAWorkflow).AssemblyQualifiedName!);
    }

    public static IEnumerable<Type> NonInstantiableWorkflowTypes() =>
    [
        typeof(IWorkflow),
        typeof(WorkflowBase),
        typeof(GenericWorkflow<>)
    ];

    private sealed class NotAWorkflow
    {
    }

    private sealed class GenericWorkflow<T> : IWorkflow
    {
        public ValueTask BuildAsync(IWorkflowBuilder builder, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    public sealed class CountingWorkflow : IWorkflow
    {
        public ValueTask BuildAsync(IWorkflowBuilder builder, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    private static void RegisterWorkflowTypeAliases(object feature, SerializationTypeOptions options)
    {
        feature.GetType()
            .GetMethod("RegisterWorkflowTypeAliases", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(feature, new object[] { options });
    }

    private static async Task AssertActivationStrategyAlias(SerializationTypeOptions options, ISerializationTypeRegistry registry, Type strategyType, string alias)
    {
        await Assert.That(options.AliasTypeDictionary[alias]).IsEqualTo(strategyType);
        await Assert.That(options.TypeAliasDictionary[strategyType]).IsEqualTo(alias);
        await Assert.That(registry.TryGetType(strategyType.GetSimpleAssemblyQualifiedName(), out var legacyType)).IsTrue();
        await Assert.That(legacyType).IsEqualTo(strategyType);
    }
}
