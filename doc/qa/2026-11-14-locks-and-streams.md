# Lock and .NET 11 stream migration

## Scope and research

Continue on `feat/dotnet-modernization` from `3d0bac007` in the existing separate
worktree. Audit all repository C# lock operands and in-memory stream construction,
migrate legacy synchronization gates to `System.Threading.Lock`, and adopt the
.NET 11 stream adapters where their capabilities fit their consumers.

The user explicitly authorized breaking API compatibility when needed. In
particular, `MemoryStore<TEntity>.Sync` will return `Lock` rather than `object`.
Keeping it typed as `object` while changing internal callers would create two
independent synchronization domains (Monitor and Lock), so all callers must use
the strongly typed gate together. Existing compiled consumers must rebuild.

Primary references:

- [C# lock semantics and recommendations](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock)
- [Warnings about converting Lock to object](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/lock-semantics)
- [.NET 11 in-memory stream adapters](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/libraries#in-memory-stream-adapters)

`Lock` is available on both supported frameworks. C# selects its specialized
locking implementation from the operand's static type; changing only the runtime
object is insufficient. No `Monitor` wait/pulse protocol was found in the initial
audit. Object identity sentinels used as signals or dictionary keys are not gates.

The initial production stream inventory contains 21 `MemoryStream` allocations
across 18 files: 17 expandable outputs, three existing input wrappers, and one
JSON conversion buffer. Expandable outputs still require expandable streams.
The .NET 11 read adapters do not replace them. HTTP string downloads require
seeking and length for range responses, which `StringStream` does not provide.

## Commit sequence

1. Commit the research, decomposition, user-authorized API change, and acceptance
   criteria in this document.
2. Migrate production gates, including the public shared memory-store gate and
   collection synchronization, preserving one gate for each synchronization domain.
3. Migrate fixture and test gates, leaving identity-only signal objects intact.
4. Adopt .NET 11 stream adapters for gzip, configuration loading, and applicable
   binary downloads, with .NET 10 implementations and meaningful behavioral tests.
5. Independently review after these four commits, fix actionable issues in separate
   conventional commits, and commit the completed validation report.

## Acceptance criteria

- Every actual C# `lock` operand in the repository is statically a `Lock`; no
  overlooked object aliases, collection locks, or mixed Monitor/Lock domains remain.
- Public `MemoryStore<TEntity>.Sync` and its consumers migrate together. The
  breaking change is documented; signal/key sentinels keep their identity role.
- Stream changes preserve valid text/binary payloads, gzip interoperability,
  configuration parsing, cancellation, stream ownership, and HTTP range behavior.
  Capability changes permitted by the user are documented.
- Both targets compile with SDK 11.0.100, the Release solution builds, and the
  relevant tests and broader CI test lanes pass on .NET 10 and .NET 11.
- Independent review addresses synchronization semantics, static operand types,
  resource lifetime, and stream capabilities. Every completed step is committed
  conventionally, and the worktree is clean at handoff.

## Results

### Lock inventory and migration

The completed source audit covers every actual C# `lock` statement in the
repository, including fixtures and tests, and checks CSX/Razor/F#/VB sources for
other synchronization protocols. It found no direct Monitor calls, synchronized
method attributes, or `SyncRoot` protocols requiring separate treatment.

| Lock domains | Count |
| --- | ---: |
| Existing object gates migrated (34 fields, one local, one public property) | 36 |
| Collection monitors replaced by dedicated shared gates | 6 |
| Migrated domains | 42 |
| Existing typed gates retained | 3 |
| Final typed gates | 45 |

All 171 final `lock` statements use `System.Threading.Lock`. This includes all 38
shared-memory-store callers. Production changes touch 26 files; fixture/test gate
changes touch seven. The queue, callback, and collection paths share one dedicated
gate per collection. The tenant-isolation test also takes that same gate for its
polling read and snapshot, adding two lock statements to the original 169.

An independent review confirmed these counts and checked for object aliases,
changed lock ordering, awaits inside critical sections, and split synchronization
domains. None were found. Reentrant callbacks retain reentrant locking semantics.

A compiled metadata audit inspected all 269 project-owned Release assemblies
(135 solution projects across both targets, with the .NET 10-only benchmark).
It found **zero Monitor member references** and specialized `Lock.EnterScope`
references in 38 assemblies. The temporary audit program uses
`System.Reflection.Metadata`; its source and output are at
`/tmp/flamme-lock-il-audit/` and `/tmp/flamme-lock-il-audit.log`.

Identity-only object keys/signals and framework-owned `TextWriter.Synchronized`
adapters remain unchanged. They are not repository-owned object lock gates.

### Stream inventory and migration

| Consumer | .NET 11 implementation | .NET 10 implementation |
| --- | --- | --- |
| Gzip text input | `StringStream`, UTF-8 | UTF-8 bytes |
| Gzip compressed input | `ReadOnlyMemoryStream` | Read-only `MemoryStream` |
| Configuration JSON input | `StringStream`, UTF-8 | JSON writer and seekable buffer |
| Binary downloadable content | `ReadOnlyMemoryStream` | Read-only `MemoryStream` |

The 17 true expandable output buffers remain expandable. HTTP string downloads
keep a seekable `MemoryStream` because range processing needs length and seeking.
No existing sequence-backed stream or fixed writable-buffer candidate was found,
so neither `ReadOnlySequenceStream` nor `WritableMemoryStream` was introduced.

Gzip compression now disposes the compressor before reading the output, ensuring
that the footer contains the final CRC and size. Tests verify external Python
gzip fixtures, complete footers, empty/Unicode/large text, and cancellation.
The .NET 10 implementation emits no valid frame for empty input even after empty
writes and disposal; flushing first produces an incomplete 15-byte result.
Its fallback returns a canonical valid empty gzip frame after checking cancellation.
.NET 11 uses the native streaming path. Configuration tests exercise non-seekable
loading, Unicode/nested values, reader-enabled comments/trailing commas, and
duplicate-key errors. Binary download tests verify bytes, metadata, seeking,
ranged reads, empty content, and disposal.

### Intentional compatibility changes

- `MemoryStore<TEntity>.Sync` now returns `System.Threading.Lock`. Binary
  consumers must rebuild. Keep the expression strongly typed in `lock (store.Sync)`;
  casting to `object` or using Monitor would create a separate lock domain.
- Binary downloadable streams now report `CanWrite == false` on both targets.
  They retain length, seeking, and range-read support. Consumers should treat
  downloads as read-only content.
- Newly compressed gzip payloads include a complete footer, including a valid
  empty-input frame on .NET 10. Text contents and the base64/gzip format remain
  unchanged; compressed bytes can differ from the previous incomplete output.

No synchronization or allocation performance improvement is claimed without
measurement.

### Build and targeted validation

The complete Release solution build passed on macOS arm64 with SDK 11.0.100:
zero errors and the same 191 warnings as the pre-migration build. Comparing warning
diagnostics against the earlier build found no new warnings. Dependencies,
framework targets, and lock files were unchanged; builds reused the locked restore.

```sh
dotnet build Elsa.slnx --configuration Release --no-restore --maxcpucount:4 --tl:off -bl:/tmp/flamme-lock-stream-release.binlog
```

The updated Common and HTTP suites passed on both targets:

| Suite | .NET 10 | .NET 11 |
| --- | ---: | ---: |
| Common | 114 | 119 |
| HTTP | 43 | 43 |
| Total | 157 | 162 |

All 319 tests passed, with zero failures or skips. The added coverage comprises
16 cases per framework. Logs are
`/tmp/flamme-lock-stream-{common,http}-{net10.0,net11.0}.log`; the full build log is
`/tmp/flamme-lock-stream-build.log`.

The broader CI test lanes also passed against that Release build with Docker-backed
fixtures and at most four concurrent MTP test modules:

| Lane | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| .NET 10 complete solution | 4,629 | 0 | 2 |
| .NET 11 unit/integration filter | 4,414 | 0 | 0 |
| .NET 11 component filter | 220 | 0 | 2 |
| Total | 9,263 | 0 | 4 |

Both targets retain the existing skips for `ImportWorkflowActivity_ShouldUpdateOtherPods`
(not implemented) and `DeleteWorkflow_Clustered` (interference with event-driven tests).

```sh
dotnet test --solution Elsa.slnx --framework net10.0 --configuration Release --no-build --no-restore --max-parallel-test-modules 4
dotnet test --solution Elsa.UnitIntegration.Tests.slnf --framework net11.0 --configuration Release --no-build --no-restore --max-parallel-test-modules 4
dotnet test --solution Elsa.Component.Tests.slnf --framework net11.0 --configuration Release --no-build --no-restore --max-parallel-test-modules 4
```

Logs are `/tmp/flamme-lock-stream-all-net10.0.log`,
`/tmp/flamme-lock-stream-unit-integration-net11.0.log`, and
`/tmp/flamme-lock-stream-component-net11.0.log`. The .NET 11 filters exclude the
.NET 10-only benchmark, matching CI. No benchmark or non-macOS application runtime
validation was performed.

### Independent review

A separate reviewer examined the lock changes after three commits, then confirmed
the final four-commit range `3d0bac007..420a0a773` matches the reviewed lock and
corrected stream implementations. The outcome was **no actionable findings**.
Review covered every lock operand, shared gates and callback closures, reentrancy,
lock ordering, public API changes, stream capabilities, gzip finalization and
empty-input fallback/cancellation, configuration parsing, and disposal.
The coordinator reviewed the diffs, reran the compiled audit, and verified tests
against the final source.
