using Elsa.CodeGeneration.Generators;
using Elsa.Features.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    public async Task SourceHelpersAssignEffectsToTheSpecifiedModuleParameterOrFeatureReceiver()
    {
        const string helpers = """
            public static class Helpers
            {
                public static void Install(IModule first, IModule second) { first.Use<Leaf>(); second.Use<Unused>(); }
                public static IModule Forward(IModule first, IModule second) => second;
            }
            public sealed class OwnerFeature(IModule m) : FeatureBase(m)
            {
                public void Install(IModule other) { Module.Use<Root>(); other.Use<Unused>(); }
            }
            """;
        var simple = Generate("""
            var first = services.CreateModule();
            var second = other.CreateModule();
            Helpers.Install(first, second);
            """, helpers);
        await Assert.That(simple.Diagnostics).IsEmpty();
        await Assert.That(Slots(simple, "Plan0").SequenceEqual(new[] { "_leaf" })).IsTrue();
        await Assert.That(Slots(simple, "Plan1").SequenceEqual(new[] { "_unused" })).IsTrue();

        var returned = Generate("""
            var first = services.CreateModule();
            var second = other.CreateModule();
            Helpers.Forward(first, second).Use<Leaf>();
            """, helpers);
        await Assert.That(returned.Diagnostics).IsEmpty();
        await Assert.That(Slots(returned, "Plan0")).IsEmpty();
        await Assert.That(Slots(returned, "Plan1").SequenceEqual(new[] { "_leaf" })).IsTrue();

        var result = Generate("""
            var first = services.CreateModule();
            var second = other.CreateModule();
            Helpers.Install(second: second, first: first);
            var feature = first.Configure<OwnerFeature>();
            feature.Install(second);
            """, helpers);
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(Slots(result, "Plan0").Order().SequenceEqual(new[] { "_leaf", "_ownerFeature", "_root" })).IsTrue();
        await Assert.That(Slots(result, "Plan1").SequenceEqual(new[] { "_unused" })).IsTrue();
    }

    [Test]
    public async Task SelectionsThroughSingleArgumentForwardersFollowTheirReturnedModule()
    {
        const string helpers = """
            public static class Helpers
            {
                public static IModule Forward(IModule m) => m;
                public static IModule Alias(IModule m) { var same = m; m.Configure<Unused>(); return same; }
            }
            """;
        var result = Generate("""
            var first = services.CreateModule();
            var second = other.CreateModule();
            var alias = first;
            Helpers.Forward(alias).Use<Leaf>();
            var returned = Helpers.Alias(second);
            returned.Use<Root>();
            """, helpers);
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(Slots(result, "Plan0").SequenceEqual(new[] { "_leaf" })).IsTrue();
        await Assert.That(Slots(result, "Plan1").Order().SequenceEqual(new[] { "_leaf", "_root", "_unused" })).IsTrue();
    }

    [Test]
    public async Task StaticWrapperArgumentsAssociateInlineAndAssignedFreshRoots()
    {
        const string helpers = "public static class H { public static IModule Forward(IModule m) => m; }";
        var result = Generate("""
            H.Forward(services.CreateModule()).Use<Leaf>();
            var forwarded = H.Forward(other.CreateModule());
            forwarded.Use<Unused>();
            """, helpers);
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(Slots(result, "Plan0").SequenceEqual(new[] { "_leaf" })).IsTrue();
        await Assert.That(Slots(result, "Plan1").SequenceEqual(new[] { "_unused" })).IsTrue();
    }

    [Test]
    [Arguments("H.Fresh(original, other)")]
    [Arguments("H.Reassign(original, other)")]
    [Arguments("original.Another()")]
    public async Task HelpersReturningAnotherModuleDoNotGrowTheirInputPlan(string helper)
    {
        const string helpers = """
            public static class H
            {
                public static IModule Fresh(IModule ignored, IServiceCollection services) => services.CreateModule();
                public static IModule Reassign(IModule m, IServiceCollection services) { m = services.CreateModule(); return m; }
                internal static IModule External = null!;
                public static IModule Another(this IModule m) => External;
            }
            """;
        var source = "var original = services.CreateModule(); var result = " + helper + ";";
        var observation = Generate(source + " System.GC.KeepAlive(result);", helpers);
        await Assert.That(observation.Diagnostics).IsEmpty();
        var configured = Generate(source + " result.Use<Leaf>();", helpers);
        var error = configured.Diagnostics.Single();
        await Assert.That(error.Id).IsEqualTo("ELSAGEN012");
        await Assert.That(error.GetMessage()).Contains("Cannot establish which module");
        await Assert.That(HostSource(configured).Contains("_leaf", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task FeatureValuedSetterSeparatesItsReceiverFromTheAssignedFeature()
    {
        const string declaration = """
            public sealed class PairFeature(IModule module) : FeatureBase(module)
            {
                public PairFeature Other { set { Module.Use<Leaf>(); value.Module.Use<Unused>(); } }
            }
            """;
        var result = Generate("""
            var first = services.CreateModule();
            var second = other.CreateModule();
            var left = first.Configure<PairFeature>();
            var right = second.Configure<PairFeature>();
            left.Other = right;
            """, declaration);
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(Slots(result, "Plan0").Order().SequenceEqual(new[] { "_leaf", "_pairFeature" })).IsTrue();
        await Assert.That(Slots(result, "Plan1").Order().SequenceEqual(new[] { "_pairFeature", "_unused" })).IsTrue();
    }

    [Test]
    public async Task PackageHelpersCanShareOneOwnerAndDiagnoseIndependentInputsAtTheCall()
    {
        const string packageSource = """
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class A(IModule m) : FeatureBase(m);
            public sealed class B(IModule m) : FeatureBase(m);
            public static class PackageHelpers
            {
                public static IModule Forward(IModule m) { var alias = m; return alias; }
                public static IModule ForwardAgain(IModule m) => Forward(m);
                internal static IModule Other = null!;
                public static IModule External(this IModule m) => Other;
                public static void Install(this IModule first, IModule second) { first.Use<A>(); second.Use<B>(); }
                public static bool Observe(IModule first, IModule second) => object.ReferenceEquals(first, second);
            }
            """;
        var package = CompilerTest.CreateCompilation(("Package.cs", packageSource)).WithAssemblyName("TwoOwners").AddReferences(References());
        CompilerTest.CreateDriver(new ElsaFeatureGenerator()).Run(package, out var output);
        using var assembly = new MemoryStream();
        var compiled = output.Emit(assembly);
        await Assert.That(compiled.Success).IsTrue().Because(string.Join("\n", compiled.Diagnostics));
        var reference = MetadataReference.CreateFromImage(assembly.ToArray());

        var shared = Generate("""
            var first = services.CreateModule();
            var alias = first;
            var second = other.CreateModule();
            _ = PackageHelpers.Observe(first, second);
            first.Install(alias);
            """, sdk: reference);
        await Assert.That(shared.Diagnostics).IsEmpty();
        await Assert.That(Slots(shared, "Plan0").Order().SequenceEqual(new[] { "_a", "_b" })).IsTrue();
        await Assert.That(Slots(shared, "Plan1")).IsEmpty();

        var chain = Generate("""
            var first = services.CreateModule();
            var second = other.CreateModule();
            PackageHelpers.ForwardAgain(first).Use<Leaf>();
            """, sdk: reference);
        await Assert.That(chain.Diagnostics).IsEmpty();
        await Assert.That(Slots(chain, "Plan0").SequenceEqual(new[] { "_leaf" })).IsTrue();
        await Assert.That(Slots(chain, "Plan1")).IsEmpty();
        var external = Generate("var supplied = services.CreateModule(); supplied.External().Use<Leaf>();", sdk: reference);
        await Assert.That(external.Diagnostics.Single().Id).IsEqualTo("ELSAGEN012");
        await Assert.That(Slots(external, "Plan0")).IsEmpty();

        const string splitCall = "first.Install(second)";
        var split = Generate("var first = services.CreateModule(); var second = other.CreateModule(); " + splitCall + ";", sdk: reference);
        var diagnostic = split.Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN012");
        await Assert.That(diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan)).IsEqualTo(splitCall);
        await Assert.That(diagnostic.GetMessage()).Contains("separate helper call per module");
    }

    [Test]
    [Arguments("var module = (services.CreateModule()); module.Use<Leaf>(); module.Use<Unused>();")]
    [Arguments("var module = services.CreateModule(); module = module.Use<Leaf>(); module.Use<Unused>();")]
    public async Task WrappedRootsAndFluentSelfAssignmentPreserveModuleOwnership(string selection)
    {
        var result = Generate(selection);
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(Slots(result, "Plan0").Order().SequenceEqual(new[] { "_leaf", "_unused" })).IsTrue();
    }

    [Test]
    [Arguments("(IModule)services.CreateModule()")]
    [Arguments("services.CreateModule()!")]
    [Arguments("(((IModule)(((services.CreateModule())!))))!")]
    [Arguments("(services.CreateModule() as IModule)!")]
    public async Task TransparentWrappersPreserveTheAssignedRootsConsumers(string root)
    {
        var result = Generate($$"""
            var first = {{root}};
            var second = other.CreateModule();
            first.Use<Leaf>();
            second.Use<Unused>();
            first.Apply();
            """);
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(Slots(result, "Plan0").SequenceEqual(new[] { "_leaf" })).IsTrue();
        await Assert.That(Slots(result, "Plan1").SequenceEqual(new[] { "_unused" })).IsTrue();
    }

    [Test]
    public async Task WidenedModuleRemainsOwnedThroughReferenceConversions()
    {
        var result = Generate("""
            object boxed = services.CreateModule();
            var forwarded = boxed;
            ((IModule)forwarded).Use<Leaf>();
            ((IModule)(object)other.CreateModule()).Use<Unused>();
            """);
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(Slots(result, "Plan0").SequenceEqual(new[] { "_leaf" })).IsTrue();
        await Assert.That(Slots(result, "Plan1").SequenceEqual(new[] { "_unused" })).IsTrue();
    }

    [Test]
    public async Task ExplicitFeatureModuleCannotSilentlyRedirectASelection()
    {
        const string declarations = """
            public sealed class Redirect(IModule m) : FeatureBase(m), IFeature
            {
                public IModule Other { get; set; } = null!;
                IModule IFeature.Module => Other;
            }
            """;
        var redirected = Generate("""
            var first = services.CreateModule();
            var second = other.CreateModule();
            var feature = first.Configure<Redirect>();
            feature.Other = second;
            ((IFeature)feature).Module.Use<Leaf>();
            """, declarations);
        await Assert.That(redirected.Diagnostics.Single().Id).IsEqualTo("ELSAGEN012");
        await Assert.That(HostSource(redirected).Contains("_leaf", StringComparison.Ordinal)).IsFalse();

        var inherited = Generate("var feature = services.CreateModule().Configure<Unused>(); ((IFeature)feature).Module.Use<Leaf>();");
        await Assert.That(inherited.Diagnostics).IsEmpty();
        await Assert.That(Slots(inherited, "Plan0").Order().SequenceEqual(new[] { "_leaf", "_unused" })).IsTrue();
    }

    [Test]
    public async Task AmbiguousObjectAliasesAreDiagnosedWhenUsedAsModules()
    {
        const string setup = """
            var first = services.CreateModule();
            var second = other.CreateModule();
            object target = first;
            if (flag) target = second;
            var alias = target;
            """;
        var used = Generate(setup + " ((IModule)alias).Use<Leaf>();");
        await Assert.That(used.Diagnostics.Single().Id).IsEqualTo("ELSAGEN012");
        var observed = Generate(setup + " System.GC.KeepAlive(alias);");
        await Assert.That(observed.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task DeconstructionReadsBothOwnersBeforeUpdatingEitherLocal()
    {
        var result = Generate("""
            var first = services.CreateModule();
            var second = other.CreateModule();
            (first, second) = (second, first);
            first.Use<Unused>();
            second.Use<Leaf>();
            """);
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(Slots(result, "Plan0").SequenceEqual(new[] { "_leaf" })).IsTrue();
        await Assert.That(Slots(result, "Plan1").SequenceEqual(new[] { "_unused" })).IsTrue();
    }

    [Test]
    [Arguments("Helpers.Redirect(ref first, ref first, second).Use<Leaf>();")]
    [Arguments("var feature = first.Configure<Owner>(); feature.Other = second; feature.Other.Use<Leaf>();")]
    public async Task UnprovenModuleOwnersAreDiagnosedAtTheRead(string body)
    {
        const string extra = """
            public static class Helpers
            {
                public static IModule Redirect(ref IModule observed, ref IModule changed, IModule replacement)
                {
                    changed = replacement;
                    return observed;
                }
            }
            public sealed class Owner(IModule module) : FeatureBase(module)
            {
                public IModule Other { get; set; } = null!;
            }
            """;
        var result = Generate("var first = services.CreateModule(); var second = other.CreateModule(); " + body, extra);
        await Assert.That(result.Diagnostics.Single().Id).IsEqualTo("ELSAGEN012");
        await Assert.That(result.Diagnostics.Single().GetMessage()).Contains("Cannot establish which module");
        await Assert.That(HostSource(result).Contains("_leaf", StringComparison.Ordinal)).IsFalse();
    }

    private static string[] Slots(GeneratorDriverRunResult result, string plan) =>
        result.GeneratedTrees.Single().GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == plan)
            .Members.OfType<FieldDeclarationSyntax>().SelectMany(f => f.Declaration.Variables).Select(v => v.Identifier.ValueText).ToArray();

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
    public async Task NullDelegateSkipsItsFeatureSelectingArgument()
    {
        var result = Generate("""
            var module = services.CreateModule();
            System.Action<object>? observe = null;
            observe?.Invoke(module.Configure<Leaf>());
            """);
        await Assert.That(result.Diagnostics).IsEmpty().Because(string.Join("\n", result.Diagnostics));
        await Assert.That(Slots(result, "Plan0")).IsEmpty();
    }

    [Test]
    [Arguments("install();")]
    [Arguments("install.Invoke();")]
    public async Task CallingACapturedDelegateSelectsFeaturesOnlyForItsModule(string invocation)
    {
        var result = Generate($$"""
            var first = services.CreateModule();
            var second = other.CreateModule();
            System.Action install = () => second.Use<Leaf>();
            System.Action deferred = () => first.Use<Unused>();
            {{invocation}}
            """);
        await Assert.That(result.Diagnostics).IsEmpty().Because(string.Join("\n", result.Diagnostics));
        await Assert.That(Slots(result, "Plan0")).IsEmpty();
        await Assert.That(Slots(result, "Plan1").SequenceEqual(["_leaf"])).IsTrue();
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
    [Arguments("module.Use<ExplicitOnly>(); module.Configure(m => new ExplicitOnly(m, 1));", true)]
    [Arguments("module.Apply(); module.Configure(m => new ExplicitOnly(m, 1));", true)]
    [Arguments("module.Configure<ExplicitOnly>(c => { module.Configure(m => new ExplicitOnly(m, 1)); });", true)]
    [Arguments("module.Configure(m => new ExplicitOnly(m, 1), c => module.Use<ExplicitOnly>());", false)]
    [Arguments("if (flag) module.Use<ExplicitOnly>(); module.Configure(m => new ExplicitOnly(m, 1));", true)]
    [Arguments("module.Configure(m => new ExplicitOnly(m, 1)); module.Use<ExplicitOnly>(); module.Apply();", false)]
    public async Task ExplicitFactoryMustPrecedeFirstResolution(string calls, bool missingFactory)
    {
        var result = Generate("var module = services.CreateModule(); " + calls,
            "public sealed class ExplicitOnly(IModule module, int value) : FeatureBase(module);");
        if (missingFactory)
        {
            var diagnostic = result.Diagnostics.Single();
            await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN011");
            await Assert.That(diagnostic.GetMessage()).Contains("requires an explicit factory");
        }
        else
            await Assert.That(result.Diagnostics).IsEmpty();
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
    [Arguments("LegacyV1")]
    [Arguments("ValidatedV1")]
    public async Task AnExecutedRegistrationRequiresItsPresenceConditions(string policy)
    {
        const string declarations = """
            [DependencyOf(typeof(Unused))] public sealed class Gated(IModule m) : FeatureBase(m);
            public static class Extensions
            {
                public static IModule UseGated(this IModule module) => module.Use<Gated>();
            }
            public sealed class Contributor(IModule m) : FeatureBase(m)
            {
                public override void Apply() => Module.Configure<Gated>();
            }
            """;
        var module = $"var module = services.CreateModule(Elsa.Features.Models.CompositionPolicy.{policy}); ";
        foreach (var call in new[] { "module.Configure<Gated>();", "module.UseGated();", "module.Use<Contributor>();" })
        {
            var absent = Generate(module + call, declarations);
            var diagnostic = absent.Diagnostics.Single();
            await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN011");
            await Assert.That(diagnostic.GetMessage()).Contains("Gated");
            await Assert.That(diagnostic.GetMessage()).Contains("DependencyOf");
            await Assert.That(diagnostic.GetMessage()).Contains("Unused");
            if (call.Contains("Contributor", StringComparison.Ordinal))
                await Assert.That(diagnostic.GetMessage()).Contains("Contributor");
            else
                await Assert.That(diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan)).IsEqualTo(call.TrimEnd(';'));

            var admitted = Generate(module + call + " module.Use<Unused>();", declarations);
            await Assert.That(admitted.Diagnostics).IsEmpty();
            await Assert.That(HostSource(admitted)).Contains("_gated.Configure();");
        }
    }

    [Test]
    public async Task CapturedLocalUsesItsValueAtTheLocalFunctionCall()
    {
        var result = Generate("""
            var first = services.CreateModule();
            var second = other.CreateModule();
            var target = first;
            void Install() { var captured = target; captured.Use<Leaf>(); }
            target = second;
            Install();
            """);
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(Slots(result, "Plan0")).IsEmpty();
        await Assert.That(Slots(result, "Plan1").SequenceEqual(new[] { "_leaf" })).IsTrue();
    }

    [Test]
    [Arguments("services.AddElsa(m => m.Apply());", true)]
    [Arguments("services.ConfigureElsa(m => Helpers.Finish(m)); services.AddElsa();", true)]
    [Arguments("services.ConfigureElsa().Apply();", false)]
    public async Task SdkCallbacksCannotApplyTheirOwnModuleRecursively(string calls, bool recursive)
    {
        var result = Generate(calls, "public static class Helpers { public static void Finish(IModule m) => m.Apply(); }", SdkReference());
        if (recursive)
        {
            var diagnostic = result.Diagnostics.Single();
            await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN011");
            await Assert.That(diagnostic.GetMessage()).Contains("recursive Apply");
        }
        else await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task LifecycleCannotApplyItsOwnModuleRecursively()
    {
        var result = Generate("services.CreateModule().Use<Recursive>();",
            "public sealed class Recursive(IModule m) : FeatureBase(m) { public override void Configure() => Module.Apply(); }");
        await Assert.That(result.Diagnostics.Single().GetMessage()).Contains("apply its own module recursively");
    }

    private static MetadataReference SdkReference()
    {
        const string sdk = """
            using System;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            namespace Elsa.Features
            {
                public sealed class AppFeature(IModule m) : FeatureBase(m)
                {
                    public Action<IModule>? Configurator { get; set; }
                    public override void Configure() => Configurator?.Invoke(Module);
                }
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
        return sdkOutput.ToMetadataReference();
    }

    [Test]
    public async Task GeneratedSdkCallbacksArePlannedAtTheHostEntry()
    {
        var sdk = SdkReference();
        var known = Generate("services.AddElsa(m => m.Use<Leaf>());", sdk: sdk);
        await Assert.That(known.Diagnostics).IsEmpty();
        await Assert.That(Slots(known, "Plan0").Order().SequenceEqual(new[] { "_appFeature", "_leaf" })).IsTrue();

        var bypass = Generate("""
            services.AddElsa(m => m.Configure<Elsa.Features.AppFeature>(app => app.Configurator = other => other.Use<Leaf>()));
            """, sdk: sdk);
        await Assert.That(bypass.Diagnostics.Single().GetMessage()).Contains("through AddElsa or ConfigureElsa");

        var unknown = Generate("services.CreateModule().Use<Other>();", """
            public sealed class Other(IModule m) : FeatureBase(m)
            {
                public System.Action<IModule>? Configurator { get; set; }
                public override void Configure() => Configurator?.Invoke(Module);
            }
            """, sdk);
        await Assert.That(unknown.Diagnostics.Single().GetMessage()).Contains("cannot inspect feature configuration callback 'Configurator'");
    }

    [Test]
    [Arguments("var module = services.ConfigureElsa(m => m.Configure(x => new ExplicitOnly(x, 1))); module.Use<ExplicitOnly>(); services.AddElsa();", true)]
    [Arguments("var module = services.ConfigureElsa(m => m.Use<ExplicitOnly>()); module.Configure(x => new ExplicitOnly(x, 1)); services.AddElsa();", false)]
    [Arguments("services.AddElsa(); services.ConfigureElsa(m => m.Configure(x => new ExplicitOnly(x, 1)));", true)]
    public async Task QueuedFactoriesRunDuringTheFirstApply(string calls, bool missingFactory)
    {
        var result = Generate(calls, "public sealed class ExplicitOnly(IModule module, int value) : FeatureBase(module);", SdkReference());
        if (missingFactory)
            await Assert.That(result.Diagnostics.Single().Id).IsEqualTo("ELSAGEN011");
        else
            await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    [Arguments("(IServiceCollection)services")]
    [Arguments("((IServiceCollection)services)")]
    public async Task SdkEntriesShareThePlanThroughACastAlias(string cast)
    {
        var result = Generate($$"""
            var alias = {{cast}};
            alias.ConfigureElsa(m => m.Use<Leaf>());
            services.AddElsa(m => m.Use<Root>());
            """, sdk: SdkReference());
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(HostSource(result).Split(" : FixedFeatureModule").Length - 1).IsEqualTo(1);
        await Assert.That(Slots(result, "Plan0").Order().SequenceEqual(new[] { "_appFeature", "_leaf", "_root" })).IsTrue();
    }

    [Test]
    [Arguments("public IServiceCollection Services { get; set; } = new ServiceCollection();")]
    [Arguments("public IServiceCollection Services = new ServiceCollection();")]
    public async Task SdkEntriesSeparateReplacedCollectionMembers(string member)
    {
        var result = Generate("""
            var holder = new Holder();
            holder.Services.AddElsa(m => m.Use<Unused>());
            holder.Services = new ServiceCollection();
            holder.Services.AddElsa(m => m.Use<Leaf>());
            """, "public sealed class Holder { " + member + " }", SdkReference());
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(Slots(result, "Plan0").Order().SequenceEqual(new[] { "_appFeature", "_unused" })).IsTrue();
        await Assert.That(Slots(result, "Plan1").Order().SequenceEqual(new[] { "_appFeature", "_leaf" })).IsTrue();
    }

    [Test]
    public async Task SdkCollectionOwnershipFollowsInvokedMethodGroups()
    {
        const string setup = "public static class Helpers { public static void Setup(IServiceCollection target) => target.ConfigureElsa(m => m.Use<Unused>()); }";
        var sdk = SdkReference();
        const string callback = "System.Action<IServiceCollection> setup = Helpers.Setup; var alias = setup; ";
        var conflict = Generate(callback + "alias(services); services.AddElsa(m => m.Use<Leaf>());", setup, sdk);
        await Assert.That(conflict.Diagnostics.Single().Id).IsEqualTo("ELSAGEN012");
        var independent = Generate(callback + "alias(other); services.AddElsa(m => m.Use<Leaf>());", setup, sdk);
        await Assert.That(independent.Diagnostics).IsEmpty();
        var shared = Generate(callback + "alias(services); services.AddElsa(m => m.Use<Unused>());", setup, sdk);
        await Assert.That(shared.Diagnostics).IsEmpty();
        await Assert.That(HostSource(shared).Split(" : FixedFeatureModule").Length - 1).IsEqualTo(1);
    }

    [Test]
    public async Task IncompatibleSdkPlansAreDiagnosedWhenTheCollectionIsForwardedToAHelper()
    {
        const string helpers = """
            public static class Helpers
            {
                public static void Setup(IServiceCollection target) => target.ConfigureElsa(m => m.Use<Unused>());
                public static void Forward(IServiceCollection target) => Setup(target);
            }
            """;
        var sdk = SdkReference();
        var conflict = Generate("var collection = services; Helpers.Forward(collection); services.AddElsa(m => m.Use<Leaf>());", helpers, sdk);
        await Assert.That(conflict.Diagnostics.Single().Id).IsEqualTo("ELSAGEN012");
        await Assert.That(conflict.Diagnostics.Single().GetMessage()).Contains("Forward");
        var independent = Generate("Helpers.Forward(other); services.AddElsa(m => m.Use<Leaf>());", helpers, sdk);
        await Assert.That(independent.Diagnostics).IsEmpty();
        var shared = Generate("Helpers.Forward(services); services.AddElsa(m => m.Use<Unused>());", helpers, sdk);
        await Assert.That(shared.Diagnostics).IsEmpty();
        await Assert.That(HostSource(shared).Split(" : FixedFeatureModule").Length - 1).IsEqualTo(1);
    }

    [Test]
    public async Task SdkEntriesShareACollectionPlanAndResolveACallbackMethodGroup()
    {
        var sdk = SdkReference();
        var host = Generate("""
            services.ConfigureElsa(m => m.Use<Leaf>());
            services.AddElsa(SelectRoot);
            other.AddElsa(m => m.Use<Unused>());
            static void SelectRoot(IModule module) => module.Use<Root>();
            """, sdk: sdk);
        await Assert.That(host.Diagnostics).IsEmpty();
        var source = HostSource(host);
        await Assert.That(source).Contains("ConfigureElsa(configure, CreatePlan0)");
        await Assert.That(source).Contains("AddElsa(configure, CreatePlan0)");
        await Assert.That(source).Contains("AddElsa(configure, CreatePlan1)");
        await Assert.That(source.Split("FixedFeatureSlot _appFeature;").Length - 1).IsEqualTo(2);
        await Assert.That(source.Split("FixedFeatureSlot _root;").Length - 1).IsEqualTo(1);
        await Assert.That(source.Split("FixedFeatureSlot _unused;").Length - 1).IsEqualTo(1);

        var forwarded = Generate("services.AddElsa(m => m.UseManagement(management => management.UseDatabase()));",
            sdk: sdk);
        await Assert.That(forwarded.Diagnostics).IsEmpty();
        await Assert.That(HostSource(forwarded)).Contains("_database.Apply();");

        var returned = Generate("""
            var module = services.ConfigureElsa();
            module.Use<Leaf>();
            other.ConfigureElsa().Use<Unused>();
            services.AddElsa();
            other.AddElsa();
            """, sdk: sdk);
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
            """, sdk: sdk);
        await Assert.That(mutable.Diagnostics).IsEmpty();
        await Assert.That(HostSource(mutable).Split(" : FixedFeatureModule").Length - 1).IsEqualTo(2);

        foreach (var entry in new[] { "services.Replace().AddElsa();", "var replacement = services.Replace(); replacement.AddElsa();" })
        {
            var opaque = Generate("services.ConfigureElsa(m => m.Use<Leaf>()); " + entry, """
                public static class CollectionExtensions
                {
                    public static IServiceCollection Replace(this IServiceCollection services) => new ServiceCollection();
                }
                """, sdk);
            var diagnostic = opaque.Diagnostics.Single();
            await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN012");
            await Assert.That(diagnostic.GetMessage()).Contains("Replace");
        }

        const string factory = "public static class Factory { public static IServiceCollection GetServices() => new ServiceCollection(); }";
        var distinct = Generate("other.ConfigureElsa(m => m.Use<Leaf>()); Factory.GetServices().AddElsa(); Factory.GetServices().AddElsa();", factory, sdk);
        await Assert.That(distinct.Diagnostics).IsEmpty();
        var captured = Generate("var saved = Factory.GetServices(); saved.ConfigureElsa(m => m.Use<Leaf>()); saved.AddElsa();", factory, sdk);
        await Assert.That(captured.Diagnostics).IsEmpty();
        var repeated = Generate("Factory.GetServices().ConfigureElsa(m => m.Use<Leaf>()); Factory.GetServices().AddElsa();", factory, sdk);
        await Assert.That(repeated.Diagnostics.Any(d => d.Id == "ELSAGEN012" && d.GetMessage().Contains("GetServices"))).IsTrue();

        var branch = Generate("""
            services.ConfigureElsa(m => m.Use<Leaf>());
            if (flag) services.AddElsa(m => m.Use<Unused>());
            """, sdk: sdk);
        await Assert.That(branch.Diagnostics.Single().Id).IsEqualTo("ELSAGEN012");

        var conditionalCollection = Generate("""
            var collection = services;
            if (flag) collection = other;
            collection.ConfigureElsa(m => m.Use<Unused>());
            """, sdk: sdk);
        await Assert.That(conditionalCollection.Diagnostics.Single().GetMessage()).Contains("reassigned conditionally");

        var reassignedCallback = Generate("""
            System.Action<IModule> configure = m => m.Use<Leaf>();
            if (flag) configure = m => m.Use<Unused>();
            services.AddElsa(configure);
            """, sdk: sdk);
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
