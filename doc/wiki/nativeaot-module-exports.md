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

A consumer may freeze its own fragment with a strong feature dependency on the
producer's public feature type:

```csharp
var application = new ExplicitFeatureCatalogBuilder()
    .Add(m => new ApplicationFeature(m),
        new FeatureDescriptor("Application", "Example", "Application"),
        dependencies: [typeof(FoundationFeature)])
    .CreateExport(new ModuleExportDescriptor("example.application", 1,
        requirements: [new ModuleRequirement("example.foundation", 1,
            requiredCapabilities: ["example.foundation-service"])]));

var catalog = new FeatureModuleCatalogBuilder()
    .Add(application)
    .Add(ProducerExports.Foundation)
    .Build();
var module = services.CreateModule(catalog, CompositionPolicy.ValidatedV1);
module.Configure<ApplicationFeature>();
module.Apply();
```

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

Existing workflow and payload data keep their formats. Later plan steps cover
generation of feature declarations and richer authoring diagnostics.

The compiler asset packages can select the host bootstrap at build time.
[Build selection and compiler assets](nativeaot-build-selection.md)

## Validate producer/consumer boundaries

The Features probe links the reusable consumer scenario and references a
separately compiled producer:

```bash
scripts/nativeaot/run.sh --probe features --provider explicit-catalog --mode native --framework net11.0
```

For actual package consumption, run:

```bash
scripts/nativeaot/test-module-packages.sh
```

The default package matrix covers managed/native execution on both frameworks
and a separately repacked .NET 11 native runtime-async opt-out. The harness stages
one NuGet package per referenced project, creates a fresh consumer outside the
repository, and verifies the effective Release compiler settings for each packed
and published target. It generates and verifies one complete two-framework lock
per runtime-async model and publication mode, checking exact package versions
and SHA512 provenance after locked restore. The consumer uses package
references with explicit local-feed mappings, not source project references or
repository build imports. Native linker/compiler IL warnings are errors.

Evidence is retained under `artifacts/nativeaot/module-packages/run.*`: staged
packages, package layouts, source snapshots and external-consumer paths,
SDK/toolchain and evaluated properties, locks, provenance checks, logs/binlogs,
and per-lane/aggregate results. Failure controls reject an unavailable package,
an injected assertion failure, and malformed output. The fixture tests real
feature/service/lifecycle behavior and two isolated hosts. It does not establish
source-generator output or whole-worker capability parity. Native execution must
run on the target OS; macOS results do not establish Linux support.
