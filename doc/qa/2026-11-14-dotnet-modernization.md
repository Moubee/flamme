# .NET 10/11 source modernization

## Baseline and research

Work starts from `origin/main` at `baeef4331`, in the separate
`dotnet-modernization` worktree on `feat/dotnet-modernization`.
The baseline already targets .NET 10 and 11, selects SDK 11.0.100 with patch
roll-forward, uses Microsoft Testing Platform and locked restores, and enables
.NET 11 runtime async. Repeating those upgrades would add no value.

Research was split between language/accessor modernization and framework API
replacement, with implementation and independent review coordinated separately.
The installed SDK is 11.0.100 (MSBuild 18.12.2); local runtimes include .NET
10.0.11 and 11.0.0. The full solution restores successfully with `--locked-mode`.

Primary references:

- [C# 14 field-backed properties](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14#the-field-keyword)
- [.NET 10 libraries](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries)
- [.NET 11 libraries](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/libraries)
- [.NET 11 SDK capabilities](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/sdk)

## Decomposition and commit sequence

1. `docs: plan .NET 10 and 11 source modernization`: record the baseline,
   research, scope, compatibility constraints, and verification criteria.
2. `refactor(core): adopt C# 14 field-backed properties`: remove private
   backing fields used only by accessors in activity execution and HTTP bookmark
   normalization. Preserve taint tracking, hierarchy registration, record
   equality, normalization, and JSON behavior.
3. `refactor(common): use generic nameof for collection aliases`: replace
   duplicated CLR collection-name strings with C# 14 unbound generic `nameof`.
   Keep literal wire-alias assertions to verify persisted names remain unchanged.
4. `refactor(common): use native Zstandard compression on .NET 11`: keep the
   .NET 10 implementation and existing codec contract, while using the built-in
   .NET 11 implementation. Verify legacy/new frame interoperability before
   accepting the change. Compressed bytes need not be identical between
   implementations; stored base64 Zstandard data must remain readable.
5. Review the preceding four steps independently, address findings in separate
   conventional commits if necessary, and record final validation and limits.

## Selection decisions

- C# 14 syntax works on both supported targets; no preview language feature is
  required. Preserve public property signatures and serialized contracts.
- Avoid changing EF entity backing fields because EF can discover them by
  convention. Avoid changing nullable sentinel fields to non-nullable property
  storage.
- Async enumerable consumers already use native `ToListAsync` in the relevant
  providers. Legacy `Open.Linq.AsyncExtensions` primarily supplies different
  task-of-enumerable helpers; a broad rewrite is outside this focused change.
- .NET 11 `StringStream` is non-seekable. Replacing public downloadable
  `MemoryStream` instances would change stream capabilities, so defer it.
- C# 15 unions, closed hierarchies, and collection arguments have no compelling
  compatible replacement here. Avoid adding conditional syntax just to use them.
- Keep existing central package versions and runtime-async/Fody configuration.
  Native Zstandard use does not require a dependency upgrade.

## Success criteria and verification

- No public API, supported target framework, bookmark JSON, or compression wire
  format changes.
- New behavior-focused regression tests pass on both .NET 10 and .NET 11 using
  the SDK's MTP `dotnet test --project` interface.
- Legacy Zstandard frames decode on both targets; native .NET 11 frames decode
  with the legacy reader, including empty text, Unicode, and large inputs.
- Build the complete solution in Release with locked dependencies and capture
  a binary log. Run affected Core, HTTP, and Common unit suites on both targets.
- Review after four commits, fix actionable findings, and keep the worktree
  clean with each completed step committed conventionally.

## Validation results

All four planned commits are implemented. No dependency versions, lock files,
framework targets, or runtime-async settings changed.

Locked full-solution restore passed. The full Release solution build passed with
zero errors and 191 warnings, all originating in untouched files. The build used
SDK 11.0.100, four MSBuild workers, `--no-restore`, and a binary log at
`/tmp/flamme-modernization-release.binlog`.

Affected unit suites passed using MTP, Release configuration, and `--no-restore`:

| Suite | .NET 10 | .NET 11 |
| --- | ---: | ---: |
| Workflows Core | 312 | 312 |
| HTTP | 40 | 40 |
| Common | 101 | 106 |
| Total | 453 | 458 |

All 911 affected-suite tests passed, with no failures or skips. The Common suites
emitted no warnings. Core retained an existing fixture-cleanup warning in an
untouched test. Logs are `/tmp/flamme-modernization-{core,http,common}-{net10.0,net11.0}.log`.

The broader CI test lanes also passed locally on macOS arm64 with Docker-backed
fixtures. Tests reused the Release solution build and limited MTP to four
concurrent test modules:

| Lane | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| .NET 10 complete solution | 4,613 | 0 | 2 |
| .NET 11 unit/integration filter | 4,398 | 0 | 0 |
| .NET 11 component filter | 220 | 0 | 2 |
| Total | 9,231 | 0 | 4 |

Both targets retain the existing skips for `ImportWorkflowActivity_ShouldUpdateOtherPods`
(not implemented) and `DeleteWorkflow_Clustered` (interferes with event-driven tests).
The commands, after the full-target locked restore and Release build, were:

```sh
dotnet test --solution Elsa.slnx --framework net10.0 --configuration Release --no-build --no-restore --max-parallel-test-modules 4
dotnet test --solution Elsa.UnitIntegration.Tests.slnf --framework net11.0 --configuration Release --no-build --no-restore --max-parallel-test-modules 4
dotnet test --solution Elsa.Component.Tests.slnf --framework net11.0 --configuration Release --no-build --no-restore --max-parallel-test-modules 4
```

Logs are `/tmp/flamme-modernization-all-net10.0.log`,
`/tmp/flamme-modernization-unit-integration-net11.0.log`, and
`/tmp/flamme-modernization-component-net11.0.log`.
The .NET 11 filters exclude the .NET 10-only benchmark project, matching CI.

### Zstandard compatibility findings

- Valid base64 Zstandard payloads retain the UTF-8 text contract. Existing frames
  decode on both targets; .NET 11 frames decode through the actual NuGet
  IronCompress package, including empty text and roughly 610 KB of Unicode.
- Native compression pledges the source byte length before writing, so its
  frame header supports older readers that allocate from that size.
- The final .NET 11 decoder validates the whole input with the one-shot decoder,
  bounds the allocation, and decodes only `bytesWritten`. It rejects empty
  compressed buffers, truncated frames, and garbage suffixes. Empty text remains
  represented by a valid nonempty frame.
- Invalid-input exception types can differ by target. The unchanged .NET 10
  native IronCompress reader tolerates some malformed frames that .NET 11 now
  rejects. IronCompress also has a managed fallback with different malformed-input
  behavior. Early isolated probes omitted native package assets; repository
  tests exposed that distinction, and the implementation/tests were corrected
  before the codec commit. Compatibility claims concern valid persisted data.
- No performance improvement is claimed without measurement. The third-party
  package remains available for .NET 10 and compatibility tests.

### Independent review

A separate reviewer inspected the first three commits, then the complete four-step
range `baeef4331..7a57a1010`. Both passes reported no actionable correctness
findings. Review covered public property signatures/defaults, taint tracking,
hierarchy registration, record equality/copy semantics, actual bookmark hash/JSON
serialization, collection wire aliases, decoder bounds, output lengths,
disposal, and cross-reader compatibility. The coordinator also reviewed each
diff before committing.

Optional future coverage identified in review is a fixed historical frame with
compressed blocks or unknown content size. Existing large-input and cross-reader
tests exercise compressed blocks dynamically; this is not a release blocker.
