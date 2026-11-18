# Elsa compiler assets and build selection

Use .NET SDK 11.0.100 or later to build projects with the Elsa compiler packages,
including projects targeting `net10.0`. The generator and analyzer require
Roslyn 5.12 or later in the SDK that runs the build.

Reference the feature module your application uses. For example, a package
reference to `Elsa` brings in `Elsa.Features` transitively. To write a feature
library against the Features API alone, reference `Elsa.Features` directly.
The Features package delivers the generator, diagnostics and MSBuild settings
to the C# compiler through NuGet. The analyzer assemblies are build assets.

A library build emits a constructor binding for each supported closed `IFeature`
and publishes the feature selections made by its configuration helpers in assembly
metadata. The host compiler can read this information from a NuGet reference
assembly. Pack the library normally and keep its dependency on Features available
to consumers. Rebuild feature packages compiled with the earlier catalog
generator. Existing explicit tooling references can be removed when upgrading to
a Features package with bundled tooling.

In this repository, libraries under `src/modules` and hosts under `src/apps`
get the same tooling from shared build settings. A new feature module in that
layout needs only its usual runtime project references.

## Choose a composition mode

| Requested `ElsaExecutionMode` | `PublishAot` true | `PublishAot` false or unset |
| --- | --- | --- |
| `Auto` (default) | Generated | Dynamic |
| `Generated` | Generated | Generated |
| `Dynamic` | Build error | Dynamic |

`PublishTrimmed` leaves Elsa's mode selection unchanged. The resolved
`ElsaEffectiveExecutionMode` reaches the compiler through the SDK-generated
editorconfig. Mode and metadata changes invalidate the compiler inputs when
switching in the same output directory. A host also receives one of the
`ELSA_GENERATED` and `ELSA_DYNAMIC` compilation symbols.

`ElsaGenerateMetadata` defaults to `true` and is independent of the mode.
Executable projects are hosts by default. A project can override host detection
with `ElsaIsHost=true` or `false`. Libraries generate registrations in the
default mode so the same package can serve both managed discovery and generated
hosts.

Set mode and generation properties on each project that needs them, in shared
MSBuild imports, or on the command line. An app-local property only affects the
application. A global `-p:ElsaExecutionMode=Generated` also reaches source
project references that import the build tooling, subject to MSBuild property
propagation rules.

## Use compiled features

To enable compiled composition in a managed host, add this project setting.

```xml
<PropertyGroup>
  <ElsaExecutionMode>Generated</ElsaExecutionMode>
</PropertyGroup>
```

Use the ordinary SDK entry points in application source.

```csharp
services.ConfigureElsa(elsa => elsa.UseWorkflows());
services.AddElsa();
```

The host compiler determines the complete set of features and their order. Each
generated module owns its feature instances and calls their configuration and
application methods in that order. A package reference alone selects zero roots.
The ordinary SDK path includes `AppFeature` and its dependencies. A direct
`services.CreateModule()` starts with the selections made for that module.
`HasFeature<T>()` reports final membership as soon as the module is returned.

Select features unconditionally through `UseX`, `Use<T>` or `Configure<T>`.
Use runtime values for feature options. The following host reads scripting
options at startup.

```csharp
services.AddElsa(elsa => elsa.UseCSharp(csharp =>
    csharp.CSharpOptions += options => configuration.GetSection("Scripting:CSharp").Bind(options)));
```

A runtime flag that changes feature membership must move to a build-time choice
such as `#if`, or the application must choose between separately compiled host
entry points. The generator reports `ELSAGEN012` for unsupported runtime
selection, and `ELSAGEN011` for an invalid fixed graph. Calls inside
`Configure` and `Apply` can configure a selected feature. When opaque code
requests an absent type, the fixed module throws before invoking the factory
or configuration action. A custom factory receives the actual host module.
A package helper can select a feature without a default constructor by calling
`module.Configure(m => new CustomFeature(m, value))` unconditionally.

`ConfigureElsa` and `AddElsa` share a module for the same service collection.
`CreateModule` returns a fresh module on each invocation. Keep related registrations
on a visibly shared service collection so the compiler can associate them.
Application code can pass a configured module into a previously compiled helper.
Module creation inside a precompiled helper must itself use a generated plan.

When a helper configures two independent modules, the host compiler can follow
the receiver and parameters in an available source body. Compiled package
helpers expose one combined selection summary. Call a package helper separately
for each module when its parameters would otherwise target different modules.
The compiler reports `ELSAGEN012` if it cannot assign a helper's effects to
the correct module. Passing the same module through several parameters is supported.

A source or package helper can forward its method type parameter directly.
For example, `Select<T>(IModule module)` can call `module.Use<T>()`. Calling
`Select<Wrapper<int>>(module)` selects the closed feature `Wrapper<int>`.
If the helper itself calls `module.Use<Wrapper<T>>()`, the compiler reports
`ELSAGEN012`. Pass the complete feature type to the helper at the call site.

The native acceptance consumer tests both sides of constructor reachability.
An unselected optional producer with a dynamic-code constructor can stay
referenced while the application publishes. Selecting that feature exposes
the constructor's native compilation requirement.
[Run the packed consumer](nativeaot-probes.md#run-the-tests)

## Inspect a generated factory in Rider

Open the project that declares the feature and expand **Dependencies → Source
Generators → ElsaFeatureGenerator → ElsaFeatureRegistrations.g.cs**. For
`IdentityFeature`, open `Elsa.Identity`. A generated server uses
`ElsaFeatureInterceptors.g.cs` in the host project to call the producer helpers.
For example, the web server's generated `ConfigureFeatures()` contains these
consecutive calls. Its `ApplyFeatures()` calls `Apply()` in the same order.

```csharp
_workflowsFeature.Configure();
_flowchartFeature.Configure();
_workflowRuntimeFeature.Configure();
```

If the constructor usage popup reports a result outside **Solution**, expand the
search scope to **All Places**. For an installed NuGet module, follow the helper
call from the host interceptor into the compiled module in Rider.
[Rider source generator navigation](https://www.jetbrains.com/help/rider/Source_generators.html)

## Migrate an authored bootstrap

If the build reports `ELSAGEN013`, replace the compiler-implemented `ElsaHost.CreateModule(services, policy)` and
its `GetModuleExports()` hook with the ordinary `services.CreateModule(policy)`
call and typed feature selections in application source. The policy for a fixed
graph must be known at build time. Keep module-specific configuration in its
existing feature classes. To inspect the resulting feature order, open the
generated `ElsaFeatureInterceptors.g.cs` in the host project.

The former `AddAvailable` and `BuildAvailable` registration APIs have been
removed. A generated host uses ordinary feature selections. A handwritten
catalog can use `Add` and `Build` to validate its explicitly declared graph.
Calling a mutable catalog creation overload from compiler-generated mode
reports `ELSAGEN014`.

Applications that explicitly manage versioned catalogs can continue to own
their entire entry point with `ElsaHostBootstrap=Manual`. That handwritten
API has its own composition and admission contract.
[Versioned module exports](nativeaot-module-exports.md)

## Run the package validation

```bash
project=test/integration/Elsa.NativeAot.IntegrationTests/Elsa.NativeAot.IntegrationTests.csproj
dotnet restore "$project" --locked-mode
dotnet test --project "$project" -c Release --no-restore -- --treenode-filter '/*/*/ManagedCodeGenerationTests/*'
dotnet test --project "$project" -c Release --no-restore -- --treenode-filter '/*/*/CodeGenerationTests/*'
```

The packed-feature cases compile a module against the installed Features package, pack that module, and install it in a fresh consumer. The consumer references the module and runs on each target framework. The tests check provider selection for managed and native executables, diagnostics for invalid inputs, generated source, propagation to child projects, and incremental mode changes in the same build directory.

Inspect packages, build logs, generated sources and publish outputs under `artifacts/nativeaot/<framework>/`. For host prerequisites and the complete acceptance category, see the [probe guide](nativeaot-probes.md#run-the-tests).
