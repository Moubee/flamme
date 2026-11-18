# NativeAOT diagnostic probes

The four console probes establish a reproducible baseline for the first NativeAOT
migration step. They call existing production code and keep unresolved failures
visible. Their `legacy-baseline` provider does not implement generated composition
or establish NativeAOT support for the libraries, server, persistence, or scripting.

| Probe | Production path |
| --- | --- |
| `features` | `Elsa.Features` default activation, declared dependency ordering, lifecycle, and installed-feature registry. No Workflow Core reference. |
| `execution` | Explicit factories for the stock core features, the real workflow runner, and a sequence containing an awaited activity. |
| `payloads` | Standalone `JsonPayloadSerializer`, registered DTO/list aliases, nested bodies, and scalar contracts. An authored System.Text.Json context supplies an independent positive control. |
| `integrated` | Default `WorkflowsFeature` activation, the real workflow runner, and production payload serialization. |

The Features probe also supports the `explicit-catalog` provider. It compiles
an independent catalog caller covering dependency graphs, policies, runtime
configuration, metadata isolation, typed hosted-service identities, and deterministic negative cases. The default
`legacy-baseline` keeps the original reflection-based observation. Provider
selection is a compiler input, so the explicit executable does not root the
legacy bootstrap. Both providers use the same module lifecycle and explicitly
select their admission policy; see [module composition](module-system.md#explicit-feature-composition).

Each executable also runs an independent asynchronous/generated-JSON control.
JSON reflection is disabled in managed and native publication. The default baseline
fixtures retain managed discovery; the explicit catalog is a runtime oracle. The packaged compiler consumer below exercises generated composition separately.

## Run the tests

Run from the repository root with the SDK selected by `global.json` and both supported runtimes installed. Native tests publish for the current host and execute that binary. The committed probe locks cover Linux x64 and macOS arm64. Each host needs its platform compiler and development libraries. [Native AOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)

```bash
project=test/integration/Elsa.NativeAot.IntegrationTests/Elsa.NativeAot.IntegrationTests.csproj
dotnet restore "$project" --locked-mode
dotnet test --project "$project" -c Release --no-restore
dotnet test --project "$project" -c Release --no-restore -- --treenode-filter '/*/*/*/*[Category=NativeAotAcceptance]'
```

The unfiltered run packs a feature producer and compiler assets. A fresh two-target consumer restores the staged packages in locked mode and exercises `services.CreateModule()` in Auto, Dynamic, and Generated builds. The fixture checks the caller-supplied constructor, the imported dependency, repeated Apply, installed metadata and hosted-service callbacks. It also checks the validation logic. Select `NativeAotAcceptance` for the remaining build matrix and published executables. This category covers the explicit Features catalog, [packed module consumers](nativeaot-module-exports.md#validate-producerconsumer-boundaries), and [compiler build selection](nativeaot-build-selection.md#run-the-package-validation). Add `-f net10.0` or `-f net11.0` before `--` to select a target. The .NET 11 tests include the runtime-async opt-out.

To run the compiled Features publish and execution cases separately, use the
following filters. Each process reports its observed provider, runtime and RID.
The optional dynamic-code producer test checks that importing its factory
reports IL3050 at publish time.

```bash
dotnet test --project "$project" -c Release -- --treenode-filter '/*/*/CodeGenerationTests/NativeConsumerRunsGeneratedProvider*'
dotnet test --project "$project" -c Release -f net11.0 -- --treenode-filter '/*/*/CodeGenerationTests/RuntimeAsyncOptOutRunsPackedFeatures'
dotnet test --project "$project" -c Release -- --treenode-filter '/*/*/CodeGenerationTests/UnselectedDynamicCodeProducerExposesItsNativeRequirement'
```

## Diagnose a legacy provider

The four legacy probes exercise capabilities at different stages of migration. Select their diagnostic tests when investigating a specific runtime or framework. An unresolved failure produces a failed test and preserves the child process output.

```bash
dotnet test --project "$project" -c Release -f net11.0 --no-restore -- --treenode-filter '/*/*/*/*[Category=NativeAotDiagnostic]'
```

To select one row, replace the category with its name, for example `legacy-baseline-payloads-native-default` or `explicit-catalog-features-native-disabled`. The runtime-async opt-out rows target .NET 11.

Set `ELSA_NATIVEAOT_STRICT=true` to treat IL diagnostics as failures when investigating a probe. The diagnostic publish records source and native compiler output even when the exercised runtime path succeeds. The explicit catalog can still report source warnings for unused legacy reflection paths.

Each test uses a fresh directory under `artifacts/nativeaot/<framework>/`. Inspect the restore and publish logs, evaluated MSBuild properties, publish binlog, IL diagnostics, and execution output for the failing case. A probe must report the expected framework, RID, dynamic-code and reflection-JSON settings, run the control and production scenario in order, and finish with the expected provider and scope. Evidence from a macOS publish describes macOS execution. Run on Linux for Linux evidence.

## Maintain probe locks

Solution restores use each executable's portable `packages.lock.json`. RID-specific restores use its `locks/<mode>/<rid>/packages.lock.json`. After a dependency change, regenerate and review each affected two-framework lock. For example, run this command for the Linux native Features lock. Lock generation works across operating systems and does not require a native compiler.

```bash
dotnet restore src/apps/Elsa.NativeAot.Features.Smoke/Elsa.NativeAot.Features.Smoke.csproj --force-evaluate \
  -p:NativeAotMode=native -p:RuntimeIdentifier=linux-x64 \
  -p:EnableTrimAnalyzer=true -p:EnableAotAnalyzer=true
```

Use the same analyzer settings for managed and native restores. Omit the target framework during restore to include both frameworks. The catalog providers share the same dependencies and lock. The test runner uses locked restore before publishing a single framework with the same graph settings.
