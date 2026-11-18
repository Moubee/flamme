using Elsa.CodeGeneration.Generators;
using Elsa.Features.Services;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.CodeGeneration.UnitTests;

public class FixedHostPlannerTests
{
    [Test]
    public async Task TwoFreshModulesCompileIndependentClosuresAndIgnoreDeferredSelections()
    {
        var result = Generate("""
            var first = services.CreateModule().Use<Leaf>();
            var second = services.CreateModule();
            first.Use<Root>();
            second.Use<Leaf>();
            System.Action opaque = () => second.Use<Unused>();
            """);
        await Assert.That(result.Diagnostics).IsEmpty();
        var host = HostSource(result);
        await Assert.That(host.Split(" : FixedFeatureModule").Length - 1).IsEqualTo(2);
        await Assert.That(host.Split("private readonly FixedFeatureSlot").Length - 1).IsEqualTo(3);
        await Assert.That(host).Contains("_leaf.Configure();");
        await Assert.That(host).Contains("_root.Apply();");
        await Assert.That(host.Contains("BuildAvailable", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task RuntimeBranchSelectingAnotherFeatureIsAnErrorAndAConstantFalseBranchAddsNothing()
    {
        var result = Generate("""
            var module = services.CreateModule();
            module.Configure<Leaf>();
            if (flag) module.Configure<Leaf>();
            if (false) module.Configure<Root>();
            if (flag) module.Configure<Unused>();
            """);
        var diagnostic = result.Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN012");
        await Assert.That(diagnostic.GetMessage()).Contains("Unused");
        await Assert.That(HostSource(result).Split("private readonly FixedFeatureSlot").Length - 1).IsEqualTo(1);
    }

    [Test]
    [Arguments("if (!flag) return; module.Use<Unused>();")]
    [Arguments("module?.Use<Unused>();")]
    public async Task AGuardedSelectionCannotActivateAnOptionalFeature(string guarded)
    {
        var selected = Generate("var module = services.CreateModule(); " + guarded);
        await Assert.That(selected.Diagnostics.Single().Id).IsEqualTo("ELSAGEN012");
        var alreadyPresent = Generate("var module = services.CreateModule(); module.Use<Unused>(); " + guarded);
        await Assert.That(alreadyPresent.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task OnlyCalledLocalFunctionsContributeTheirCapturedAndForwardedRoots()
    {
        var result = Generate("""
            var module = services.CreateModule();
            void Setup() => module.Use<Leaf>();
            static void SetupOn(IModule target) => target.Use<Root>();
            void Uncalled() => module.Use<Unused>();
            Setup();
            SetupOn(module);
            """);
        await Assert.That(result.Diagnostics).IsEmpty();
        var host = HostSource(result);
        await Assert.That(host).Contains("_root.Apply();");
        await Assert.That(host).Contains("_leaf.Apply();");
        await Assert.That(host.Contains("_unused", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ReassigningTheModuleLocalStartsAnIndependentPlan()
    {
        var result = Generate("""
            var module = services.CreateModule();
            module.Use<Root>();
            module = services.CreateModule();
            module.Use<Unused>();
            """);
        await Assert.That(result.Diagnostics).IsEmpty();
        var host = HostSource(result);
        await Assert.That(host.Split(" : FixedFeatureModule").Length - 1).IsEqualTo(2);
        await Assert.That(host.Split("FixedFeatureSlot _unused;").Length - 1).IsEqualTo(1);
        await Assert.That(host.Split("private readonly FixedFeatureSlot").Length - 1).IsEqualTo(3);
    }

    [Test]
    public async Task RuntimeReassignmentCannotTransferASelectionBetweenModules()
    {
        var result = Generate("""
            var first = services.CreateModule();
            var second = other.CreateModule();
            var target = first;
            if (flag) target = second;
            target.Configure<Unused>();
            """);
        await Assert.That(result.Diagnostics.Any(d => d.Id == "ELSAGEN012" && d.GetMessage().Contains("reassigned conditionally"))).IsTrue();
    }

    [Test]
    public async Task ObservingAnAliasWithAGenericApiDoesNotRequireAFixedModuleIdentity()
    {
        const string observe = """
            var module = services.CreateModule();
            IModule? observed = null;
            System.Action capture = () => observed = module;
            capture();
            _ = System.Linq.Enumerable.Repeat(observed, 1);
            _ = System.Collections.Generic.EqualityComparer<IModule>.Default.Equals(observed, module);
            Observe(observed);
            static void Observe(IModule? value) => System.GC.KeepAlive(value);
            """;
        var observed = Generate(observe);
        await Assert.That(observed.Diagnostics).IsEmpty();
        var membership = Generate(observe + "\n_ = observed!.HasFeature<Leaf>();");
        await Assert.That(membership.Diagnostics.Single().Id).IsEqualTo("ELSAGEN012");
    }

    [Test]
    public async Task GraphChangingConstructorsAreDiagnosedAndAnImmediateSafeFactoryCanOverrideTheDefault()
    {
        const string declarations = """
            public sealed class Composer : FeatureBase
            {
                public Composer(IModule m) : base(m) { m.Configure<Unused>(); }
                public Composer(IModule m, int custom) : base(m) { }
            }
            """;
        var byDefault = Generate("services.CreateModule().Use<Composer>();", declarations);
        await Assert.That(byDefault.Diagnostics.Any(d => d.GetMessage().Contains("during construction", StringComparison.Ordinal))).IsTrue();
        var safe = Generate("var module = services.CreateModule(); module.Configure(m => new Composer(m, 1));", declarations);
        await Assert.That(safe.Diagnostics).IsEmpty();
        var conditional = Generate("services.CreateModule().Configure(m => flag ? new Composer(m, 1) : new Composer(m));", declarations);
        await Assert.That(conditional.Diagnostics.Any(d => d.GetMessage().Contains("Feature factory invokes"))).IsTrue();
    }

    [Test]
    public async Task AConditionalLifecycleRequestCanConfigureAnIndependentlySelectedFeature()
    {
        const string declarations = """
            public sealed class Introducer(IModule m) : FeatureBase(m)
            {
                public bool Enabled { get; set; }
                public override void Configure() { if (Enabled) Module.Configure<Unused>(); }
            }
            """;
        var missing = Generate("services.CreateModule().Use<Introducer>();", declarations);
        await Assert.That(missing.Diagnostics.Single().GetMessage()).Contains("conditionally selects");
        var fixedPlan = Generate("services.CreateModule().Use<Introducer>().Use<Unused>();", declarations);
        await Assert.That(fixedPlan.Diagnostics).IsEmpty();
        await Assert.That(HostSource(fixedPlan)).Contains("_unused.Apply();");
    }

    [Test]
    public async Task CycleIsReportedOnlyWhenReachedFromConfiguredFeature()
    {
        const string declarations = """
            [DependsOn(typeof(B))] public sealed class A(IModule m) : FeatureBase(m);
            [DependsOn(typeof(A))] public sealed class B(IModule m) : FeatureBase(m);
            """;
        var unrelated = Generate("services.CreateModule().Configure<Leaf>();", declarations);
        await Assert.That(unrelated.Diagnostics).IsEmpty();
        var selected = Generate("services.CreateModule().Configure<A>();", declarations);
        var error = selected.Diagnostics.Single();
        await Assert.That(error.Id).IsEqualTo("ELSAGEN011");
        await Assert.That(error.GetMessage()).Contains("A -> B -> A");
    }

    [Test]
    public async Task AStrongConditionalDependencyIsFilteredUnderLegacyAndRejectedUnderValidatedPolicy()
    {
        const string declarations = """
            [DependencyOf(typeof(Unused))] public sealed class Conditional(IModule m) : FeatureBase(m);
            [DependsOn(typeof(Conditional))] public sealed class Selected(IModule m) : FeatureBase(m);
            """;
        var legacy = Generate("services.CreateModule().Configure<Selected>();", declarations);
        await Assert.That(legacy.Diagnostics).IsEmpty();
        await Assert.That(HostSource(legacy).Split("private readonly FixedFeatureSlot").Length - 1).IsEqualTo(1);
        var validated = Generate("services.CreateModule(Elsa.Features.Models.CompositionPolicy.ValidatedV1).Configure<Selected>();", declarations);
        await Assert.That(validated.Diagnostics.Single().GetMessage()).Contains("excluded");
    }

    [Test]
    public async Task SdkEntriesShareACollectionPlanAndResolveACallbackMethodGroup()
    {
        const string sdk = """
            using System;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            namespace Elsa.Features
            {
                public sealed class AppFeature(IModule m) : FeatureBase(m);
                public sealed class Management(IModule m) : FeatureBase(m);
                public sealed class Database(IModule m) : FeatureBase(m);
            }
            namespace Elsa.Extensions
            {
                public static class ModuleExtensions
                {
                    public static IModule UseManagement(this IModule module, Action<Elsa.Features.Management>? configure = null)
                    {
                        module.Configure<Elsa.Features.Management>(management => configure?.Invoke(management));
                        return module;
                    }
                    public static void UseDatabase(this Elsa.Features.Management management) => management.Module.Use<Elsa.Features.Database>();
                    public static IModule AddElsa(this IServiceCollection services, Action<IModule>? configure = null) => throw new Exception();
                    public static IModule ConfigureElsa(this IServiceCollection services, Action<IModule>? configure = null) => throw new Exception();
                }
            }
            """;
        var assembly = CompilerTest.CreateCompilation(("Sdk.cs", sdk)).WithAssemblyName("Elsa").AddReferences(References());
        CompilerTest.CreateDriver(new ElsaFeatureGenerator()).Run(assembly, out var sdkOutput);
        var host = Generate("""
            services.ConfigureElsa(m => m.Use<Leaf>());
            services.AddElsa(SelectRoot);
            other.AddElsa(m => m.Use<Unused>());
            static void SelectRoot(IModule module) => module.Use<Root>();
            """, sdk: sdkOutput.ToMetadataReference());
        await Assert.That(host.Diagnostics).IsEmpty();
        var source = HostSource(host);
        await Assert.That(source).Contains("ConfigureElsa(configure, CreatePlan0)");
        await Assert.That(source).Contains("AddElsa(configure, CreatePlan0)");
        await Assert.That(source).Contains("AddElsa(configure, CreatePlan1)");
        await Assert.That(source.Split("FixedFeatureSlot _appFeature;").Length - 1).IsEqualTo(2);
        await Assert.That(source.Split("FixedFeatureSlot _root;").Length - 1).IsEqualTo(1);
        await Assert.That(source.Split("FixedFeatureSlot _unused;").Length - 1).IsEqualTo(1);

        var forwarded = Generate("services.AddElsa(m => m.UseManagement(management => management.UseDatabase()));",
            sdk: sdkOutput.ToMetadataReference());
        await Assert.That(forwarded.Diagnostics).IsEmpty();
        await Assert.That(HostSource(forwarded)).Contains("_database.Apply();");

        var returned = Generate("""
            var module = services.ConfigureElsa();
            module.Use<Leaf>();
            other.ConfigureElsa().Use<Unused>();
            services.AddElsa();
            other.AddElsa();
            """, sdk: sdkOutput.ToMetadataReference());
        await Assert.That(returned.Diagnostics).IsEmpty();
        var returnedSource = HostSource(returned);
        await Assert.That(returnedSource).Contains("_leaf.Apply();");
        await Assert.That(returnedSource).Contains("_unused.Apply();");
        await Assert.That(returnedSource.Split(" : FixedFeatureModule").Length - 1).IsEqualTo(2);

        var mutable = Generate("""
            var collection = services;
            collection.AddElsa(m => m.Use<Leaf>());
            collection = other;
            collection.AddElsa(m => m.Use<Unused>());
            """, sdk: sdkOutput.ToMetadataReference());
        await Assert.That(mutable.Diagnostics).IsEmpty();
        await Assert.That(HostSource(mutable).Split(" : FixedFeatureModule").Length - 1).IsEqualTo(2);

        var branch = Generate("""
            services.ConfigureElsa(m => m.Use<Leaf>());
            if (flag) services.AddElsa(m => m.Use<Unused>());
            """, sdk: sdkOutput.ToMetadataReference());
        await Assert.That(branch.Diagnostics.Single().Id).IsEqualTo("ELSAGEN012");

        var conditionalCollection = Generate("""
            var collection = services;
            if (flag) collection = other;
            collection.ConfigureElsa(m => m.Use<Unused>());
            """, sdk: sdkOutput.ToMetadataReference());
        await Assert.That(conditionalCollection.Diagnostics.Single().GetMessage()).Contains("reassigned conditionally");

        var reassignedCallback = Generate("""
            System.Action<IModule> configure = m => m.Use<Leaf>();
            if (flag) configure = m => m.Use<Unused>();
            services.AddElsa(configure);
            """, sdk: sdkOutput.ToMetadataReference());
        await Assert.That(reassignedCallback.Diagnostics.Single().GetMessage()).Contains("reassigned");
    }

    private static string HostSource(GeneratorDriverRunResult result) =>
        result.Results.Single().GeneratedSources.Single().SourceText.ToString();

    private static GeneratorDriverRunResult Generate(string selection, string extra = "", MetadataReference? sdk = null)
    {
        var source = $$"""
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Attributes;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            [DependsOn(typeof(Leaf))] public sealed class Root(IModule m) : FeatureBase(m);
            public sealed class Unused(IModule m) : FeatureBase(m);
            {{extra}}
            public static class Application
            {
                public static void Start(IServiceCollection services, IServiceCollection other, bool flag)
                {
                    {{selection}}
                }
            }
            """;
        var input = CompilerTest.CreateCompilation(("App.cs", source)).AddReferences(References());
        if (sdk != null) input = input.AddReferences(sdk);
        return CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(input, out _).GetRunResult();
    }

    private static MetadataReference[] References() =>
    [
        MetadataReference.CreateFromFile(typeof(IModule).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(IHostedService).Assembly.Location)
    ];
}
