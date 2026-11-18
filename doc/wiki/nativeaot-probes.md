# NativeAOT Features tests

A packed consumer exercises generated feature factories through `services.CreateModule()`.
The standalone `Elsa.NativeAot.Features.Smoke` executable uses a handwritten
catalog to test dependency ordering, admission policies, configuration, hosted
service lifetimes and failure cases under managed .NET and NativeAOT.
See [explicit feature composition](module-system.md#explicit-feature-composition).

## Run the tests

Run from the repository root with the SDK selected by `global.json` and both supported runtimes installed. Native tests publish for the current host and execute that binary. The committed probe locks cover Linux x64 and macOS arm64. Each host needs its platform compiler and development libraries. [Native AOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)

```bash
project=test/integration/Elsa.NativeAot.IntegrationTests/Elsa.NativeAot.IntegrationTests.csproj
dotnet restore "$project" --locked-mode
dotnet test --project "$project" -c Release --no-restore
dotnet test --project "$project" -c Release --no-restore -- --treenode-filter '/*/*/*/*[Category=NativeAotAcceptance]'
```

The unfiltered run builds a feature producer against the packed `Elsa.Features` package. A fresh two-target consumer references only the producer package, restores in locked mode, and exercises `services.CreateModule()` in Auto, Dynamic, and Generated builds. The fixture checks the caller-supplied constructor, the imported dependency, repeated Apply, installed metadata and hosted-service callbacks. It also checks the validation logic. Select `NativeAotAcceptance` for the remaining build matrix and published executables. This category covers the explicit Features catalog, [packed module consumers](nativeaot-module-exports.md#validate-producerconsumer-boundaries), and [compiler build selection](nativeaot-build-selection.md#run-the-package-validation). Add `-f net10.0` or `-f net11.0` before `--` to select a target. The .NET 11 tests include the runtime-async opt-out.

To run the compiled Features publish and execution cases separately, use the
following filters. Each process reports its observed provider, runtime and RID.
The optional dynamic-code producer test publishes and runs with the feature
excluded, then selects it and verifies that native publication reports IL3050.

```bash
dotnet test --project "$project" -c Release -- --treenode-filter '/*/*/CodeGenerationTests/NativeConsumerRunsGeneratedProvider*'
dotnet test --project "$project" -c Release -f net11.0 -- --treenode-filter '/*/*/CodeGenerationTests/RuntimeAsyncOptOutRunsPackedFeatures'
dotnet test --project "$project" -c Release -- --treenode-filter '/*/*/CodeGenerationTests/OnlySelectingTheOptionalProducerRootsItsDynamicConstructor'
```

The tests record build logs, evaluated MSBuild properties and process output in
`artifacts/nativeaot/<framework>/`. Set `ELSA_NATIVEAOT_STRICT=true` to make
IL diagnostics fail the standalone catalog publish.

## Maintain the Features probe locks

Solution restores use the portable `packages.lock.json`. When the probe's
dependencies change, regenerate its two-framework locks for each supported RID
(`linux-x64` and `osx-arm64`) and publication mode (`managed` and `native`).
For example, update the Linux native lock with the same analyzer settings used
by the publication test.

```bash
dotnet restore src/apps/Elsa.NativeAot.Features.Smoke/Elsa.NativeAot.Features.Smoke.csproj --force-evaluate \
  -p:NativeAotMode=native -p:RuntimeIdentifier=linux-x64 \
  -p:EnableTrimAnalyzer=true -p:EnableAotAnalyzer=true
```
