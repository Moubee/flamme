using System.Reflection;
using System.Runtime.Loader;
using Elsa.CodeGeneration.Generators;
using Elsa.Features.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.CodeGeneration.UnitTests;

public class FeatureRegistrationTests
{
    private const string Dependency = """
        using Elsa.Features.Abstractions;
        using Elsa.Features.Services;
        namespace Infrastructure;
        public sealed class DatabaseFeature(IModule module) : FeatureBase(module)
        {
            public override void Apply() => Module.Properties["steps"] += "database;";
        }
        """;

    private const string Producer = """
        using Elsa.Extensions;
        using Elsa.Features.Abstractions;
        using Elsa.Features.Attributes;
        using Elsa.Features.Contracts;
        using Elsa.Features.Implementations;
        using Elsa.Features.Services;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using System;
        using System.ComponentModel;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Infrastructure;
        namespace Application;

        [DependsOn(typeof(UnselectedFeature), Type = typeof(DatabaseFeature))]
        [DisplayName("Host \"plugin\""), Description("Inherited description")]
        internal abstract class PluginBase(IModule module) : FeatureBase(module);

        [DependencyOf(typeof(UnselectedFeature), Type = typeof(DatabaseFeature))]
        internal sealed class PluginFeature : PluginBase
        {
            public PluginFeature(IModule module) : base(module) => module.Properties["steps"] = "module;";
            public PluginFeature(Module module) : base(module) => throw new Exception("wrong overload");
            public int Priority { get; set; }
            public override void ConfigureHostedServices()
            {
                Module.ConfigureHostedService(priority: Priority, hostedServiceType: typeof(Second));
                ConfigureHostedService<First>(-1);
            }
            public override void Apply() => Module.Properties["steps"] += "plugin;";
        }

        internal sealed class CustomFeature(IModule module) : FeatureBase(module)
        {
            public required string Value { get; init; }
            public override void Apply() => Module.Properties["custom"] = Value;
        }
        internal sealed class UnselectedFeature : IFeature
        {
            public UnselectedFeature(IModule module) => throw new Exception("unselected");
            public IModule Module => throw new NotSupportedException();
            public void Configure() { }
            public void ConfigureHostedServices() { }
            public void Apply() { }
        }
        internal sealed class GenericFeature<T>(IModule module) : FeatureBase(module);
        [DependencyOf(typeof(UnselectedFeature))]
        internal sealed class ExcludedFeature(IModule module) : FeatureBase(module)
        {
            public override void Apply() => throw new Exception("conditional feature was admitted");
        }
        internal class First : IHostedService
        {
            public Task StartAsync(CancellationToken token) => Task.CompletedTask;
            public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        }
        internal sealed class Second : First;

        public static class Scenario
        {
            public static string Run(FixedFeatureModule module)
            {
                if (module.HasFeature<UnselectedFeature>() || module.HasFeature<ExcludedFeature>())
                    throw new Exception("unselected feature was declared");
                var services = module.Services;
                module.Configure<PluginFeature>(p => p.Priority = 42);
                module.Configure(m => new CustomFeature(m) { Value = "caller" });
                module.Use<GenericFeature<int>>();
                module.Apply();
                module.Apply();
                var registry = (IInstalledFeatureRegistry)services.Last(x => x.ServiceType == typeof(IInstalledFeatureRegistry)).ImplementationInstance!;
                var info = registry.Find("Elsa.Plugin")!;
                var hosted = string.Join(",", services.Where(s => s.ServiceType == typeof(IHostedService))
                    .Select(s => s.ImplementationType!.Name + "/" + s.Lifetime));
                return string.Join("|", module.Properties["steps"], module.Properties["custom"], info.DisplayName,
                    info.Description, registry.Find("Elsa.Generic`1")!.Name, hosted);
            }
        }
        """;

    [Test]
    public async Task ProducerBindingsExecuteAcrossAssemblies()
    {
        var dependencyInput = Create("Infrastructure", ("Dependency.cs", Dependency));
        var dependency = Compile(dependencyInput);
        var dependencyImage = Emit(dependency);
        var input = Create("Application", ("Plugin.cs", Producer)).AddReferences(MetadataReference.CreateFromImage(dependencyImage));
        var producer = Compile(input);
        var context = new AssemblyLoadContext("feature-bindings", isCollectible: true);
        try
        {
            var infrastructure = context.LoadFromStream(new MemoryStream(dependencyImage));
            var app = context.LoadFromStream(new MemoryStream(Emit(producer)));
            var plan = new TestPlan(
                Bridge(infrastructure, "DatabaseFeature"), Bridge(app, "PluginFeature"),
                Bridge(app, "CustomFeature"), Bridge(app, "GenericFeature`1"));
            var run = app.GetType("Application.Scenario", throwOnError: true)!.GetMethod("Run")!;
            var actual = run.CreateDelegate<Func<FixedFeatureModule, string>>()(plan);
            await Assert.That(actual).IsEqualTo("module;database;plugin;database;plugin;|caller|Host \"plugin\"|Inherited description|Generic`1|First/Singleton,Second/Singleton");
        }
        finally
        {
            context.Unload();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PropertyReadsSelectOnlyTheAccessorsThatWillExecute(bool packed)
    {
        const string features = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            public sealed class FromRead(IModule m) : FeatureBase(m);
            public sealed class FromWrite(IModule m) : FeatureBase(m);
            public sealed class FromHostProperty(IModule m) : FeatureBase(m);
            public sealed class Unused(IModule m) : FeatureBase(m);
            public abstract class Parent(IModule m) : FeatureBase(m)
            {
                public virtual int Option { get; set; }
                public abstract Leaf Child { get; }
                public override void Configure() { _ = Child; _ = Services; }
            }
            public sealed class Root(IModule m) : Parent(m)
            {
                public Root Self => this;
                public override Leaf Child => Module.Configure<Leaf>();
                public int Count
                {
                    get { Module.Configure<FromRead>(); return 0; }
                    set => Module.Configure<FromWrite>();
                }
                public int WriteOnlyInHost
                {
                    get { Module.Configure<Unused>(); return 0; }
                    set { }
                }
                public int Auto { get; set; }
                public override void Apply() { _ = Auto; }
            }
            public static class PackageOptions
            {
                public static void SetOption(Parent feature) => feature.Option = 42;
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    var root = module.Configure<Root>();
                    PackageOptions.SetOption(root);
                    root.Self.Module.Configure<FromHostProperty>();
                    root.Count++;
                    root.WriteOnlyInHost = 42;
                    _ = nameof(root.WriteOnlyInHost);
                    module.Apply();
                    return module is Elsa.Features.Services.FixedFeatureModule && root.Option == 42 && module.HasFeature<Leaf>() &&
                        module.HasFeature<FromRead>() && module.HasFeature<FromWrite>() && module.HasFeature<FromHostProperty>() && !module.HasFeature<Unused>();
                }
            }
            """;
        await RunGeneratedHost(features, host, packed);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task HelperConstructorsSelectBaseBodyAndExecutedCallbackFeatures(bool packed, bool calledByHost)
    {
        const string library = """
            using System;
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class FromBase(IModule module) : FeatureBase(module);
            public sealed class Leaf(IModule module) : FeatureBase(module);
            public sealed class FromCallback(IModule module) : FeatureBase(module);
            public sealed class Deferred(IModule module) : FeatureBase(module);
            public abstract class BaseInstaller
            {
                protected BaseInstaller(IModule module, Action<IModule> install)
                {
                    module.Use<FromBase>();
                    install(module);
                }
            }
            public sealed class Installer : BaseInstaller
            {
                private readonly Action<IModule> _deferred;
                public Installer(IModule module, Action<IModule> configure, Action<IModule> deferred) : base(module, configure)
                {
                    module.Use<Leaf>();
                    _deferred = deferred;
                }
            }
            public sealed class Root(IModule module) : FeatureBase(module)
            {
                public override void Configure() =>
                    _ = new Installer(Module, m => m.Use<FromCallback>(), m => m.Use<Deferred>());
            }
            """;
        var setup = calledByHost ? """
            var selected = false;
            _ = new Installer(services.CreateModule(), module =>
            {
                module.Use<FromCallback>();
                selected = HasExpectedPlan(module);
            }, m => m.Use<Deferred>());
            return selected;
            """ : """
            var module = services.CreateModule();
            module.Use<Root>();
            module.Apply();
            return HasExpectedPlan(module);
            """;
        var host = $$"""
            using Elsa.Extensions;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    {{setup}}
                }
                private static bool HasExpectedPlan(IModule module) =>
                    module is FixedFeatureModule && module.HasFeature<FromBase>() && module.HasFeature<Leaf>() && module.HasFeature<FromCallback>()
                    && !module.HasFeature<Deferred>();
            }
            """;
        await RunGeneratedHost(library, host, packed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HelperInstanceSelectingThroughItsStoredModuleIsDiagnosed(bool packed)
    {
        const string helper = """
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule module) : FeatureBase(module);
            public sealed class Installer(IModule module)
            {
                private readonly IModule _module = module;
                public void Install() => _module.Use<Leaf>();
                public void Install(IModule other) => _module.Use<Leaf>();
                public bool HasModule() => _module != null;
                public static void InstallOn(IModule target) => target.Use<Leaf>();
            }
            """;
        foreach (var lifecycle in new[] { true, false })
        foreach (var alias in new[] { false, true })
        {
            var module = lifecycle ? "Module" : "module";
            var install = alias ? $"var installer = new Installer({module}); installer.Install();" : $"new Installer({module}).Install();";
            var features = helper + $$"""

                public sealed class Root(IModule module) : FeatureBase(module)
                {
                    public override void Configure() { {{(lifecycle ? install : "")}} }
                }
                """;
            var host = $$"""
                using Elsa.Extensions;
                using Microsoft.Extensions.DependencyInjection;
                public static class Host
                {
                    public static bool Start(IServiceCollection services)
                    {
                        var module = services.CreateModule();
                        {{(lifecycle ? "module.Use<Root>();" : install)}}
                        module.Apply();
                        return module.HasFeature<Leaf>();
                    }
                }
                """;
            var diagnostic = RunHost(features, host, packed).Diagnostics.Single();
            await Assert.That(diagnostic.Id).IsEqualTo(lifecycle ? "ELSAGEN011" : "ELSAGEN012");
            await Assert.That(diagnostic.GetMessage()).Contains("Installer.Install()");
            await Assert.That(diagnostic.GetMessage()).Contains("Pass the module explicitly");
        }

        await RunGeneratedHost(helper, """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    var installer = new Installer(module);
                    Installer.InstallOn(module);
                    module.Apply();
                    return installer.HasModule() && module.HasFeature<Leaf>();
                }
            }
            """, packed);

        const string multipleInputs = """
            using Elsa.Extensions;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static void Start(IServiceCollection services, IModule other) =>
                    new Installer(services.CreateModule()).Install(other);
            }
            """;
        var mixed = RunHost(helper, multipleInputs, packed).Diagnostics.Single();
        await Assert.That(mixed.Id).IsEqualTo("ELSAGEN012");
        await Assert.That(mixed.GetMessage()).Contains("module stored by its receiver");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LifecycleFactoryMustRunBeforeTheGeneratedSlotResolves(bool packed)
    {
        const string library = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Attributes;
            using Elsa.Features.Services;
            public sealed class Custom(IModule m, int value) : FeatureBase(m) { public int Value => value; }
            public sealed class Early(IModule m) : FeatureBase(m)
            {
                public override void Configure() => Module.Configure(x => new Custom(x, 42));
            }
            public sealed class Hosted(IModule m) : FeatureBase(m)
            {
                public override void ConfigureHostedServices() => Module.Configure(x => new Custom(x, 42));
            }
            public sealed class Late(IModule m) : FeatureBase(m)
            {
                public override void Apply() => Module.Configure(x => new Custom(x, 42));
            }
            [DependsOn(typeof(Custom))]
            public sealed class AfterDependency(IModule m) : FeatureBase(m)
            {
                public override void Configure() => Module.Configure(x => new Custom(x, 42));
            }
            public sealed class ReadsFirst(IModule m) : FeatureBase(m)
            {
                public override void Configure() { Module.Configure<Custom>(); Module.Configure(x => new Custom(x, 42)); }
            }
            """;
        foreach (var selection in new[] { "Early", "Hosted", "Late", "AfterDependency", "ReadsFirst" })
        {
            var host = $$"""
                using Elsa.Extensions;
                using Microsoft.Extensions.DependencyInjection;
                public static class Host
                {
                    public static bool Start(IServiceCollection services)
                    {
                        var module = services.CreateModule();
                        module.Use<{{selection}}>();
                        module.Apply();
                        return module.Configure<Custom>().Value == 42;
                    }
                }
                """;
            if (selection is "Early" or "Hosted") await RunGeneratedHost(library, host, packed);
            else
            {
                var input = packed
                    ? Create("Host", ("Host.cs", host)).AddReferences(MetadataReference.CreateFromImage(Emit(Compile(Create("Package", ("Features.cs", library))))))
                    : Create("Host", ("Host.cs", host), ("Features.cs", library));
                var result = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(input, out _).GetRunResult();
                var diagnostic = result.Diagnostics.Single();
                await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN011");
                await Assert.That(diagnostic.GetMessage()).Contains("requires an explicit factory");
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LifecycleFactoryCanUseAnIndependentlySelectedFeature(bool packed)
    {
        const string features = """
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Extra(IModule m) : FeatureBase(m);
            public sealed class Leaf(IModule m) : FeatureBase(m)
            {
                public bool CreatedByFactory { get; init; }
                public bool Configured { get; set; }
            }
            public sealed class Root(IModule m) : FeatureBase(m)
            {
                public override void Configure() => Module.Configure<Leaf>(m =>
                {
                    m.Use<Extra>();
                    return new Leaf(m) { CreatedByFactory = true };
                }, leaf => leaf.Configured = true);
            }
            """;
        static string Host(string selections, bool cached = false) => $$"""
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    {{selections}}
                    module.Use<Root>();
                    module.Apply();
                    var leaf = module.Configure<Leaf>();
                    return leaf.Configured && {{(cached ? "!" : "")}}leaf.CreatedByFactory && ReferenceEquals(leaf.Module, module);
                }
            }
            """;
        await RunGeneratedHost(features, Host("module.Use<Extra>();"), packed);
        await RunGeneratedHost(features, Host("module.Use<Extra>(); module.Use<Leaf>();", cached: true), packed);

        var diagnostic = RunHost(features, Host(""), packed).Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN012");
        await Assert.That(diagnostic.GetMessage()).Contains("Declare 'Extra' at build time");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HelperConstructorSuppliesFactoryBeforeObjectInitializerRuns(bool packed)
    {
        const string library = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Custom(IModule module, int value) : FeatureBase(module)
            {
                public int Value => value;
            }
            public sealed class Installer
            {
                public Installer(IModule module) => module.Configure(m => new Custom(m, 42));
                public int Value { get; init; }
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    var installer = new Installer(module) { Value = module.Configure<Custom>().Value };
                    return module is Elsa.Features.Services.FixedFeatureModule && installer.Value == 42;
                }
            }
            """;
        await RunGeneratedHost(library, host, packed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConditionalHelperConstructorCannotIntroduceAFeature(bool packed)
    {
        const string library = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule module) : FeatureBase(module);
            public sealed class Installer
            {
                public Installer(IModule module) => module.Configure<Leaf>();
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static void Start(IServiceCollection services, bool enabled)
                {
                    var module = services.CreateModule();
                    if (enabled) _ = new Installer(module);
                }
            }
            """;
        var diagnostic = RunHost(library, host, packed).Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN012");
        await Assert.That(diagnostic.GetMessage()).Contains("Leaf");
    }

    [Test]
    public async Task OpaqueHelperConstructorReceivingAModuleRequiresASummary()
    {
        const string library = """
            using Elsa.Features.Services;
            public sealed class Installer
            {
                public Installer(IModule module) { }
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static void Start(IServiceCollection services) => _ = new Installer(services.CreateModule());
            }
            """;
        var opaque = MetadataReference.CreateFromImage(Emit(Create("Opaque", ("Helper.cs", library))));
        var input = Create("Host", ("Host.cs", host)).AddReferences(opaque);
        var result = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(input, out _).GetRunResult();
        var diagnostic = result.Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN012");
        await Assert.That(diagnostic.GetMessage()).Contains("Installer");
    }

    private static async Task RunGeneratedHost(string features, string host, bool packed)
    {
        var library = packed ? Emit(Compile(Create("Package", ("Features.cs", features)))) : null;
        var input = library != null
            ? Create("Host", ("Host.cs", host)).AddReferences(MetadataReference.CreateFromImage(library))
            : Create("Host", ("Features.cs", features), ("Host.cs", host));
        var result = CompilerTest.RunHostGenerators(input, out var output);
        await Assert.That(result.Diagnostics).IsEmpty().Because(string.Join("\n", result.Diagnostics));
        var context = new AssemblyLoadContext("feature-effects", isCollectible: true);
        try
        {
            if (library != null) context.LoadFromStream(new MemoryStream(library));
            var compiledHost = context.LoadFromStream(new MemoryStream(Emit(output)));
            var run = compiledHost.GetType("Host", true)!.GetMethod("Start")!.CreateDelegate<Func<IServiceCollection, bool>>();
            await Assert.That(run(new ServiceCollection())).IsTrue();
        }
        finally
        {
            context.Unload();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OpaqueCallbackCanConfigureAVirtuallySelectedSlot(bool packed)
    {
        static string Features(string callback) => $$"""
            using System;
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule m) : FeatureBase(m) { public bool Configured { get; set; } }
            public class Root(IModule m) : FeatureBase(m)
            {
                private IModule Foreign { get; set; } = null!;
                public override void Configure() => Array.ForEach(new[] { 1 }, _ => {{callback}});
                protected virtual void Select() { }
            }
            public sealed class Derived(IModule m) : Root(m)
            {
                protected override void Select() => Module.Use<Leaf>(leaf => leaf.Configured = true);
            }
            """;
        static string Host(bool selectLeaf) => $$"""
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    {{(selectLeaf ? "var leaf = module.Configure<Leaf>();" : "")}}
                    module.Use<Derived>();
                    module.Apply();
                    return {{(selectLeaf ? "leaf.Configured" : "true")}};
                }
            }
            """;
        var features = Features("Select()");
        var diagnostic = RunHost(features, Host(selectLeaf: false), packed).Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN012");
        await Assert.That(diagnostic.GetMessage()).Contains("Declare 'Leaf' at build time");
        var host = Host(selectLeaf: true);
        var error = RunHost(Features("Foreign.Use<Leaf>()"), host, packed).Diagnostics.Single();
        await Assert.That(error.Id).IsEqualTo("ELSAGEN011");
        await Assert.That(error.GetMessage()).Contains("cannot establish the module");
        await RunGeneratedHost(features, host, packed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VirtualSettersFollowTheConcreteLifecycleReceiverAndExplicitBase(bool packed)
    {
        const string features = """
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            public sealed class BaseOnly(IModule m) : FeatureBase(m);
            public abstract class Parent(IModule m) : FeatureBase(m)
            {
                public virtual int Option { set => Module.Use<BaseOnly>(); }
                public override void Configure() => Option = 1;
            }
            public sealed class Root(IModule m) : Parent(m)
            {
                public override int Option { set => Module.Use<Leaf>(); }
            }
            public sealed class Direct(IModule m) : Parent(m)
            {
                public override int Option { set => Module.Use<Leaf>(); }
                public override void Configure() => base.Option = 1;
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var root = services.CreateModule().Use<Root>();
                    var direct = services.CreateModule().Use<Direct>();
                    root.Apply();
                    direct.Apply();
                    return root.HasFeature<Leaf>() && !root.HasFeature<BaseOnly>() &&
                        direct.HasFeature<BaseOnly>() && !direct.HasFeature<Leaf>();
                }
            }
            """;
        await RunGeneratedHost(features, host, packed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SetterOnAnOrdinaryClassCanConfigureAnAssignedModule(bool packed)
    {
        const string library = """
            using Elsa.Extensions;
            using Elsa.Features.Services;
            using Elsa.Features.Abstractions;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            public sealed class Installer
            {
                public IModule Target { set => value.Use<Leaf>(); }
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    new Installer().Target = module;
                    module.Apply();
                    return module.HasFeature<Leaf>();
                }
            }
            """;
        await RunGeneratedHost(library, host, packed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ForwardedVirtualSetterUsesTheKnownOverrideOnOwnedFeatureSlots(bool packed)
    {
        const string library = """
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            public abstract class Parent<T>(IModule m) : FeatureBase(m) { public virtual int Option { set { } } }
            public class Root(IModule m) : Parent<Root>(m)
            {
                public override int Option { set => Module.Configure<Child>(child => child.Value = value); }
            }
            public class Child(IModule m) : FeatureBase(m) { public virtual int Value { get; set; } }
            public static class Options
            {
                public static void Forward<T>(this Parent<T> input) => Write(input);
                public static void Write<T>(Parent<T> target) => target.Option = 17;
                public static void Mixed(Parent<Root> target) { target.Module.Use<Leaf>(); target.Option = 17; }
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    var root = module.Configure<Root>();
                    root.Forward();
                    module.Apply();
                    return module.HasFeature<Child>();
                }
            }
            """;
        await RunGeneratedHost(library, host, packed);
        var mixed = RunHost(library, host.Replace("root.Forward();", "Options.Mixed(root);"), packed);
        await Assert.That(mixed.Diagnostics.Any(d => d.Id == "ELSAGEN012" && d.GetMessage().Contains("virtual feature selection"))).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConditionalVirtualSetterAndOpenHostSetterAreDiagnosed(bool packed)
    {
        const string library = """
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            public abstract class Parent(IModule m) : FeatureBase(m)
            {
                public bool Enabled { get; set; }
                public virtual int Option { set { } }
                public override void Configure() { if (Enabled) Option = 1; }
            }
            public sealed class Root(IModule m) : Parent(m)
            {
                public override int Option { set => Module.Use<Leaf>(); }
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static void Start(IServiceCollection services)
                {
                    var root = services.CreateModule().Configure<Root>();
                    ((Parent)root).Option = 1;
                }
            }
            """;
        var result = RunHost(library, host, packed);
        await Assert.That(result.Diagnostics.Any(d => d.Id == "ELSAGEN012" && d.GetMessage().Contains("conditionally selects 'Leaf'"))).IsTrue();
        await Assert.That(result.Diagnostics.Any(d => d.Id == "ELSAGEN012" && d.GetMessage().Contains("virtual feature selection"))).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetterReturningAnUnknownModuleCannotRedirectAFeatureSelection(bool packed)
    {
        const string features = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Root(IModule m) : FeatureBase(m)
            {
                public IModule AnotherModule { get; set; } = null!;
            }
            public sealed class Leaf(IModule m) : FeatureBase(m);
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static void Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    module.Configure<Root>().AnotherModule.Use<Leaf>();
                }
            }
            """;
        var input = packed
            ? Create("Host", ("Host.cs", host)).AddReferences(MetadataReference.CreateFromImage(Emit(Compile(Create("Package", ("Feature.cs", features))))))
            : Create("Host", ("Host.cs", host), ("Feature.cs", features));
        var result = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(input, out _).GetRunResult();
        var diagnostic = result.Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN012");
        await Assert.That(diagnostic.GetMessage()).Contains("which module 'Root.AnotherModule' returns");
    }

    [Test]
    [Arguments("Select", false)]
    [Arguments("FactoryFirst", false)]
    [Arguments("FactoryLast", true)]
    [Arguments("ApplyFirst", true)]
    [Arguments("ConditionalFirst", true)]
    [Arguments("AfterHelperApply", true)]
    [Arguments("SelectConditionally", true)]
    public async Task PackedHelpersPreserveUnconditionalFactoryRegistrations(string entryPoint, bool missingFactory)
    {
        const string package = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class CustomFeature(IModule module, int value) : FeatureBase(module);
            public static class FeatureSetup
            {
                public static void Select(IModule module, bool enabled) => SupplyFactory(module);
                public static void FactoryFirst(IModule module, bool enabled)
                {
                    SupplyFactory(module);
                    module.Configure<CustomFeature>();
                }
                public static void FactoryLast(IModule module, bool enabled)
                {
                    module.Configure<CustomFeature>();
                    SupplyFactory(module);
                }
                public static void ApplyFirst(IModule module, bool enabled)
                {
                    module.Apply();
                    SupplyFactory(module);
                }
                public static void ConditionalFirst(IModule module, bool enabled)
                {
                    if (enabled) module.Configure<CustomFeature>();
                    SupplyFactory(module);
                }
                public static void AfterHelperApply(IModule module, bool enabled)
                {
                    Apply(module);
                    SupplyFactory(module);
                }
                private static void Apply(IModule module) => module.Apply();
                public static void SelectConditionally(IModule module, bool enabled)
                {
                    if (enabled) SupplyFactory(module);
                    module.Configure<CustomFeature>();
                }
                private static void SupplyFactory(IModule module) => module.Configure(m => new CustomFeature(m, 42));
            }
            """;
        var reference = MetadataReference.CreateFromImage(Emit(Compile(Create("Package", ("Setup.cs", package)))));
        var app = $$"""
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class App
            {
                public static void Start(IServiceCollection services, bool enabled)
                {
                    var module = services.CreateModule();
                    FeatureSetup.{{entryPoint}}(module, enabled);
                    module.Apply();
                }
            }
            """;
        var input = Create("Host", ("Host.cs", app)).AddReferences(reference);
        var result = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(input, out _).GetRunResult();
        if (missingFactory)
        {
            var diagnostic = result.Diagnostics.Single();
            await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN011");
            await Assert.That(diagnostic.GetMessage()).Contains("requires an explicit factory");
        }
        else
        {
            await Assert.That(result.Diagnostics).IsEmpty();
            await Assert.That(result.GeneratedTrees.Single().ToString()).Contains(".DeclareCustomFeature_");
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task PackedSelectorResolvesFeaturesInCallbackOrder(bool factoryFirst)
    {
        var package = $$"""
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Custom(IModule module, int value) : FeatureBase(module) { public int Value => value; }
            public static class Helpers
            {
                public static void Setup(IModule module, System.Action<IModule> callback) => Install(module, callback);
                private static void Install(IModule module, System.Action<IModule> callback)
                {
                    {{(factoryFirst ? "module.Configure(m => new Custom(m, 1)); callback(module);" : "callback(module); module.Configure(m => new Custom(m, 1));")}}
                }
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    var observedValue = 0;
                    Helpers.Setup(module, m => observedValue = m.Configure<Custom>().Value);
                    module.Apply();
                    return observedValue == 1;
                }
            }
            """;
        if (factoryFirst) await RunGeneratedHost(package, host, packed: true);
        else await Assert.That(RunHost(package, host, packed: true).Diagnostics.Single().Id).IsEqualTo("ELSAGEN011");
    }

    [Test]
    public async Task ProducerForwardsOnlyItsSelectedConstructorsPublicationRequirements()
    {
        const string source = """
            using System.Diagnostics.CodeAnalysis;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class OptionalFeature : FeatureBase
            {
                [RequiresDynamicCode("Needs runtime generation.", Url = "https://example.org/native")]
                [RequiresUnreferencedCode("Reads metadata.")]
                public OptionalFeature(IModule module) : base(module) { }
                [RequiresAssemblyFiles("This overload is unused.")]
                public OptionalFeature(IModule module, object value) : base(module) { }
            }
            """;
        var result = CompilerTest.CreateDriver(new ElsaFeatureGenerator()).Run(Create("Producer", ("Optional.cs", source)), out var produced);
        await produced.AssertEmits();
        var generated = result.GetRunResult().GeneratedTrees.Single().ToString();
        await Assert.That(generated).Contains("""RequiresDynamicCodeAttribute("OptionalFeature: Needs runtime generation.", Url = "https://example.org/native")""");
        await Assert.That(generated).Contains("""RequiresUnreferencedCodeAttribute("OptionalFeature: Reads metadata.")""");
        await Assert.That(generated.Contains("RequiresAssemblyFiles", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task PackedWrapperExportsItsSelectedInternalFeaturesAndSkipsDeferredCallbacks()
    {
        const string source = """
            using System;
            using Elsa.Features.Services;
            using Elsa.Features.Abstractions;
            namespace Package;
            public sealed class InstalledFeature(IModule module) : FeatureBase(module);
            internal sealed class AdditionalFeature(IModule module) : FeatureBase(module);
            internal sealed class DeferredFeature(IModule module) : FeatureBase(module);
            public static class Extras
            {
                public static Action Later = () => { };
                public static void SelectAdditional(this InstalledFeature feature) => feature.Module.Configure<AdditionalFeature>();
                public static void InstallExtras(this IModule module, Action<InstalledFeature>? configure = null)
                {
                    void Install() => module.Configure<InstalledFeature>();
                    void ConfigureInstalled() => module.Configure<InstalledFeature>(feature => { configure?.Invoke(feature); });
                    Install();
                    ConfigureInstalled();
                    Later = () => module.Configure<DeferredFeature>();
                }
            }
            """;
        var producer = Compile(Create("Example.Package_002E", ("Package.cs", source)));
        using var referenceAssembly = new MemoryStream();
        var emitted = producer.Emit(referenceAssembly, options: new Microsoft.CodeAnalysis.Emit.EmitOptions(metadataOnly: true, includePrivateMembers: false));
        await Assert.That(emitted.Success).IsTrue().Because(string.Join("\n", emitted.Diagnostics));
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            using Package;
            public static class Host
            {
                public static void Start(IServiceCollection services) => services.CreateModule().InstallExtras(f => f.SelectAdditional());
            }
            """;
        var input = Create("Host", ("Host.cs", host)).AddReferences(MetadataReference.CreateFromImage(referenceAssembly.ToArray()));
        var result = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(input, out _).GetRunResult();
        await Assert.That(result.Diagnostics).IsEmpty();
        var hook = result.GeneratedTrees.Single().ToString();
        await Assert.That(hook).Contains("PExample_002EPackage_005F002E.Bindings.DeclareInstalledFeature_");
        await Assert.That(hook).Contains(".DeclareAdditionalFeature_");
        await Assert.That(hook.Contains(".DeclareDeferredFeature_", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ClosedFeatureUsesItsProtectedOverrideFromACompiledBase()
    {
        const string foundation = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public abstract class Foundation<T>(IModule module) : FeatureBase(module)
            {
                public override void ConfigureHostedServices() => Install();
                protected virtual void Install() { }
            }
            """;
        var parent = Compile(Create("Foundation", ("Base.cs", foundation)));
        const string derived = """
            using Elsa.Features.Services;
            using Microsoft.Extensions.Hosting;
            using System.Threading;
            using System.Threading.Tasks;
            internal class Worker<T> : IHostedService
            {
                public Task StartAsync(CancellationToken token) => Task.CompletedTask;
                public Task StopAsync(CancellationToken token) => Task.CompletedTask;
            }
            public sealed class Plugin(IModule module) : Foundation<int>(module)
            {
                protected override void Install() => Module.ConfigureHostedService<Worker<int>>();
            }
            """;
        var child = Create("Producer", ("Plugin.cs", derived)).AddReferences(MetadataReference.CreateFromImage(Emit(parent)));
        var result = CompilerTest.CreateDriver(new ElsaFeatureGenerator()).Run(child, out var output);
        await output.AssertEmits();
        var bridge = result.GetRunResult().GeneratedTrees.Single().ToString();
        await Assert.That(bridge).Contains(".DeclareHostedWorker_");
        await Assert.That(bridge).Contains("module.DeclareHostedService<global::Worker<int>>();");
    }

    [Test]
    public async Task PackageConditionalSelectionIsLegalOnlyWhenItsFeatureAlreadyBelongsToThePlan()
    {
        const string package = """
            using Elsa.Features.Services;
            using Elsa.Features.Abstractions;
            public sealed class Feature(IModule m) : FeatureBase(m);
            public static class PackageExtensions
            {
                public static void Maybe(this IModule m, bool enable)
                {
                    if (!enable) return;
                    m.Configure<Feature>();
                }
            }
            """;
        var reference = MetadataReference.CreateFromImage(Emit(Compile(Create("External", ("Package.cs", package)))));
        foreach (var selected in new[] { false, true })
        {
            var host = $$"""
                using Elsa.Extensions;
                using Microsoft.Extensions.DependencyInjection;
                public static class Host
                {
                    public static void Start(IServiceCollection services, bool flag)
                    {
                        var module = services.CreateModule();
                        {{(selected ? "module.Configure<Feature>();" : "")}}
                        module.Maybe(flag);
                    }
                }
                """;
            var input = Create("Host", ("Host.cs", host)).AddReferences(reference);
            var result = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(input, out _).GetRunResult();
            if (selected) await Assert.That(result.Diagnostics).IsEmpty();
            else await Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.GetMessage().Contains("Feature"))).IsTrue();
        }
    }

    [Test]
    public async Task SelectedFeatureRejectsOpaqueBaseCallbacksAndIncludesExplicitInterfaceImplementations()
    {
        const string dependency = """
            using Elsa.Features.Services;
            using Elsa.Features.Abstractions;
            public abstract class LegacyBase(IModule m) : FeatureBase(m)
            {
                public override void Apply() => Module.Configure<PrivateFeature>();
                private sealed class PrivateFeature(IModule m) : FeatureBase(m);
            }
            """;
        var opaque = MetadataReference.CreateFromImage(Emit(Create("Legacy", ("Legacy.cs", dependency))));
        const string declarations = """
            using Elsa.Features.Services;
            using Elsa.Features.Abstractions;
            public sealed class Derived(IModule m) : LegacyBase(m);
            public sealed class Extra(IModule m) : FeatureBase(m);
            public sealed class Explicit(IModule module) : IFeature
            {
                public IModule Module => module;
                void IFeature.Configure() => Module.Configure<Extra>();
                void IFeature.ConfigureHostedServices() { }
                void IFeature.Apply() { }
            }
            """;
        foreach (var root in new[] { "Derived", "Explicit" })
        {
            var host = $$"""
                using Elsa.Extensions;
                using Microsoft.Extensions.DependencyInjection;
                public static class App
                {
                    public static void Start(IServiceCollection services) => services.CreateModule().Configure<{{root}}>();
                }
                """;
            var input = Create("Host", ("Features.cs", declarations), ("Host.cs", host)).AddReferences(opaque);
            var result = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(input, out _).GetRunResult();
            if (root == "Derived") await Assert.That(result.Diagnostics.Any(d => d.GetMessage().Contains("LegacyBase.Apply"))).IsTrue();
            else
            {
                await Assert.That(result.Diagnostics).IsEmpty();
                await Assert.That(result.GeneratedTrees.Single().ToString()).Contains(".DeclareExtra_");
            }
        }
    }

    [Test]
    public async Task ProducerMarksComposingConstructorsAndKeepsBypassOverloadsDistinct()
    {
        const string source = """
            using Elsa.Features.Services;
            using Elsa.Features.Abstractions;
            public sealed class Extra(IModule module) : FeatureBase(module);
            public abstract class Base : FeatureBase
            {
                protected Base(IModule module, bool marker) : base(module) => module.Configure<Extra>();
                protected Base(IModule module) : base(module) { }
            }
            public sealed class Plugin : Base
            {
                public Plugin(IModule module) : base(module, true) { }
                public Plugin(IModule module, string value) : base(module) { }
            }
            public sealed class Primary(IModule module) : FeatureBase(module)
            {
                public Extra Child { get; } = module.Configure<Extra>();
            }
            """;
        var result = CompilerTest.CreateDriver(new ElsaFeatureGenerator()).Run(Create("CtorPlugin", ("Ctor.cs", source)), out var output);
        await output.AssertEmits();
        var generated = result.GetRunResult().GeneratedTrees.Single().ToString();
        await Assert.That(generated).Contains("'Plugin.Plugin(Elsa.Features.Services.IModule)' changes or may change feature composition");
        await Assert.That(generated).Contains("'Primary.Primary(Elsa.Features.Services.IModule)' changes or may change feature composition");
        var overload = generated.Split('\n').Single(line => line.Contains("M:Plugin.#ctor(Elsa.Features.Services.IModule,System.String)"));
        await Assert.That(overload.Contains("Extra", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompositionHelpersRetainConstrainedPartialAndAliasedEffects(bool packed)
    {
        const string library = """
            using System;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Direct(IModule module) : FeatureBase(module);
            public sealed class Callback(IModule module) : FeatureBase(module);
            public static partial class Helpers
            {
                public static partial void Install<T>(T module, Action<IModule> configure) where T : IModule;
                public static partial void Install<T>(T module, Action<IModule> configure) where T : IModule
                {
                    module.Configure<Direct>();
                    var run = configure;
                    run(module);
                }
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            public static class App
            {
                public static void Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    Helpers.Install(module, m => m.Configure<Callback>());
                }
            }
            """;
        var result = RunHost(library, host, packed);
        await Assert.That(result.Diagnostics).IsEmpty().Because(string.Join("\n", result.Diagnostics));
        var plan = result.GeneratedTrees.Single().ToString();
        await Assert.That(plan).Contains(".DeclareDirect_");
        await Assert.That(plan).Contains(".DeclareCallback_");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AConditionalDelegateAliasCannotSilentlyDropAPackedCallback(bool conditional)
    {
        var library = $$"""
            using System;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule module) : FeatureBase(module);
            public static class Helpers
            {
                public static void Install(this IModule module, Action<IModule> configure, bool flag)
                {
                    Action<IModule> run = m => { };
                    {{(conditional ? "if (flag)" : "")}} run = configure;
                    run?.Invoke(module);
                }
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class App
            {
                public static void Start(IServiceCollection services, bool flag) =>
                    services.CreateModule().Install(m => m.Configure<Leaf>(), flag);
            }
            """;
        var result = RunHost(library, host, packed: true);
        if (conditional)
        {
            var diagnostic = result.Diagnostics.Single();
            await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN012");
            await Assert.That(diagnostic.GetMessage()).Contains("which feature configuration callback 'run' invokes");
        }
        else
        {
            await Assert.That(result.Diagnostics).IsEmpty();
            await Assert.That(result.GeneratedTrees.Single().ToString()).Contains(".DeclareLeaf_");
        }
    }

    [Test]
    [Arguments("PrivateService", "hosted-service binding")]
    [Arguments("ComposingConstructor", "during construction")]
    public async Task SelectedFeaturesReportUnavailableLifecycleBindings(string root, string reason)
    {
        const string library = """
            using Elsa.Features.Services;
            using Elsa.Features.Abstractions;
            using Microsoft.Extensions.Hosting;
            using System.Threading;
            using System.Threading.Tasks;
            public sealed class Extra(IModule module) : FeatureBase(module);
            public abstract class Foundation(IModule module)
            {
                private readonly Extra extra = module.Configure<Extra>();
            }
            public sealed class ComposingConstructor(IModule module) : Foundation(module), IFeature
            {
                public IModule Module => module;
                public void Configure() { }
                public void Apply() { }
                public void ConfigureHostedServices() { }
            }
            public sealed class PrivateService(IModule module) : FeatureBase(module)
            {
                public override void ConfigureHostedServices() => ConfigureHostedService<Worker>();
                private sealed class Worker : IHostedService
                {
                    public Task StartAsync(CancellationToken token) => Task.CompletedTask;
                    public Task StopAsync(CancellationToken token) => Task.CompletedTask;
                }
            }
            """;
        var host = $$"""
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class App
            {
                public static void Start(IServiceCollection services) => services.CreateModule().Use<{{root}}>();
            }
            """;
        foreach (var packed in new[] { false, true })
        {
            var result = RunHost(library, host, packed);
            var diagnostic = result.Diagnostics.Single();
            await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN011");
            await Assert.That(diagnostic.GetMessage()).Contains(reason);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CoalescingAssignmentRequiresPriorSelection(bool packed)
    {
        const string library = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule module) : FeatureBase(module);
            public static class Helpers
            {
                public static void Install(IModule module, object? option) => option ??= module.Configure<Leaf>();
            }
            """;
        foreach (var selected in new[] { false, true })
        {
            var host = $$"""
                using Elsa.Extensions;
                using Microsoft.Extensions.DependencyInjection;
                public static class App
                {
                    public static void Start(IServiceCollection services, Leaf? option)
                    {
                        var module = services.CreateModule();
                        {{(selected ? "module.Configure<Leaf>();" : "")}}
                        {{(packed ? "Helpers.Install(module, option);" : "option ??= module.Configure<Leaf>();")}}
                    }
                }
                """;
            var result = RunHost(library, host, packed);
            if (selected) await Assert.That(result.Diagnostics).IsEmpty();
            else await Assert.That(result.Diagnostics.Single().Id).IsEqualTo("ELSAGEN012");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LifecycleDelegatesCanCaptureFeatureSelections(bool packed)
    {
        const string library = """
            using System;
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            public sealed class Other(IModule m) : FeatureBase(m);
            public sealed class Unused(IModule m) : FeatureBase(m);
            public sealed class Root(IModule m) : FeatureBase(m)
            {
                public Action? Notify { get; set; }
                public override void Configure()
                {
                    Action install = () => Module.Use<Leaf>();
                    var alias = install;
                    alias();
                    Action<int> other = Select;
                    Action<Action> dispatch = callback => callback();
                    dispatch(() => other?.Invoke(1));
                    Action deferred = () => Module.Use<Unused>();
                    Action<int>? skipped = null;
                    skipped?.Invoke(Module.Configure<Unused>() == null ? 1 : 0);
                    Notify?.Invoke();
                    Action<string> log = Console.WriteLine;
                    log("");
                }
                private void Select(int option) => Module.Use<Other>();
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    module.Use<Root>();
                    module.Apply();
                    return module.HasFeature<Leaf>() && module.HasFeature<Other>() && !module.HasFeature<Unused>();
                }
            }
            """;
        await RunGeneratedHost(library, host, packed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DelegatesInvokedConditionallyRequireAnAdmittedSlot(bool packed)
    {
        const string library = """
            using System;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            public sealed class Root(IModule m) : FeatureBase(m)
            {
                public bool Enabled { get; set; }
                public override void Configure()
                {
                    Action install = () => Module.Configure<Leaf>();
                    if (Enabled) install();
                }
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host { public static void Start(IServiceCollection services) => services.CreateModule().Use<Root>(); }
            """;
        var diagnostic = RunHost(library, host, packed).Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN012");
        await Assert.That(diagnostic.GetMessage()).Contains("Leaf");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CallbackArgumentsMustBelongToTheComposedModule(bool packed)
    {
        const string library = """
            using System;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            public static class Helpers
            {
                public static IModule Other { get; set; } = null!;
                public static void Run(this IModule first, Action<IModule> configure) => configure(Other);
                public static void Forward(this IModule first, Action<IModule> configure) { var same = first; configure?.Invoke(same); }
            }
            """;
        foreach (var name in new[] { "Run", "Forward" })
        {
            var host = $$"""
                using Elsa.Extensions;
                using Microsoft.Extensions.DependencyInjection;
                public static class Host
                {
                    public static void Start(IServiceCollection services) =>
                        services.CreateModule().{{name}}(m => m.Configure<Leaf>());
                }
                """;
            var result = RunHost(library, host, packed);
            if (name == "Run")
            {
                var diagnostic = result.Diagnostics.Single();
                await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN012");
                await Assert.That(diagnostic.GetMessage()).Contains("module passed to");
            }
            else
            {
                await Assert.That(result.Diagnostics).IsEmpty();
                await Assert.That(result.GeneratedTrees.Single().ToString()).Contains(".DeclareLeaf_");
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task VirtualLifecycleCallsPreserveClosedArrayTypeArguments(bool packed)
    {
        const string library = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public abstract class Foundation(IModule m) : FeatureBase(m)
            {
                public override void Configure() => Forward<int[][,]>();
                private void Forward<T>() => Probe<T>();
                protected abstract void Probe<T>();
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            public sealed class Root(IModule m) : Foundation(m)
            {
                protected override void Probe<T>() => Module.Configure<Leaf>();
            }
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    module.Use<Root>();
                    module.Apply();
                    return module.HasFeature<Leaf>();
                }
            }
            """;
        await RunGeneratedHost(library, host, packed);
    }

    [Test]
    [Arguments("invoke")]
    [Arguments("conditional")]
    [Arguments("store")]
    public async Task VirtualLifecycleOverrideChecksWhetherTheCallbackRuns(string behavior)
    {
        const string library = """
            using System;
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule module) : FeatureBase(module);
            public abstract class Foundation(IModule module) : FeatureBase(module)
            {
                public override void Configure() => Install(m => m.Use<Leaf>());
                protected abstract void Install(Action<IModule> configure);
            }
            """;
        var body = behavior switch
        {
            "invoke" => "callback(Module);",
            "conditional" => "if (Module.Properties.ContainsKey(\"enabled\")) callback(Module);",
            _ => "_later = callback;"
        };
        var host = $$"""
            using System;
            using Elsa.Extensions;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            public sealed class Root(IModule module) : Foundation(module)
            {
                private Action<IModule>? _later;
                protected override void Install(Action<IModule> callback) { {{body}} }
            }
            public static class Host
            {
                public static void Start(IServiceCollection services) => services.CreateModule().Use<Root>();
            }
            """;
        foreach (var packed in new[] { false, true })
        {
            var result = RunHost(library, host, packed);
            if (behavior == "store")
            {
                await Assert.That(result.Diagnostics).IsEmpty().Because(string.Join("\n", result.Diagnostics));
                await Assert.That(result.GeneratedTrees.Single().ToString().Contains(".DeclareLeaf_", StringComparison.Ordinal)).IsFalse();
            }
            else
            {
                var diagnostic = result.Diagnostics.Single();
                await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN011");
                await Assert.That(diagnostic.GetMessage()).Contains("Install");
            }
        }
    }

    [Test]
    [Arguments("base.Install", false)]
    [Arguments("Module.Configure<SealedReceiver>().Install", false)]
    [Arguments("Module.Configure<FinalMethodReceiver>().Install", false)]
    [Arguments("Other.Install", true)]
    public async Task VirtualLifecycleCallsBindOnlyKnownFeatureReceivers(string target, bool unknownReceiver)
    {
        var library = $$"""
            using System;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule module) : FeatureBase(module);
            public class Receiver(IModule module) : FeatureBase(module)
            {
                public virtual void Install(Action<IModule> callback) => callback(Module);
            }
            public sealed class SealedReceiver(IModule module) : Receiver(module);
            public class FinalMethodReceiver(IModule module) : Receiver(module)
            {
                public sealed override void Install(Action<IModule> callback) => callback(Module);
            }
            public sealed class Root(IModule module) : Receiver(module)
            {
                private Receiver Other => Module.Properties.TryGetValue("other", out var value) ? (Receiver)value : this;
                public override void Configure() => {{target}}(m => m.Configure<Leaf>());
                public override void Install(Action<IModule> callback) { }
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static void Start(IServiceCollection services) => services.CreateModule().Use<Root>();
            }
            """;
        foreach (var packed in new[] { false, true })
        {
            var result = RunHost(library, host, packed);
            if (unknownReceiver)
            {
                var diagnostic = result.Diagnostics.Single();
                await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN011");
                await Assert.That(diagnostic.GetMessage()).Contains("Install");
            }
            else
            {
                await Assert.That(result.Diagnostics).IsEmpty().Because(string.Join("\n", result.Diagnostics));
                await Assert.That(result.GeneratedTrees.Single().ToString()).Contains(".DeclareLeaf_");
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HostSelectorsCannotLoseUnresolvedVirtualDispatch(bool packed)
    {
        const string library = """
            using Elsa.Extensions;
            using Elsa.Features.Services;
            using Elsa.Features.Abstractions;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            public class Root(IModule m) : FeatureBase(m)
            {
                public void Install() => Select();
                public void MaybeInstall(bool flag) { if (flag) Select(); }
                public virtual void Select() { }
            }
            public sealed class Derived(IModule m) : Root(m)
            {
                public override void Select() => Module.Use<Leaf>();
            }
            public static class Options
            {
                public static void Install(Root root) => root.Install();
            }
            public class Setup { public virtual void Configure(Root root) { } }
            public sealed class DerivedSetup : Setup
            {
                public override void Configure(Root root) => root.Module.Use<Leaf>();
            }
            """;
        foreach (var callback in new[] { "root => root.Install()", "root => root.MaybeInstall(flag)", "root => root.Select()", "Options.Install", "setup.Configure" })
        {
            var result = Generate("module.Configure<Root>(m => new Derived(m), " + callback + ");");
            await Assert.That(result.Diagnostics.Any(d => d.Id == "ELSAGEN012" && d.GetMessage().Contains("virtual feature selection")))
                .IsTrue().Because(callback + ": " + string.Join("\n", result.Diagnostics));
        }
        var known = Generate("module.Configure<Derived>(root => root.Select());");
        await Assert.That(known.Diagnostics).IsEmpty().Because(string.Join("\n", known.Diagnostics));
        await Assert.That(known.GeneratedTrees.Single().ToString()).Contains(".DeclareLeaf_");

        GeneratorDriverRunResult Generate(string selection) => RunHost(library, $$"""
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static void Start(IServiceCollection services, bool flag)
                {
                    Setup setup = new DerivedSetup();
                    var module = services.CreateModule();
                    {{selection}}
                }
            }
            """, packed);
    }

    [Test]
    public async Task VirtualLifecycleOverridesBindTheTypeArgumentsSuppliedByACompiledBase()
    {
        const string library = """
            using Elsa.Features.Services;
            using Elsa.Features.Abstractions;
            public sealed class Extra(IModule module) : FeatureBase(module);
            public abstract class Foundation(IModule module) : FeatureBase(module)
            {
                public override void Configure() => Forward<Extra>();
                private void Forward<T>() where T : class, IFeature => Install<T>();
                protected abstract void Install<T>() where T : class, IFeature;
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Elsa.Features.Services;
            using Microsoft.Extensions.DependencyInjection;
            public sealed class Derived(IModule module) : Foundation(module)
            {
                protected override void Install<T>() => Module.Configure<T>();
            }
            public static class App
            {
                public static void Start(IServiceCollection services) => services.CreateModule().Use<Derived>();
            }
            """;
        foreach (var packed in new[] { false, true })
        {
            var result = RunHost(library, host, packed);
            await Assert.That(result.Diagnostics).IsEmpty().Because(string.Join("\n", result.Diagnostics));
            await Assert.That(result.GeneratedTrees.Single().ToString()).Contains(".DeclareExtra_");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PartialHelperPreservesTheModuleForANamedHostedServiceRegistration(bool packed)
    {
        const string library = """
            using Elsa.Features.Services;
            using Microsoft.Extensions.Hosting;
            using System.Threading;
            using System.Threading.Tasks;
            public class Worker : IHostedService
            {
                public Task StartAsync(CancellationToken token) => Task.CompletedTask;
                public Task StopAsync(CancellationToken token) => Task.CompletedTask;
            }
            public static partial class Helpers
            {
                public static partial IModule Select(IModule module);
                public static partial IModule Select(IModule implementation) => implementation;
            }
            """;
        const string host = """
            using System.Linq;
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    Helpers.Select(module).ConfigureHostedService(priority: 10, hostedServiceType: typeof(Worker));
                    module.Apply();
                    return services.Any(d => d.ImplementationType == typeof(Worker));
                }
            }
            """;
        await RunGeneratedHost(library, host, packed);
    }

    [Test]
    [Arguments("Module.Configure<Leaf>(m => { m.Use<Leaf>(); return new Leaf(m); })", "same slot")]
    [Arguments("Module.Configure<Leaf>(m => { m.Apply(); return new Leaf(m); })", "applies the module")]
    [Arguments("Module.Configure<Leaf>(m => { System.Array.ForEach(new[] { 1 }, _ => m.Use<Extra>()); return new Leaf(m); })", "Move feature selection to Configure")]
    [Arguments("Module.Configure<Leaf>(m => { Install(m); return new Leaf(m); })", "Feature factory invokes")]
    [Arguments("Module.Configure<Leaf>(m => { _ = new Installer(m); return new Leaf(m); })", "Feature factory invokes")]
    [Arguments("Module.Configure<Extra>(outer => Module.Configure<Leaf>(m => new Leaf(outer.Module)))", "module owns the factory result")]
    [Arguments("Module.Configure<Leaf>(m => new Leaf(Foreign))", "module owns the factory result")]
    [Arguments("Module.Configure<Leaf>(m => { Foreign.Use<Extra>(); return new Leaf(m); })", "module used by captured feature registration")]
    [Arguments("Foreign.Configure<Leaf>(m => new Leaf(m))", "supplied module")]
    [Arguments("Module.Configure<Leaf>(Create)", "Use a lambda")]
    public async Task UnsupportedLifecycleFactoryReportsAnActionableDiagnostic(string call, string reason)
    {
        var library = $$"""
            using Elsa.Extensions;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule module) : FeatureBase(module);
            public sealed class Extra(IModule module) : FeatureBase(module);
            public sealed class Root(IModule module) : FeatureBase(module)
            {
                private IModule Foreign { get; set; } = null!;
                private static Leaf Create(IModule m) => new Leaf(m);
                private static void Install(IModule m) => System.Array.ForEach(new[] { 1 }, _ => m.Use<Extra>());
                private sealed class Installer { public Installer(IModule m) => Install(m); }
                public override void Configure() => {{call}};
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static void Start(IServiceCollection services) => services.CreateModule().Use<Root>();
            }
            """;
        foreach (var packed in new[] { false, true })
        {
            var diagnostic = RunHost(library, host, packed).Diagnostics.Single();
            await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN011");
            await Assert.That(diagnostic.GetMessage()).Contains(reason);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryCallbacksReturningAFeatureCanResolveItsExistingInstance(bool packed)
    {
        const string library = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            public static class Setup
            {
                public static Leaf Run(IModule m, System.Func<IModule, Leaf> callback) => callback(m);
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static bool Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    var leaf = Setup.Run(module, m => m.Configure<Leaf>());
                    module.Apply();
                    return ReferenceEquals(leaf, module.Configure<Leaf>());
                }
            }
            """;
        await RunGeneratedHost(library, host, packed);
    }

    [Test]
    public async Task PackedForwardedFactoryCannotIntroduceAnExplicitSlotBeforeItsFirstUse()
    {
        const string library = """
            using System;
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class Leaf(IModule m) : FeatureBase(m);
            public sealed class Custom(IModule m, int value) : FeatureBase(m);
            public sealed class Root(IModule module) : FeatureBase(module)
            {
                public override void Configure()
                {
                    Install(Module, m => { m.Configure<Custom>(); return new Leaf(m); });
                    Module.Configure(m => new Custom(m, 42));
                }
                private static void Install<T>(IModule m, Func<IModule,T> factory) where T : class, IFeature =>
                    m.Configure(factory);
            }
            """;
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static void Start(IServiceCollection services) => services.CreateModule().Use<Root>();
            }
            """;
        var diagnostic = RunHost(library, host, packed: true).Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN011");
        await Assert.That(diagnostic.GetMessage()).Contains("requires an explicit factory before its first resolution");
    }

    private static GeneratorDriverRunResult RunHost(string library, string host, bool packed)
    {
        var input = packed
            ? Create("Host", ("Host.cs", host)).AddReferences(MetadataReference.CreateFromImage(Emit(Compile(Create("Package", ("Feature.cs", library))))))
            : Create("Host", ("Host.cs", host), ("Feature.cs", library));
        return CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(input, out _).GetRunResult();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task HostRequiresProducerBindingsThatDescribeCurrentCompositionEffects(int schema)
    {
        const string feature = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            public sealed class ExternalFeature(IModule module) : FeatureBase(module);
            """;
        var compiled = Compile(Create("External", ("Feature.cs", feature)));
        var bindings = compiled.SyntaxTrees.Single(tree => tree.ToString().Contains("const int SchemaVersion"));
        var legacy = bindings.WithChangedText(SourceText.From(bindings.ToString().Replace("const int SchemaVersion = 3", $"const int SchemaVersion = {schema}")));
        var package = MetadataReference.CreateFromImage(Emit(compiled.ReplaceSyntaxTree(bindings, legacy)));
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static void Start(IServiceCollection services) => services.CreateModule().Use<ExternalFeature>();
            }
            """;
        var result = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator())
            .Run(Create("Host", ("Host.cs", host)).AddReferences(package), out _).GetRunResult();
        await Assert.That(result.Diagnostics.Single().GetMessage()).Contains("compile the declaring feature library");
    }

    [Test]
    public async Task MalformedProducerSchemaCannotCrashTheHostGenerator()
    {
        const string source = """
            using System;
            using Elsa.Features.Services;
            using Elsa.Features.Abstractions;
            public sealed class ExternalFeature(IModule module) : FeatureBase(module);
            namespace Elsa.FixedFeatures.V1.PExternal
            {
                internal sealed class SlotAttribute : Attribute
                {
                    public SlotAttribute(Type type) { }
                }
                public static class Bindings
                {
                    public const int SchemaVersion = 3;
                    [Slot(typeof(ExternalFeature))] public static void Broken() { }
                }
            }
            """;
        var package = MetadataReference.CreateFromImage(Emit(Create("External", ("Package.cs", source))));
        const string host = """
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class App
            {
                public static void Start(IServiceCollection services) => services.CreateModule().Configure<ExternalFeature>();
            }
            """;
        var input = Create("Host", ("Host.cs", host)).AddReferences(package);
        var result = CompilerTest.CreateDriver(true, new ElsaCompositionHostGenerator()).Run(input, out _).GetRunResult();
        await Assert.That(result.Diagnostics.All(d => d.Id.StartsWith("ELSAGEN", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.GetMessage().Contains("ExternalFeature"))).IsTrue();
    }

    [Test]
    public async Task EditsAndRemovalsProduceTheSameBindingsAsACleanCompilation()
    {
        const string source = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            using System.ComponentModel;
            [Description("old")]
            internal sealed class SampleFeature(IModule module) : FeatureBase(module);
            """;
        var input = Create("Producer", ("Sample.cs", source), ("Other.cs", Dependency));
        var driver = CompilerTest.CreateDriver(new ElsaFeatureGenerator()).Run(input, out _);
        var old = input.SyntaxTrees.First();
        var edited = input.ReplaceSyntaxTree(old, old.WithChangedText(SourceText.From(source.Replace("old", "new"))));
        driver = driver.Run(edited, out var editedOutput);
        await CompareWithClean(edited, editedOutput, driver);
        var removed = edited.RemoveSyntaxTrees(edited.SyntaxTrees.First());
        driver = driver.Run(removed, out var removedOutput);
        await CompareWithClean(removed, removedOutput, driver);
        var empty = removed.RemoveAllSyntaxTrees();
        driver = driver.Run(empty, out _);
        await Assert.That(driver.GetRunResult().GeneratedTrees).IsEmpty();
    }

    [Test]
    public async Task InaccessibleHostRequestPointsToTheCall()
    {
        const string call = "module.Configure<HiddenFeature>()";
        const string source = """
            using Elsa.Features.Abstractions;
            using Elsa.Features.Services;
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public class Host
            {
                private sealed class HiddenFeature(IModule module) : FeatureBase(module);
                public static void Start(IServiceCollection services, ExplicitFeatureCatalog explicitCatalog)
                {
                    services.CreateModule(explicitCatalog).Configure<HiddenFeature>();
                    var module = services.CreateModule();
                    module.Configure<HiddenFeature>();
                }
            }
            """;
        var input = Create("Host", ("Request.cs", source));
        var driver = CompilerTest.CreateDriver(true, new ElsaFeatureGenerator()).Run(input, out var output);
        var diagnostic = driver.GetRunResult().Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN010");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.SourceTree!.FilePath).IsEqualTo("Request.cs");
        await Assert.That(diagnostic.Location.SourceSpan).IsEqualTo(new TextSpan(source.IndexOf(call, StringComparison.Ordinal), call.Length));
        await Assert.That(diagnostic.GetMessage()).Contains("Host.Start");
        await Assert.That(diagnostic.GetMessage()).Contains("Host.HiddenFeature");
        await output.AssertEmits();
    }

    [Test]
    [Arguments("[CustomDependency]", "derived attribute")]
    [Arguments("[DependsOn(typeof(HiddenFeature))]", "accessible, closed feature type")]
    public async Task UnsupportedMetadataIdentifiesTheRequestedFeature(string attribute, string reason)
    {
        const string call = "module.Configure<PluginFeature>()";
        var source = $$"""
            using Elsa.Features.Abstractions;
            using Elsa.Features.Attributes;
            using Elsa.Features.Services;
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                private sealed class HiddenFeature(IModule module) : FeatureBase(module);
                private sealed class CustomDependencyAttribute() : DependsOnAttribute(typeof(HiddenFeature));
                {{attribute}}
                public sealed class PluginFeature(IModule module) : FeatureBase(module);
                public static void Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    module.Configure<PluginFeature>();
                }
            }
            """;
        var input = Create("Host", ("Request.cs", source));
        var diagnostic = CompilerTest.CreateDriver(true, new ElsaFeatureGenerator()).Run(input, out var output)
            .GetRunResult().Diagnostics.Single();
        await Assert.That(diagnostic.Id).IsEqualTo("ELSAGEN010");
        await Assert.That(diagnostic.Location.SourceSpan).IsEqualTo(new TextSpan(source.IndexOf(call, StringComparison.Ordinal), call.Length));
        await Assert.That(diagnostic.GetMessage()).Contains(reason);
        await Assert.That(diagnostic.GetMessage()).Contains("PluginFeature");
        await output.AssertEmits();
    }

    [Test]
    public async Task RebuildingAReferencedFeatureWithCompilerToolingClearsItsRequestDiagnostic()
    {
        const string call = "module.Configure<Infrastructure.DatabaseFeature>()";
        const string source = """
            using Elsa.Features.Services;
            using Elsa.Extensions;
            using Microsoft.Extensions.DependencyInjection;
            public static class Host
            {
                public static void Start(IServiceCollection services)
                {
                    var module = services.CreateModule();
                    module.Configure<Infrastructure.DatabaseFeature>();
                }
            }
            """;
        var library = Create("Infrastructure", ("Dependency.cs", Dependency));
        var plain = MetadataReference.CreateFromImage(Emit(library));
        var host = Create("Host", ("Host.cs", source)).AddReferences(plain);
        var driver = CompilerTest.CreateDriver(true, new ElsaFeatureGenerator()).Run(host, out _);
        var missing = driver.GetRunResult().Diagnostics.Single();
        await Assert.That(missing.Id).IsEqualTo("ELSAGEN010");
        await Assert.That(missing.Location.SourceTree!.FilePath).IsEqualTo("Host.cs");
        await Assert.That(missing.Location.SourceSpan).IsEqualTo(new TextSpan(source.IndexOf(call, StringComparison.Ordinal), call.Length));
        await Assert.That(missing.GetMessage()).Contains("Host.Start");
        await Assert.That(missing.GetMessage()).Contains("Infrastructure.DatabaseFeature");

        var boundLibrary = MetadataReference.CreateFromImage(Emit(Compile(library)));
        var repaired = host.ReplaceReference(plain, boundLibrary);
        driver = driver.Run(repaired, out var output);
        var clean = CompilerTest.CreateDriver(true, new ElsaFeatureGenerator()).Run(repaired, out _);
        await Assert.That(driver.GetRunResult().Diagnostics).IsEmpty();
        await Assert.That(clean.GetRunResult().Diagnostics).IsEmpty();
        await Assert.That(driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).SequenceEqual(
            clean.GetRunResult().GeneratedTrees.Select(t => t.ToString()))).IsTrue();
        await output.AssertEmits();
    }

    private static CSharpCompilation Create(string assembly, params (string, string)[] sources) =>
        CompilerTest.CreateCompilation(sources).WithAssemblyName(assembly).AddReferences(
            MetadataReference.CreateFromFile(typeof(IFeature).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IHostedService).Assembly.Location));

    private static CSharpCompilation Compile(CSharpCompilation input)
    {
        var driver = CompilerTest.CreateDriver(new ElsaFeatureGenerator()).Run(input, out var output);
        if (!driver.GetRunResult().Diagnostics.IsEmpty)
            throw new InvalidOperationException(string.Join(Environment.NewLine, driver.GetRunResult().Diagnostics));
        return output;
    }

    private static Func<FixedFeatureModule, FixedFeatureSlot> Bridge(Assembly assembly, string featureName)
    {
        var method = assembly.GetExportedTypes().Single(t => t.Name == "Bindings").GetMethods()
            .Single(m => m.GetCustomAttributesData().Any(a =>
                a.AttributeType.Name == "SlotAttribute" && ((Type)a.ConstructorArguments[0].Value!).Name == featureName));
        return method.CreateDelegate<Func<FixedFeatureModule, FixedFeatureSlot>>();
    }

    private sealed class TestPlan : FixedFeatureModule
    {
        private readonly FixedFeatureSlot[] _slots;

        public TestPlan(params Func<FixedFeatureModule, FixedFeatureSlot>[] bindings) : base(new ServiceCollection())
        {
            _slots = bindings.Select(binding => binding(this)).ToArray();
            SealDeclarations();
        }

        protected override void ConfigureFeatures()
        {
            foreach (var slot in _slots) slot.Configure();
        }

        protected override void ApplyFeatures()
        {
            foreach (var slot in _slots) slot.Apply();
        }
    }

    private static byte[] Emit(CSharpCompilation compilation)
    {
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        return image.ToArray();
    }

    private static async Task CompareWithClean(CSharpCompilation input, CSharpCompilation output, GeneratorDriver driver)
    {
        var clean = CompilerTest.CreateDriver(new ElsaFeatureGenerator()).Run(input, out var cleanOutput);
        var expected = clean.GetRunResult().GeneratedTrees.Single().ToString();
        await Assert.That(driver.GetRunResult().GeneratedTrees.Single().ToString()).IsEqualTo(expected);
        await Assert.That(driver.GetRunResult().Diagnostics).IsEmpty();
        await output.AssertEmits();
        await cleanOutput.AssertEmits();
    }
}
