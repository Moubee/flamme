# Module System

The module system is the backbone of Flamme. It is a thin abstraction over `IServiceCollection` that lets packages register cohesive feature sets with dependency ordering.

## Core Types

| Type | File | Role |
| --- | --- | --- |
| `IModule` | [src/common/Flamme.Features/Services/IModule.cs](../../src/common/Flamme.Features/Services/IModule.cs) | Holds `IServiceCollection`, module properties, configured features, hosted service descriptors, and `Apply()`. |
| `Module` | [src/common/Flamme.Features/Implementations/Module.cs](../../src/common/Flamme.Features/Implementations/Module.cs) | Concrete feature graph builder and applier. |
| `IFeature` | [src/common/Flamme.Features/Services/IFeature.cs](../../src/common/Flamme.Features/Services/IFeature.cs) | Feature lifecycle contract. |
| `FeatureBase` | [src/common/Flamme.Features/Abstractions/FeatureBase.cs](../../src/common/Flamme.Features/Abstractions/FeatureBase.cs) | Base class for most code-first features. |
| `DependsOnAttribute` | [src/common/Flamme.Features/Attributes/DependsOn.cs](../../src/common/Flamme.Features/Attributes/DependsOn.cs) | Declares feature dependencies. |
| `DependencyOfAttribute` | [src/common/Flamme.Features/Attributes/DependencyOf.cs](../../src/common/Flamme.Features/Attributes/DependencyOf.cs) | Declares optional dependency relationships. |

## Lifecycle

Feature classes usually use three lifecycle methods:

1. `Configure()`: declare additional feature configuration, scan activities, or add endpoint assemblies.
2. `ConfigureHostedServices()`: register hosted services with optional priority.
3. `Apply()`: add concrete services, options, stores, handlers, endpoints, and providers to DI.

`Module.Apply()` topologically sorts configured features and dependencies, configures them once, filters features with missing optional dependencies, registers hosted services, applies services, and finally registers installed-feature metadata.

A feature may introduce another feature from `Apply()`, for example by calling `Module.Configure<OtherFeature>()` directly or through a helper such as `AddActivity<T>()`. `Module.Apply()` keeps applying until no new features show up, so the introduced feature (and its dependencies) are configured and applied as well.

## Entry Points

The common public path is:

```csharp
services.AddFlamme(flamme =>
{
    flamme
        .UseWorkflowManagement()
        .UseWorkflowRuntime()
        .UseWorkflowsApi();
});
```

Implementation links:

- [AddFlamme and ConfigureFlamme](../../src/modules/Flamme/Extensions/DependencyInjectionExtensions.cs)
- [CreateModule and Use<T>](../../src/common/Flamme.Features/Extensions/DependencyInjectionExtensions.cs)
- [FlammeFeature](../../src/modules/Flamme/Features/FlammeFeature.cs)
- [AppFeature](../../src/modules/Flamme/Features/AppFeature.cs)

`AppFeature` is a small wrapper that lets application-specific configuration run after the default `FlammeFeature` dependencies.

## Feature Dependencies

Feature dependencies are explicit attributes. Examples:

- [WorkflowsFeature](../../src/modules/Flamme.Workflows.Core/Features/WorkflowsFeature.cs) depends on system clock, expressions, mediator, default formatters, multitenancy, and commit strategies.
- [WorkflowManagementFeature](../../src/modules/Flamme.Workflows.Management/Features/WorkflowManagementFeature.cs) depends on string compression, mediator, memory cache, system clock, workflows, workflow definitions, and workflow instances.
- [WorkflowsApiFeature](../../src/modules/Flamme.Workflows.Api/Features/WorkflowsApiFeature.cs) depends on workflow instances, management, runtime, and SAS tokens.

This is why feature classes are the best way to learn a module. They encode its runtime assumptions.

## Module Properties

`IModule.Properties` is used as a shared bag during feature configuration. A concrete example is FastEndpoints assembly collection in [Flamme.Api.Common/Extensions/ModuleExtensions.cs](../../src/common/Flamme.Api.Common/Extensions/ModuleExtensions.cs). Features call `AddFastEndpointsAssembly`, and later `AddFastEndpointsFromModule` registers all collected assemblies with FastEndpoints.

## Static Host Composition

Hosts select modules through code-first `AddFlamme` registration and deploy the resulting application as a versioned image. The former configuration-driven shell feature system, runtime package loading, and independent shell reload APIs have been removed. Register custom activities and modules at build time and roll out a new host version when composition changes.

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
