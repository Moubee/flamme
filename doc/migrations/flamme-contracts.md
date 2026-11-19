# Moving definitions and hosts to Flamme

Use a fresh Flamme environment for new runs. This release changes source, binary and workflow-file contracts. An instance that suspended under Elsa needs its original engine, extensions, stored definition revision and delivery infrastructure to finish.

## .NET applications and packages

Replace fork-owned package and project references with their Flamme counterparts, including transitive modules that the host references directly. For example, reference `Flamme` and `Flamme.Persistence.EFCore.PostgreSql`, import `Flamme.Extensions`, and call `services.AddFlamme(...)`. Recompile the host, native extensions and clients against the same release. Update assembly-qualified type references in application configuration and regenerate executable NuGet lock files. Use `Flamme.slnx` and the renamed solution filters for builds and CI.

Third-party NuGet dependencies retain their own names. The reference server still defaults to the `/elsa/api` route and the EF provider still defaults to the PostgreSQL schema `Elsa`. Hosts that subscribe to the `Elsa.Workflows` activity source or meter can continue using that name. These values have separate consumers and lifetimes from the .NET assemblies.

## Workflow files

An activity occurrence keeps its own ID. Its `type` selects a capability such as `ControlFlow.Flowchart`, `ControlFlow.Join`, `Console.WriteLine` or `Signals.Event`. Its integer `version` selects the contract major. A flowchart connection references an occurrence ID. Changing a type preserves that ID, its connection endpoints, the event name chosen by the author and user strings such as the literal text `Elsa.WriteLine`.

1. Export definitions on the original host. Keep the originals and their referenced workflow versions together.
2. Convert a single portable definition with `python3 scripts/migrations/convert-activity-contracts.py original.json converted.json`. Choose a new output path. The converter assigns major 1 to an omitted version on a known v1 contract. Inspect the typed changes against the [built-in inventory](activity-contracts.csv). Closed generics, extension activities, delegates and dependent workflows require a host-specific conversion. Review typed variables, input/output types, custom storage drivers and scripts that reference CLR types with the extension owner.
3. Load the converted dependencies and definitions into a fresh store. Exercise the production import and export paths, run from a new instance, and verify branch results and wake-up stimuli. A successful import alone does not verify execution or binding.
4. Route new starts to Flamme. Keep the old deployment and its store available to drain existing instances, bookmarks, timers and pending messages. Separate the two deployments' incoming event, outbox, lock and queue traffic during cutover.

A definition-file conversion does not migrate database rows. Existing state may contain serialized CLR payloads, callbacks, protected values, scheduled messages and bookmark hashes computed using earlier route names. Pointing a renamed host at the same PostgreSQL schema cannot establish compatibility for those records. Recovery of existing Elsa instances, an in-place upgrade and a mixed-version cluster each need a separate deployment-specific migration and rehearsal.

For native workflow-as-activity imports, export the child and its caller together. The child's exact revision ID takes precedence over its cached display name and numeric version. Its logical definition ID must agree when present. A stale revision ID can bind only when the effective descriptor independently matches the logical owner and requested numeric version. If only a logical ID survives a display-name change, imports require one matching revision in the effective tenant scope. Supply the exact version to disambiguate an older export. Verify all references against the target tenant.

Native drafts marked usable as activities also reserve a descriptor when the registry refreshes. Saving or publishing checks the proposed name and version against stored workflow owners in its tenant scope. Concurrent authors creating different native definitions under the same activity name can pass this check before either writes. Serialize those native edits in the authoring host. The registry rejects a conflicting binding if it encounters one during registration.

## Producer-authored workflows

A trusted authoring host assigns each publisher a namespace. For example, `north.mail` and `south.mail` can each offer `Send`. A workflow imports those scopes under local aliases and requests an exact contract major for each occurrence. The catalog checks the authenticated principal's issuer and subject against the administrator's grant. The host owns authentication, tenant isolation and permission to publish a given workflow definition.

The curated translator emits a sequence of literal console writes and event waits. At publication it saves the executable graph and a manifest with the selected recipe releases, content digests, primitive versions and source mapping. The runtime starts from the published definition and resumes from the instance's exact definition version ID. Retain that revision and its registered primitive contracts for the lifetime of its runs. In a distributed authoring deployment, configure a shared distributed lock provider for publishers.

Each author occurrence and each recipe step has a stable ID supplied by its author. Their combination identifies a generated activity, including its wait, when a release or a local alias changes. Publishing another recipe revision creates a new workflow revision. Operators can inspect the published JSON and use its source map to locate the producer occurrence behind a running activity.

The [package consumer](../../samples/PublishedActivities/README.md) publishes both producers in one workflow, changes a release while the first instance is waiting, and resumes from PostgreSQL in another process.
