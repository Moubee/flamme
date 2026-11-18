# Versioned module exports

Module exports let a compiled producer supply typed feature declarations to a
consumer before startup. They use the existing explicit catalog and module
lifecycle. The shared contracts live in
[Elsa.CodeGeneration.Abstractions](../../src/common/Elsa.CodeGeneration.Abstractions);
feature bindings remain in [Elsa.Features](../../src/common/Elsa.Features).
The shared assembly has no Features, hosting, Workflow Core, Roslyn, or serializer
dependency.

These runtime contracts define the typed exports consumed by authored hosts and
compiler-selected bootstraps.

## Identity and compatibility

| Fact | Contract |
| --- | --- |
| `ModuleId` | Explicit stable identity, ordinal and case-sensitive, nonblank without surrounding whitespace. |
| `SchemaVersion` | Export structure; the current runtime supports schema 1 exactly. Unsupported positive schemas are rejected at composition. |
| `ContractVersion` | Positive numeric module contract revision. A requirement matches exactly; revision 2 does not implicitly satisfy revision 1. |
| NuGet package version | Independent deployment/versioning input. It does not select the module revision or schema. |
| Capabilities | Copied stable string identities describing declared bindings available to a consumer. They do not activate features or certify native coverage. |

`ModuleExportDescriptor` and `ModuleRequirement` copy their input collections.
Requirements can also name capabilities that the selected dependency must declare.
Duplicate requirement IDs or capability IDs are rejected. Missing requirements,
wrong revisions, unsupported schemas, and cycles fail before feature factories,
lifecycle callbacks, or hosted-service construction.

## Producer entry point

Publish an explicitly callable immutable export object from its owning assembly:

```csharp
public static class ProducerExports
{
    public static FeatureModuleExport Foundation { get; } =
        new ExplicitFeatureCatalogBuilder()
            .Add(m => new FoundationFeature(m),
                new FeatureDescriptor("Foundation", "Example", "Foundation"))
            .AddHostedService<FoundationWorker>()
            .CreateExport(new ModuleExportDescriptor(
                "example.foundation", contractVersion: 1,
                capabilities: ["example.foundation-service"]));
}
```

The required namespaces are `Elsa.CodeGeneration`, `Elsa.Features.Models`, and
`Elsa.Features.Services`. The feature and worker implementations stay in the
producer. Its factory receives the consuming `IModule`; configuration values,
instances, services, and scopes belong to that host. A static immutable export
stores declarations, not host state or a process-global registration.

`CreateExport` freezes a fragment while allowing strong feature dependencies owned
by another producer. It checks local identities and cycles without requiring
external feature declarations locally. Ordinary `ExplicitFeatureCatalogBuilder.Build()`
continues to require a complete graph. The consumer's final composition checks
all fragments together; copying dependency features into every producer is not
needed.

See the [compiled producer fixture](../../test/fixtures/Elsa.NativeAot.Modules.Producer/ProducerExports.cs)
for a complete example with runtime configuration, scoped services, and a hosted
service. No assembly scanning, module initializer, runtime generic construction,
or registration callback is used to import its bindings.

## Host selection and ordering

For a host that selects versioned exports explicitly, put these properties in the
executable project. Both managed and native builds then use its authored export
entry point.

```xml
<PropertyGroup>
  <ElsaExecutionMode>Generated</ElsaExecutionMode>
  <ElsaHostBootstrap>Manual</ElsaHostBootstrap>
</PropertyGroup>
```

Declare the entry point and the host’s own export in the application. The
example declares a requirement on the foundation provider and a strong
dependency on its public feature.

```csharp
using Elsa.Features.Models;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.CodeGeneration;

internal static partial class ElsaHost
{
    internal static IModule CreateModule(IServiceCollection services, CompositionPolicy policy)
        => FeatureModuleCatalogBuilder.CreateModule(services, GetModuleExports(), policy);

    private static FeatureModuleExport[] GetModuleExports()
        => [ApplicationExports.Application, ProducerExports.Foundation];
}

internal static class ApplicationExports
{
    internal static FeatureModuleExport Application { get; } = new ExplicitFeatureCatalogBuilder()
        .Add(m => new ApplicationFeature(m),
            new FeatureDescriptor("Application", "Example", "Application"),
            dependencies: [typeof(FoundationFeature)])
        .CreateExport(new ModuleExportDescriptor("example.application", 1,
            requirements: [new ModuleRequirement("example.foundation", 1,
                requiredCapabilities: ["example.foundation-service"])]));
}
```

Create the module with `ElsaHost.CreateModule(services, CompositionPolicy.ValidatedV1)`,
configure the selected features, then call `module.Apply()`. The package consumer
fixture also shows how a second host can reuse the first host’s immutable catalog
with separate feature and service instances. [Authored entry point](../../test/fixtures/nativeaot/module-consumer/ElsaHost.cs)
and [two-host consumer](../../test/fixtures/nativeaot/module-consumer/ModuleConsumerScenarios.cs).

Composition validates module dependencies before merging typed bindings. Modules
are ordered dependency-first; among ready modules, the earliest first-add slot
wins. A replacement retains that slot. Within each fragment, authored declaration
order is retained. Dictionary or assembly enumeration order never selects an
implementation.

Cross-export strong feature dependencies require the owning export in the
requester's declared requirement closure, directly or transitively. Conditional
`DependencyOf` targets may be absent and do not create module requirements.
Selecting an export makes its bindings available; it does not automatically
configure every feature in that export. The selected [composition policy](module-system.md#explicit-feature-composition)
still controls conditional and late-feature admission independently.

The final catalog rejects collisions in CLR feature types, installed feature
identities, and hosted-service implementation types, naming both exporting owners.
It then checks the complete strong feature graph. Composition invokes no feature
factory or lifecycle callback. Runtime `Configure<T>(factory, action)` continues
to provide per-host construction and configuration overrides for declared types.

## Repeated imports and explicit replacement

Only re-adding the currently selected **same export object** is idempotent. A
different object claiming the same module ID is an error, even if its revision or
metadata matches. Metadata equality cannot prove factory equivalence. A producer
can expose the immutable singleton shown above for repeated explicit references.

Use `Replace(expectedExport, replacementExport)` for deliberate replacement. The
expected object must be the current selection and the replacement must retain the
same logical module ID. All final revision/capability requirements, feature
dependencies, and binding ownership are revalidated. An already-built registry or
catalog remains unchanged. `Add(A)`, `Replace(A, B)`, then `Add(A)` is a conflict;
it cannot silently restore an obsolete export.

Adding distinct exports appends declarations subject to dependency ordering.
There is no implicit last-wins binding override. Replacing an entire export and
per-host `Configure<T>(factory)` are the supported explicit override boundaries.

## Public API and migration inventory

Existing `AddElsa`, `IModule`, `Module`, explicit catalog, and hosted-service APIs
remain valid. Callers need no migration unless they opt into versioned exports.

| Owning assembly | Added public surface |
| --- | --- |
| `Elsa.CodeGeneration.Abstractions` | `ModuleExport`, `ModuleExportDescriptor`, `ModuleRequirement`, `ModuleExportSchema`, `ModuleExportRegistryBuilder<TExport>`, `ModuleExportRegistry<TExport>`. |
| `Elsa.Features` | `FeatureModuleExport`, `FeatureModuleCatalogBuilder`, and `ExplicitFeatureCatalogBuilder.CreateExport`. |

For a modular producer, replace its local final `Build()` with `CreateExport` and
declare external module revisions/capabilities explicitly. In the host, select
exports, build the combined catalog, then create/configure the module as before.
Keep feature factories and state host-local. Export revisions are not inferred
from assembly or package versions.

A feature library can also publish compiler-generated registrations from its
existing feature declarations. Its dependency on `Elsa.Features` supplies the
compiler settings to the consuming host.
[Build selection and compiler assets](nativeaot-build-selection.md)

## Validate producer/consumer boundaries

The explicit Features probe references the separately compiled producer. The package tests also install that producer into a fresh consumer outside the checkout. Run them from the repository root on a host with the native compiler and both runtimes installed.

```bash
project=test/integration/Elsa.NativeAot.IntegrationTests/Elsa.NativeAot.IntegrationTests.csproj
dotnet restore "$project" --locked-mode
dotnet test --project "$project" -c Release --no-restore -- --treenode-filter '/*/*/ModulePackageTests/*'
```

The published consumer exercises feature registration, service lifetimes, and isolation between two hosts. Cases cover managed and native publication on .NET 10 and .NET 11, including .NET 11 with runtime async disabled in both the producer and consumer. The .NET 11 suite also publishes both framework targets from the same locked restore and intermediate directory. Each case checks Release compiler settings, performs a locked restore covering both target frameworks, and verifies the installed package version and SHA-512 hash against the staged archive. The empty-feed control checks the restore diagnostic for a missing producer. An injected failure checks that assertions in the published process reach the test runner.

Logs, evaluated properties and build artifacts are under `artifacts/nativeaot/<framework>/`. The [probe guide](nativeaot-probes.md#run-the-tests) covers host prerequisites and the full acceptance category.
