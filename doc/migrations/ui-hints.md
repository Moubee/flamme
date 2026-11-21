# Migrate from UI hints

Flamme removed editor hints from its .NET APIs and JSON contracts. Update hosts,
extension packages, and API clients together when upgrading.

## Custom activities and API clients

- Remove `InputAttribute.UIHint`, `Options`, `UIHandler`, and `UIHandlers` assignments
  from activity inputs. Remove registrations for hint handlers and their property
  options providers.
- Remove reads of `uiHint` and `uiSpecifications` from activity input descriptors.
  Clients can use the input type, display name, description, and other catalog
  metadata to build their own editor.
- Remove requests to
  `POST /descriptors/activities/{activityTypeName}/options/{propertyName}` and
  references to `IActivityDescriptorOptionsApi`.
- Remove references to `GetMonacoLanguage`, the API client's UI hint models, and
  `WithHttpEndpointBasePathProvider`. Set the HTTP activity base URL through
  `HttpActivityOptions.BaseUrl` where a base URL is needed for execution.
- Update External Authentication extensions for the `SettingFieldDescriptor`
  constructor without `UiHint`. Use `ValueType`, `AllowedValues`, validation, and
  sensitivity metadata to interpret the setting.
- Update consumers of AI grounding and Secrets descriptors for the removed hint
  fields. Secret picker discovery remains available through `PickerEndpoint`.

## Existing workflow definitions

Workflow imports accept legacy input definitions carrying `uiHint`. The field is
ignored on import and omitted on export.

The former `dictionary` hint stops enabling nested entry evaluation on upgrade.
Before upgrading, inventory or export the affected workflow definition versions.
Opt into nested evaluation explicitly for those inputs:

- For a wrapped input on a custom C# activity or host method, use
  `[Input(EvaluatorType = typeof(DictionaryValueEvaluator))]`. The evaluator is
  registered by the core workflow feature in the `Flamme.Workflows` namespace.
- For a published workflow used as an activity, set
  `"evaluateDictionaryEntries": true` in the corresponding entry of `inputs`.
  In C#, set `InputDefinition.EvaluateDictionaryEntries` to `true`.

Publish the updated child definitions and update and republish any parents pinned
to older child versions. Drain affected in-flight workflows before upgrading, or
migrate the exact definition versions they still reference before resuming them.
