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

Results and the final audit inventory will be recorded after implementation and
validation. No synchronization or allocation performance improvement is claimed
without measurement.
