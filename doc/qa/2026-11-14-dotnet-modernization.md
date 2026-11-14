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
  with the legacy reader, including empty, Unicode, and large inputs.
- Build the complete solution in Release with locked dependencies and capture
  a binary log. Run affected Core, HTTP, and Common unit suites on both targets.
- Review after four commits, fix actionable findings, and keep the worktree
  clean with each completed step committed conventionally.

## Validation results

Implementation, test results, and the independent review outcome will be recorded
here as the steps complete. No performance improvement is claimed without
measurement.
