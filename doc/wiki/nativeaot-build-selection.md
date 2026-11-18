# Elsa compiler assets and build selection

Use .NET SDK 11.0.100 or later to build projects with the Elsa compiler packages,
including projects targeting `net10.0`. The generator and analyzer require
Roslyn 5.12 or later in the SDK that runs the build.

Applications and module libraries can opt in to the compiler tooling with explicit
references to `Elsa.CodeGeneration.Build`, `Elsa.CodeGeneration` and
`Elsa.CodeGeneration.Analyzers`. Use matching package versions and set
`PrivateAssets="all"` to keep compiler tooling out of upstream package
dependencies. Reference `Elsa.Features` and any producer modules separately for
the runtime APIs used by the host.

The Build package contributes MSBuild imports under `buildTransitive`. The other
two packages contribute C# compiler assemblies under `analyzers/dotnet/cs`.
The neutral registration compiler ships alongside the Elsa generator as a build
asset. Each participating library publishes its own feature registration helper
and assembly marker in its compile assets.

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
with `ElsaIsHost=true` or `false`. Libraries can enable the compiler tooling
without declaring a host. Prebuilt producers supply the exports that were
compiled into their packages.

Set mode and generation properties on each project that needs them, in shared
MSBuild imports, or on the command line. An app-local property only affects the
application. A global `-p:ElsaExecutionMode=Generated` also reaches source
project references that import the build tooling, subject to MSBuild property
propagation rules.

## Use compiled features

In a host with the compiler package references above, enable compiled composition
for a managed build with this project setting.

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

The generator connects calls to the producer helpers at compile time. The first
`AddElsa` or `ConfigureElsa` on a service collection selects the provider.
Subsequent calls share that module and must use the same provider instance.
On a concrete `Module`, `Catalog` exposes the selected catalog and is null for managed discovery.
`services.CreateModule()` and `services.CreateModule(policy)` each create a
fresh module. An app-source `new Module(services)` or a delegate to an ordinary
creation method gets a diagnostic in a generated host. A previously compiled
wrapper can accept a catalog explicitly. Precompiled `UseX(IModule)` helpers
receive the module selected by their caller.

A participating library supplies closed feature constructor calls, inherited
dependency and installed-feature metadata, and typed hosted-service descriptors.
These bindings are available as soon as the host references the library. The
application still selects features, supplies options and custom factories, and
runs their callbacks. Use `Configure<T>(factory, action)` when construction
requires other arguments. Feature classes added in the host participate too.

The native acceptance consumer uses only Features. A reachable constructor can
introduce a native compiler requirement even for a feature the application
never selects. Keep optional features with dynamic-code requirements in
separately referenced libraries when native consumers need to omit them.
[Run the packed consumer](nativeaot-probes.md#run-the-tests)

## Continue using authored module exports

An existing host with an authored partial entry point can keep its versioned
module exports. [Versioned module exports](nativeaot-module-exports.md)

This example uses managed discovery in Dynamic builds and an explicitly selected
producer export in Generated builds.

```csharp
using Elsa.Extensions;
using Elsa.Features.Models;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.CodeGeneration;

internal static partial class ElsaHost
{
    internal static partial IModule CreateModule(
        IServiceCollection services, CompositionPolicy policy);

    private static IModule CreateDynamicModule(
        IServiceCollection services, CompositionPolicy policy)
        => services.CreateModule(policy);

    private static FeatureModuleExport[] GetModuleExports()
        => [ProducerExports.Foundation];
}
```

Call `ElsaHost.CreateModule(services, policy)` and configure and apply the module
as usual. The C# generator implements the partial method with one dispatch path.
Generated mode builds a catalog from the selected exports and passes it and the
requested policy to the same runtime lifecycle used for explicit composition.
It requires a nonempty typed export selection. Catalog and module validation
reject incompatible versions, missing bindings, and ownership conflicts.

The compiler assets check the host shape and the hook required by the selected
mode. The generator reports missing compiler hooks during source generation.
The build reports missing compiler assets and treats asset load failures as errors.
A project that disables generation can use `ElsaHostBootstrap=Manual` and provide
an ordinary `CreateModule` method with the same signature. Use the compilation
symbols to return the selected call with the original services and policy:

```csharp
internal static IModule CreateModule(IServiceCollection services, CompositionPolicy policy)
{
#if ELSA_GENERATED
    return FeatureModuleCatalogBuilder.CreateModule(services, GetModuleExports(), policy);
#else
    return CreateDynamicModule(services, policy);
#endif
}
```

The analyzer validates this return shape. The shared factory checks the concrete
exports and builds the catalog before constructing the module. Supply an explicit
export for each selected capability.

These compiler assets establish the build and host bootstrap contract.
Generating feature declarations and checking richer authoring shapes are
subsequent plan steps.

## Run the package validation

```bash
project=test/integration/Elsa.NativeAot.IntegrationTests/Elsa.NativeAot.IntegrationTests.csproj
dotnet restore "$project" --locked-mode
dotnet test --project "$project" -c Release --no-restore -- --treenode-filter '/*/*/ManagedCodeGenerationTests/*'
dotnet test --project "$project" -c Release --no-restore -- --treenode-filter '/*/*/CodeGenerationTests/*'
```

These TUnit cases pack the generator, analyzer, build assets and runtime libraries with the repository SDK. An external consumer installs those packages and runs on each target framework. The tests check provider selection for managed and native executables, diagnostics for invalid inputs, generated source, propagation to child projects, and incremental mode changes in the same build directory.

Inspect packages, build logs, generated sources and publish outputs under `artifacts/nativeaot/<framework>/`. For host prerequisites and the complete acceptance category, see the [probe guide](nativeaot-probes.md#run-the-tests).
