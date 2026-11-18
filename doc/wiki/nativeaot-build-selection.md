# Elsa compiler assets and build selection

Use .NET SDK 11.0.100 or later to build projects with the Elsa compiler packages,
including projects targeting `net10.0`. The generator and analyzer require
Roslyn 5.12 or later in the SDK that runs the build.

Reference the feature module your application uses. For example, a package
reference to `Elsa` brings in `Elsa.Features` transitively. To write a feature
library against the Features API alone, reference `Elsa.Features` directly.
The Features package delivers the generator, diagnostics and MSBuild settings
to the C# compiler through NuGet. The analyzer assemblies are build assets.

A library build compiles its existing `IFeature` implementations into a helper
in the library assembly. Pack the library normally and keep its dependency on
Features available to consumers. A host compiled against that package can
import the helper directly from its reference metadata. Rebuild older feature
packages to give them generated helpers. Existing explicit tooling references
can be removed when upgrading to a Features package with bundled tooling.

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

The generator connects calls to the producer helpers at compile time. The first
`AddElsa` or `ConfigureElsa` on a service collection selects the provider.
Subsequent calls share that module and must use the same provider instance.
On a concrete `Module`, `Catalog` exposes the selected catalog and is null for managed discovery.
`services.CreateModule()` and `services.CreateModule(policy)` each create a
fresh module. An app-source `new Module(services)` or a delegate to an ordinary
creation method gets a diagnostic in a generated host. A previously compiled
wrapper can accept a catalog explicitly. Precompiled `UseX(IModule)` helpers
receive the module selected by their caller.

The generated producer helper supplies closed feature constructor calls, inherited
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

## Inspect a generated factory in Rider

Open the project that declares the feature and expand **Dependencies → Source
Generators → ElsaFeatureGenerator → ElsaFeatureRegistrations.g.cs**. For
`IdentityFeature`, open `Elsa.Identity`. A generated server uses
`ElsaFeatureInterceptors.g.cs` in the host project to call the producer helpers.
If the constructor usage popup reports a result outside **Solution**, expand the
search scope to **All Places**. For an installed NuGet module, follow the helper
call from the host interceptor into the compiled module in Rider.
[Rider source generator navigation](https://www.jetbrains.com/help/rider/Source_generators.html)

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

## Run the package validation

```bash
project=test/integration/Elsa.NativeAot.IntegrationTests/Elsa.NativeAot.IntegrationTests.csproj
dotnet restore "$project" --locked-mode
dotnet test --project "$project" -c Release --no-restore -- --treenode-filter '/*/*/ManagedCodeGenerationTests/*'
dotnet test --project "$project" -c Release --no-restore -- --treenode-filter '/*/*/CodeGenerationTests/*'
```

The packed-feature cases compile a module against the installed Features package, pack that module, and install it in a fresh consumer. The consumer references the module and runs on each target framework. The tests check provider selection for managed and native executables, diagnostics for invalid inputs, generated source, propagation to child projects, and incremental mode changes in the same build directory.

Inspect packages, build logs, generated sources and publish outputs under `artifacts/nativeaot/<framework>/`. For host prerequisites and the complete acceptance category, see the [probe guide](nativeaot-probes.md#run-the-tests).
