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
The tooling adds no runtime assembly and does not add package references to the
consumer. Producer assemblies expose typed exports through the versioned export
contract. [Versioned module exports](nativeaot-module-exports.md)

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

## Author a host bootstrap

The default `ElsaHostBootstrap=Compiler` uses an authored partial entry point.
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
scripts/nativeaot/test-codegen-build.sh
```

The harness stages actual packages and builds fresh consumers using the
repository SDK. It writes packages, logs, and results under
`artifacts/nativeaot/codegen-build/run.*`. It exercises mode selection and failure cases, validates
property propagation to source project references, and switches a compiled
consumer between modes without cleaning. Managed and native fixture runs
assert which module provider the application used.
