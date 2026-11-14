# .NET 11 runtime async enablement

Research date: 2026-11-14. Base: `origin/main` at `9f596030e707b5b4a73ecf534bad2a8675eec339`.
Implementation branch: `feat/dotnet11-runtime-async`, in a separate worktree.

## Scope and acceptance criteria

Enable runtime-managed async compilation for supported .NET 11 builds while
retaining the .NET 10 assets and existing public APIs. The repository already
targets both frameworks and pins stable SDK 11.0.100, so another framework or
dependency migration is unnecessary.

- Supported .NET 11 source projects and correctness tests opt in centrally.
- .NET 10 builds continue using compiler-generated async state machines.
- A documented build property permits rebuilding with runtime async disabled.
- Existing `ConfigureAwait(false)` policy, ambient execution context,
  cancellation, exception delivery, and workflow execution remain correct.
- Verification inspects emitted metadata and executes real suspension paths;
  an evaluated MSBuild property alone does not prove runtime async was emitted.
- Shared configuration changes receive solution-level build/test coverage on
  both frameworks and an independent review after the first three commits.

## High-level delivery steps

1. **Research and plan** (`docs`): record current platform guidance, repository
   scope, compatibility risks, and acceptance checks before implementation.
2. **Compilation policy** (`feat`): centralize framework/platform gating and
   explicit opt-out, preserving other compiler features and ConfigureAwait
   behavior. Update developer instructions for the new build configuration.
3. **Regression coverage** (`test`): add focused checks near existing tests for
   emitted method metadata and async behavior, including the opt-out control.
4. **Review and verification**: independently review the first three commits,
   correct any findings in conventional commits, and record actual validation
   results and limitations below.

The coordinator owns sequencing, commits, and final review. Parallel research,
the Fody compatibility experiment, and implementation can proceed independently;
shared build edits and commits remain serialized.

## Current platform findings

The [.NET 11 runtime documentation](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-11/runtime)
specifies the compiler opt-in below for `net11.0`. Existing feature settings must
be retained when appending it:

```xml
<Features>$(Features);runtime-async=on</Features>
```

No `EnablePreviewFeatures` opt-in is needed for this target. The previous
`DOTNET_RuntimeAsync` and `UNSUPPORTED_RuntimeAsync` environment switches have
been removed; disabling compilation requires rebuilding the relevant projects.
The [.NET 11 announcement](https://devblogs.microsoft.com/dotnet/announcing-dotnet-11/)
still describes Runtime Async as a preview feature within the stable SDK release.

The [official performance article](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-11/)
also confirms that `LangVersion=preview` is unnecessary. This remains the same
C# async/await programming model: changing code generation should preserve
observable behavior, including ConfigureAwait, exceptions, and cancellation.
Performance gains for this workload require separate measurements; this task
does not claim a throughput or allocation improvement.

### Project opt-out and platform boundaries

The installed SDK 11.0.100's `.props` and `.targets` contain no mapping from
`UseRuntimeAsync` to compiler features. The
[ASP.NET Core build](https://github.com/dotnet/aspnetcore/blob/main/Directory.Build.targets)
implements its own `UseRuntimeAsync != false` condition before appending the
feature. Flamme likewise needs an explicit shared mapping so
`-p:UseRuntimeAsync=false` is effective and testable rather than merely accepted
as an unused MSBuild property. In the
[Roslyn implementation](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Compilation/CSharpCompilation.cs),
the normal compilation path enables runtime async only when the feature value
is exactly `on`; `runtime-async=off` disables that path.

[Runtime library build conditions](https://github.com/dotnet/runtime/blob/main/src/libraries/Directory.Build.targets)
exclude browser, WASI, Android, Apple mobile targets, and Mono. Accordingly,
central policy must avoid applying the switch indiscriminately to those
consumers. The repository currently builds server libraries and hosts for
`net10.0` and `net11.0`; consumers on unsupported runtimes can use the .NET 10
asset or rebuild with the opt-out. Runtime async is not a general browser/Mono
compatibility promise for the .NET 11 NuGet asset.

[.NET 11 Preview 3 release notes](https://github.com/dotnet/core/blob/main/release-notes/11.0/preview/preview3/runtime.md)
record NativeAOT and ReadyToRun support. These deployment modes must not be
disabled solely because runtime async is enabled. This task does not convert
the application to NativeAOT or change its publishing model.

### Implemented compilation policy

`Directory.Build.targets` applies the opt-in after each project's framework is
known, covering both production props roots and the correctness tests. It
defaults `UseRuntimeAsync` to `true` for exactly `net11.0` when `UseMonoRuntime`
is not `true` and the runtime identifier does not start with `browser-` or
`wasi-`; other builds default to `false`. This also leaves platform-specific
TFMs outside the default opt-in. An explicit property value is respected, and
the compiler feature is appended only for `net11.0` with `UseRuntimeAsync=true`.
The existing Fody package references and configuration guards remain in place.

The mapping preserves project-defined compiler feature entries. Standard
MSBuild global-property precedence still applies: supplying `Features` on the
command line overrides ordinary project assignments, including this appended
feature. A caller that overrides `Features` therefore controls the complete
compiler feature list; `UseRuntimeAsync` is the supported repository switch
when changing just the async compilation model.

SDK 11.0.100's `_GenerateCompileDependencyCache` hashes inputs such as
`DefineConstants` and `LangVersion`, but omits `Features`. Without an additional
cache input, changing the opt-in could reuse production assemblies compiled
with the previous model. `IncludeCompilerFeaturesInCompileCache` adds the
effective feature list to `CoreCompileCache` immediately before the SDK target
generates its hash. Ordinary incremental builds can then switch models without
requiring a clean build; `--no-build` still reuses existing assemblies.

### Metadata and compiler fallbacks

The [Roslyn design](https://github.com/dotnet/roslyn/blob/main/docs/compilers/CSharp/Runtime%20Async%20Design.md)
describes the emitted `MethodImplOptions.Async` bit (`0x2000`) and
`AsyncHelpers` calls. Metadata checks should inspect representative real
production `Task`/`ValueTask` methods for that bit, with the .NET 10 and opt-out
builds as negative controls. It is incorrect to require every async-shaped
method to carry the bit: custom task-like types and explicit custom builders
can retain compiler state machines. Source-generated code belongs to the same
compilation, so generator compatibility must be established by the real build.

## ConfigureAwait compatibility checkpoint

Both production props roots import `src/Fody.props`, which configures
`ContinueOnCapturedContext="false"`. Existing central package pins are
ConfigureAwait.Fody 3.4.1 and Fody 6.9.3. Merely enabling runtime async must not
silently remove this library behavior.

The [current Fody implementation](https://github.com/Fody/ConfigureAwait/blob/master/ConfigureAwait.Fody/ModuleWeaver.cs)
has a separate compiler-service path in addition to its state-machine rewrite.
A local probe with the released 3.4.1/6.9.3 packages, SDK 11.0.100, `net11.0`,
and `runtime-async=on` built and ran successfully. Its forced incomplete `Task`
await retained `MethodImplFlags=Async`, produced no nested state-machine type,
and resumed without the tracking synchronization context (`Captured=False`,
`Posts=0`). Its IL contains `ConfigureAwait(Boolean)` followed by
`AsyncHelpers.Await(ConfiguredTaskAwaitable)`, showing that the weaver changed
the runtime-async await while retaining the new compilation model.

The same forced-suspension probe produced these controls:

| Compilation and weaving | Emitted metadata | Captured context | Context posts |
| --- | --- | --- | --- |
| Runtime async, Fody enabled | `Async`, no nested state machine | `false` | 0 |
| Runtime async, Fody disabled | `Async`, no nested state machine | `true` | 1 |
| Compiler state machine, Fody enabled | `IL`, `AsyncStateMachineAttribute` | `false` | 0 |

The negative weaving control establishes that the context probe detects a
missing rewrite. The existing Fody configuration can therefore remain in place
without changing the default context behavior.

With the compile-cache hook applied, three ordinary incremental `dotnet build`
invocations of the scratch project toggled `UseRuntimeAsync` from `true` to
`false` and back to `true`. They emitted `Async`, then
`IL` with `AsyncStateMachineAttribute`, then `Async` again. All three runs
retained zero context posts. No clean or `Rebuild` target was used for this
toggle sequence, demonstrating that the effective compiler option invalidates
the cached compilation.

## Planned validation

1. Evaluate `TargetFramework`, `Features`, and opt-out properties for a
   production library, app host, correctness test, and benchmark. Check .NET 10,
   .NET 11, and `UseRuntimeAsync=false`, preserving unrelated feature entries.
2. Build and run a minimal Fody probe with forced asynchronous completion.
   Inspect `0x2000` and generated ConfigureAwait calls and verify that the
   continuation does not post to a captured synchronization context.
3. Add focused regression coverage for actual production metadata, `Task` and
   `ValueTask` suspension, execution-context flow, cancellation, exceptions, and
   the existing context policy. Use the existing TUnit/MTP infrastructure.
4. Restore all frameworks before selecting one for test execution. Use locked
   restore for unchanged dependencies; if a dependency change is necessary,
   reevaluate complete lock files as required by `AGENTS.md`.
5. Build `Elsa.slnx` in Release, run the focused tests and existing solution
   tests on both frameworks, then repeat the focused .NET 11 checks with
   `-p:UseRuntimeAsync=false`. Verify standalone projects affected by shared
   build configuration explicitly.
6. Review after the first three commits, run whitespace checks, and record
   results with any environment-dependent limitations. Container-dependent
   suites require the existing PostgreSQL/container prerequisites.

## Validation results

Verified on macOS Arm64 with SDK 11.0.100, MSBuild 18.12.2, .NET 11.0.0,
.NET 10.0.11, and Docker 29.9.3.

| Check | Result |
| --- | --- |
| Full solution locked restore | Passed; no dependency or lock-file changes |
| Release solution build, both frameworks | Passed; zero errors, 191 existing warnings |
| Complete solution suite, both frameworks | 9,198 passed, zero failures, four existing skips across 118 modules |
| Focused mediator suite, Release and Debug | 28 passed per configuration, 14 on each runtime |
| .NET 11 opt-out, ordinary incremental build | 14 passed without cleaning; production methods used state machines |
| Return to the default compilation model | Full solution build and metadata tests passed after the opt-out run |
| Standalone testing extension and TLS smoke builds | Passed on both frameworks, zero warnings or errors |
| NuGet packaging | 71 packages and 71 symbol packages; every package retains .NET 10/11 assemblies |
| Configuration evaluation | Library, host, and tests opt in on .NET 11; .NET 10/benchmarks, browser RID, and Mono default out; existing Razor features retained |
| Whitespace | `git diff --check` passed |

The complete suite includes the PostgreSQL component and persistence tests.
The existing skips are `ImportWorkflowActivity_ShouldUpdateOtherPods` and
`DeleteWorkflow_Clustered`, each skipped on both runtimes. No test failures
required retries or production-code changes.

Independent review followed commits `7196e6598`, `30b66d730`, and `7b7e45040`
and found no actionable defects. It checked framework/platform defaults,
explicit MSBuild overrides, compile-cache invalidation, production metadata,
and forced-suspension context behavior. The subsequent test-only correction
uses TUnit's boolean assertion helpers to remove newly introduced analyzer
warnings; the full build and suite include that correction.

Key reproduction commands, from the repository root:

```sh
dotnet restore Elsa.slnx --locked-mode
dotnet build Elsa.slnx --configuration Release --no-restore
dotnet test --solution Elsa.slnx --configuration Release --no-build --max-parallel-test-modules 4 --timeout 10m -- --report-trx
dotnet test --project test/unit/Elsa.Mediator.UnitTests/Elsa.Mediator.UnitTests.csproj --configuration Release --framework net11.0 --no-restore -p:UseRuntimeAsync=false
dotnet test --project test/unit/Elsa.Mediator.UnitTests/Elsa.Mediator.UnitTests.csproj --configuration Release --framework net11.0 --no-restore
dotnet test --project test/unit/Elsa.Mediator.UnitTests/Elsa.Mediator.UnitTests.csproj --configuration Debug --no-restore
dotnet pack Elsa.slnx --configuration Release --no-build --no-restore --output packages
```

Restore and build `src/extensions/Elsa.Testing.Extensions/Elsa.Testing.Extensions.csproj`
and `test/TlsSmoke/TlsSmoke.csproj` separately; they are outside the solution.
Keep the full-target locked restore before selecting a framework for testing.
The local sandbox required normal build-process access; disabling the MSBuild
server avoided a sandbox connection timeout. `--disable-build-servers` is a
build/restore option and must not be passed to the MTP test application.

No benchmark, NativeAOT/ReadyToRun publish, container image rebuild, or remote
CI run was performed. Performance and deployment-mode claims remain limited
to the primary platform guidance above.
