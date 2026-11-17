# Module System

The module system is the backbone of Elsa. It is a thin abstraction over `IServiceCollection` that lets packages register cohesive feature sets with dependency ordering.

## Core Types

| Type | File | Role |
| --- | --- | --- |
| `IModule` | [src/common/Elsa.Features/Services/IModule.cs](../../src/common/Elsa.Features/Services/IModule.cs) | Holds `IServiceCollection`, module properties, configured features, hosted service descriptors, and `Apply()`. |
| `Module` | [src/common/Elsa.Features/Implementations/Module.cs](../../src/common/Elsa.Features/Implementations/Module.cs) | Concrete feature graph builder and applier. |
| `IFeature` | [src/common/Elsa.Features/Services/IFeature.cs](../../src/common/Elsa.Features/Services/IFeature.cs) | Feature lifecycle contract. |
| `FeatureBase` | [src/common/Elsa.Features/Abstractions/FeatureBase.cs](../../src/common/Elsa.Features/Abstractions/FeatureBase.cs) | Base class for most code-first features. |
| `DependsOnAttribute` | [src/common/Elsa.Features/Attributes/DependsOn.cs](../../src/common/Elsa.Features/Attributes/DependsOn.cs) | Declares feature dependencies. |
| `DependencyOfAttribute` | [src/common/Elsa.Features/Attributes/DependencyOf.cs](../../src/common/Elsa.Features/Attributes/DependencyOf.cs) | Declares optional dependency relationships. |

## Lifecycle

Feature classes usually use three lifecycle methods:

1. `Configure()`: declare additional feature configuration, scan activities, or add endpoint assemblies.
2. `ConfigureHostedServices()`: register hosted services with optional priority.
3. `Apply()`: add concrete services, options, stores, handlers, endpoints, and providers to DI.

`Module.Apply()` topologically sorts configured features and dependencies, configures them once, filters conditional features, and remembers the hosted-service insertion point. It applies feature registrations, inserts newly queued hosted-service descriptors at that point, and publishes installed-feature metadata.

A feature may introduce another feature from `Apply()`, for example by calling `Module.Configure<OtherFeature>()` directly or through a helper such as `AddActivity<T>()`. `Module.Apply()` keeps applying until no new features show up, so the introduced feature (and its dependencies) are configured and applied as well.

## Entry Points

The common public path is:

```csharp
services.AddElsa(elsa =>
{
    elsa
        .UseWorkflowManagement()
        .UseWorkflowRuntime()
        .UseWorkflowsApi();
});
```

Implementation links:

- [AddElsa and ConfigureElsa](../../src/modules/Elsa/Extensions/DependencyInjectionExtensions.cs)
- [CreateModule and Use<T>](../../src/common/Elsa.Features/Extensions/DependencyInjectionExtensions.cs)
- [ElsaFeature](../../src/modules/Elsa/Features/ElsaFeature.cs)
- [AppFeature](../../src/modules/Elsa/Features/AppFeature.cs)

`AppFeature` is a small wrapper that lets application-specific configuration run after the default `ElsaFeature` dependencies.

## Feature Dependencies

Feature dependencies are explicit attributes. Examples:

- [WorkflowsFeature](../../src/modules/Elsa.Workflows.Core/Features/WorkflowsFeature.cs) depends on system clock, expressions, mediator, default formatters, multitenancy, and commit strategies.
- [WorkflowManagementFeature](../../src/modules/Elsa.Workflows.Management/Features/WorkflowManagementFeature.cs) depends on string compression, mediator, memory cache, system clock, workflows, workflow definitions, and workflow instances.
- [WorkflowsApiFeature](../../src/modules/Elsa.Workflows.Api/Features/WorkflowsApiFeature.cs) depends on workflow instances, management, runtime, and SAS tokens.

This is why feature classes are the best way to learn a module. They encode its runtime assumptions.

## Explicit feature composition

`ExplicitFeatureCatalogBuilder` supplies factories, installed-feature metadata,
strong dependencies, and conditional-presence keys without reading attributes.
Build the catalog before creating the module:

```csharp
var catalog = new ExplicitFeatureCatalogBuilder()
    .Add(m => new ApplicationFeature(m),
        new FeatureDescriptor("Application", "Example", "Application"))
    .Build();
var module = services.CreateModule(catalog, CompositionPolicy.ValidatedV1);
module.Configure<ApplicationFeature>();
module.Apply();
```

The builder snapshots input metadata and collections. `Build()` rejects duplicate
CLR types or installed identities, missing strong dependencies, open/abstract
feature types, and dependency cycles. Conditional targets may be absent; their
presence filters a feature rather than installing the target. Catalogs are
immutable and reusable across modules; factories create each module's instances.
Installed descriptors are copied for each module. Runtime configuration values
and `Configure` actions remain dynamic, and a custom factory can override a
cataloged type's construction. Its result must have the exact declared type.

The provider and admission policy are independent:

| Policy | Behavior with either provider |
| --- | --- |
| `LegacyV1` (default) | Preserves the existing single-pass initial conditional filter, late additions, and repeated `Apply` behavior. |
| `ValidatedV1` | Filters conditionals to a fixed point and rejects surviving strong dependencies whose targets were filtered out, for both initial and late batches. |

`new Module(services, policy)` selects managed discovery with the same chosen
policy. `new Module(services, catalog, policy)` and the corresponding `CreateModule`
overload use only catalog metadata. They never fall back to managed discovery.
Neither NativeAOT publication nor the provider implicitly selects a new policy.
Within either policy, `Configure` and `ConfigureHostedServices` run once per
instance; every `Apply` batch reapplies the surviving instances once.

An explicit module, or a managed module using `ValidatedV1`, becomes faulted after
an admission, factory, or callback failure. Further composition requires a fresh
module and service collection. Catching a nested admission error inside a feature
cannot resume a faulted module. Arbitrary callback effects are not rolled back.
The existing default managed path retains its legacy failure behavior.

This is the runtime contract for future generated manifests. The
[NativeAOT component probe](nativeaot-probes.md) exercises handwritten catalogs;
source generation and full Workflow Core/native-host support remain separate work.

### Versioned producer exports

For separately compiled modules, freeze declarations with `CreateExport`, select
them through `FeatureModuleCatalogBuilder`, and build one complete host catalog.
Module schema/revision/capability and binding ownership checks run before feature
activation. Export fragments allow external strong dependencies; ordinary catalog
`Build()` remains strict. See [versioned module exports](nativeaot-module-exports.md)
for the producer/consumer contract, exact replacement rules, and API inventory.

### Hosted services in a catalog

Declare every queued hosted-service implementation with
`builder.AddHostedService<Worker>()` before building the catalog. This records a
closed singleton descriptor with its implementation identity and required public
constructor metadata. The declaration alone does not install or instantiate the
service. Features still call `ConfigureHostedService<Worker>(priority)` or the
covered `Type` overload at runtime; unknown types fail before activation.

Module registrations preserve stable priority ordering and implementation-type
deduplication, including services registered directly during `Apply`. Existing
direct duplicates retain their original descriptor and position; newly added
module descriptors occupy the remembered insertion point. Repeated Apply does
not create duplicate hosted-service descriptors. Duplicate declarations in the
catalog are rejected, while a frozen catalog can serve multiple independent hosts.

Use DI scope validation to reject singleton constructors that capture scoped
services. A hosted service that needs scoped work should create and dispose an
owned scope through `IServiceScopeFactory` during execution. The
[architecture decision](../adr/2026-11-17-explicit-feature-composition.md)
records this provider, policy, and hosted-service boundary.

## Module Properties

`IModule.Properties` is used as a shared bag during feature configuration. A concrete example is FastEndpoints assembly collection in [Elsa.Api.Common/Extensions/ModuleExtensions.cs](../../src/common/Elsa.Api.Common/Extensions/ModuleExtensions.cs). Features call `AddFastEndpointsAssembly`, and later `AddFastEndpointsFromModule` registers all collected assemblies with FastEndpoints.

## Static Host Composition

Hosts select modules through code-first `AddElsa` registration and deploy the resulting application as a versioned image. The former configuration-driven shell feature system, runtime package loading, and independent shell reload APIs have been removed. Register custom activities and modules at build time and roll out a new host version when composition changes.

## Extension Method Pattern

Modules expose fluent extension methods in `Extensions/ModuleExtensions.cs` or related files. The method usually calls `module.Configure<TFeature>()` and returns `IModule`:

```csharp
public static IModule UseWorkflowsApi(this IModule module, Action<WorkflowsApiFeature>? configure = default)
{
    module.Configure(configure);
    return module;
}
```

When adding a new module, follow this shape:

- one `Features/*Feature.cs`
- one `Extensions/ModuleExtensions.cs`
- tests that prove the feature registers its core contracts

## Common Pitfalls

- Do not register services in extension methods when the module already has a feature class. Put service registration in `Apply()`.
- Do not bypass dependencies with direct service provider access in unrelated modules. Add a contract and dependency if the relationship is real.
- Use `TryAdd*` for overridable defaults and normal `Add*` for deliberate multiple registrations such as handlers, validators, and descriptors.
- If a feature uses `Module.Configure<OtherFeature>()`, verify that the other feature is already a dependency or that optional behavior is intentional. Introducing a feature this way works from `Configure()` and from `Apply()`, but declaring `[DependsOn(typeof(OtherFeature))]` keeps the relationship visible and gives the dependency the normal ordering.
