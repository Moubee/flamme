# Activities And Authoring

Flamme supports C# workflow classes, JSON definitions, visual designer definitions, host method activities, and workflow-definition activities. See the [ElsaScript migration guide](../migrations/elsascript.md) when upgrading a host that used the retired DSL.

## C# Workflows

C# workflows usually derive from [WorkflowBase](../../src/modules/Flamme.Workflows.Core/Abstractions/WorkflowBase.cs) and implement `Build(IWorkflowBuilder builder)`.

The root activity can be a `Sequence`, `Flowchart`, or another composite activity. The README has a minimal example that starts with `HttpEndpoint` and then sends email.

Source landmarks:

- [WorkflowBase](../../src/modules/Flamme.Workflows.Core/Abstractions/WorkflowBase.cs)
- [WorkflowBuilder](../../src/modules/Flamme.Workflows.Core/Builders/WorkflowBuilder.cs)
- [IWorkflowBuilder](../../src/modules/Flamme.Workflows.Core/Contracts/IWorkflowBuilder.cs)
- [Workflow runtime feature AddWorkflow/AddWorkflowsFrom](../../src/modules/Flamme.Workflows.Runtime/Features/WorkflowRuntimeFeature.cs)

The reference server registers workflows from its assembly with `AddWorkflowsFrom<Program>()` in [Program.cs](../../src/apps/Flamme.Server.Web/Program.cs).

## JSON Workflows

JSON definitions are materialized by [JsonWorkflowMaterializer](../../src/modules/Flamme.Workflows.Management/Materializers/JsonWorkflowMaterializer.cs). Sample JSON workflows appear in:

- [src/apps/Flamme.Server.Web/Workflows](../../src/apps/Flamme.Server.Web/Workflows)
- [test/component/Flamme.Workflows.ComponentTests/Scenarios](../../test/component/Flamme.Workflows.ComponentTests/Scenarios)
- [test/integration/Flamme.Workflows.IntegrationTests/Scenarios](../../test/integration/Flamme.Workflows.IntegrationTests/Scenarios)

JSON workflows are important for designer compatibility and import/export tests. The [blob storage workflow provider](../../src/modules/Flamme.WorkflowProviders.BlobStorage) loads `.json` files through its built-in JSON format handler; register it with `UseFluentStorageProvider()`.

## Designer Authored Workflows

The designer consumes metadata from workflow API descriptor endpoints and persists definitions through workflow definition endpoints. The server-side responsibilities are:

- expose activity descriptors
- expose expression descriptors
- expose variable descriptors
- save drafts and publish versions
- return workflow graphs and reference data

Relevant code:

- [ActivityDescriptors endpoints](../../src/modules/Flamme.Workflows.Api/Endpoints/ActivityDescriptors)
- [WorkflowDefinitions endpoints](../../src/modules/Flamme.Workflows.Api/Endpoints/WorkflowDefinitions)
- [WorkflowDefinitionManager](../../src/modules/Flamme.Workflows.Management/Services/WorkflowDefinitionManager.cs)
- [ActivityRegistryPopulator](../../src/modules/Flamme.Workflows.Management/Services/ActivityRegistryPopulator.cs)

## Host Method Activities

Host method activities expose methods on registered host types as activities. Register host types with workflow management:

```csharp
services.AddFlamme(flamme =>
{
    flamme.AddActivityHost<MyHost>();
});
```

Key files:

- [HostMethodActivity](../../src/modules/Flamme.Workflows.Management/Activities/HostMethod/HostMethodActivity.cs)
- [HostMethodActivityProvider](../../src/modules/Flamme.Workflows.Management/Activities/HostMethod/HostMethodActivityProvider.cs)
- [HostMethodActivitiesOptions](../../src/modules/Flamme.Workflows.Management/Options/HostMethodActivitiesOptions.cs)
- sample host type [Penguin](../../src/apps/Flamme.Server.Web/ActivityHosts/Penguin.cs)

Use host method activities when host application methods need to appear as designer activities without creating a full activity package.

## Workflow Definition Activities

Workflow definition activities let a workflow call another workflow definition. This is useful for composition and reuse.

Key files:

- [WorkflowDefinitionActivity](../../src/modules/Flamme.Workflows.Management/Activities/WorkflowDefinitionActivity/WorkflowDefinitionActivity.cs)
- [WorkflowDefinitionActivityDescriptorFactory](../../src/modules/Flamme.Workflows.Management/Activities/WorkflowDefinitionActivity/WorkflowDefinitionActivityDescriptorFactory.cs)
- [WorkflowDefinitionActivityProvider](../../src/modules/Flamme.Workflows.Management/Activities/WorkflowDefinitionActivity/WorkflowDefinitionActivityProvider.cs)
- [WorkflowReferenceGraphBuilder](../../src/modules/Flamme.Workflows.Management/Services/WorkflowReferenceGraphBuilder.cs)

Tests:

- [CachingAndWorkflowDefinitionActivity](../../test/component/Flamme.Workflows.ComponentTests/Scenarios/CachingAndWorkflowDefinitionActivity)
- [WorkflowReferenceGraph](../../test/component/Flamme.Workflows.ComponentTests/Scenarios/WorkflowReferenceGraph)

## StateMachine Activity

The [StateMachine](../../src/modules/Flamme.Workflows.Core/Activities/StateMachine/Activities/StateMachine.cs) activity models named states with trigger-driven transitions. It lives in `Flamme.Workflows.Core` and is registered as a built-in activity.

Key concepts:

- **States** ([StateMachineState](../../src/modules/Flamme.Workflows.Core/Activities/StateMachine/Models/StateMachineState.cs)): Named states, each with an optional `Entry` and `Exit` activity.
- **Transitions** ([Transition](../../src/modules/Flamme.Workflows.Core/Activities/StateMachine/Models/Transition.cs)): Directed From→To pairs with an optional `Trigger` activity, optional `Condition` expression, and optional `Action` activity.
- **InitialState**: The first state to enter when execution starts.

Execution cycle:

1. On start, the machine enters `InitialState` and runs its `Entry` activity if present.
2. All outbound transition triggers for the current state are scheduled concurrently.
3. When a trigger completes and its `Condition` evaluates to true, competing triggers are canceled, the optional `Action` runs, then the current state's `Exit` activity runs.
4. The machine moves to the `To` state and repeats from step 1.
5. If the current state has no valid outbound transitions, the machine completes.

When a condition evaluates to false the failed trigger is re-armed and remains waiting alongside other triggers for the state.

## Adding A New Activity

Typical steps:

1. Add an activity class in the owning module's `Activities` folder.
2. Derive from the appropriate base class, usually `Activity` or `CodeActivity`.
3. Define inputs and outputs with Flamme input/output models.
4. Build the producer with the Flamme activity generator and select the activity in management. Use `Module.AddActivitiesFrom<TMarker>()` for the marker assembly's exported activities or `management.AddActivity<T>()` for an exact type.
5. Add a unit test with `ActivityTestFixture` for activity-only behavior.
6. Add integration or component tests if it creates bookmarks, uses persistence, or participates in runtime dispatch.

Good examples:

- [WriteLine](../../src/modules/Flamme.Workflows.Core/Activities/WriteLine.cs)
- [HttpEndpoint](../../src/modules/Flamme.Http/Activities/HttpEndpoint.cs)
- [SendHttpRequest](../../src/modules/Flamme.Http/Activities/SendHttpRequest.cs)
- [RunJavaScript](../../src/modules/Flamme.Expressions.JavaScript/Activities/RunJavaScript/RunJavaScript.cs)

### Producer contexts

In an external activity project, reference the generator as a private build dependency.
The project below uses central package management. Pin `Flamme.ActivityGenerator` to
the same release as `Flamme.Workflows.Core` in your package versions file.

```xml
<PackageReference Include="Flamme.ActivityGenerator" PrivateAssets="all" />
```

The consuming host can use the compiled bindings from that package. A host that
declares its own activities or closed generic contexts also needs the generator.

The producer assembly supplies bindings for its public concrete activities, including activities without an `Activity` attribute. Assembly and marker registration select its generated export inventory. A producer can also declare an internal activity or a closed generic activity in a context.

```csharp
using Flamme.Workflows;
using Flamme.Workflows.Activities;

[ActivityBinding(typeof(SetVariable<Receipt>))]
internal partial class ReceiptActivities : ActivityContext;
```

Use `management.AddActivities(ReceiptActivities.Default)` or `services.AddActivities(ReceiptActivities.Default)` to select an explicit context. Use `management.RemoveActivity<T>()` or update `ManagementOptions.ActivityTypes` to remove an activity from that host's catalog. A workflow can still use a bound activity in a code-first graph. When a host or graph selects an exact type without a binding, Flamme reports the module, activity and missing binding.

Two producers can explicitly bind the same closed activity type. The registry shares their binding when the activity identity and the bound input and output members agree.

Use named activity methods for bookmark and child-completion callbacks.
Keep those names when changing activities used by suspended workflows. The
generated bindings look up the stored callback name on the owning activity.

## Activity Metadata

Activity descriptors expose metadata such as input types, display names,
descriptions, and defaults. For instructions on upgrading custom activities
and API clients that used UI hints, see the [migration guide](../migrations/ui-hints.md).

The generator copies declared and inherited activity attributes into
`ActivityDescriptor.Attributes`. Consumers that inspect this raw attribute
list should obtain compiler-synthesized nullability annotations from the CLR
type when they need them. The generated list omits those annotations.

Implement `IActivityPropertyDefaultValueProvider.GetDefaultValue(ActivityPropertyDefaultValueContext)` for scoped defaults. The context includes the exact activity type, CLR member name, declaring type and CLR property type. Resolve dependency-based provider activities as host services or supply a direct provider construction binding. The host evaluates defaults and runs descriptor modifiers when it builds each catalog.

## Authoring Choice Guide

| Need | Use |
| --- | --- |
| Compile-time workflow with strong typing | C# workflow class |
| Designer-created or imported workflow | JSON workflow definition |
| Host app method as activity | Host method activity |
| Reusable workflow composition | Workflow definition activity |
| Module-specific trigger or IO | Custom activity in the owning module |
