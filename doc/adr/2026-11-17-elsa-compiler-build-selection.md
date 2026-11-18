# Compile time selection of Elsa composition

Status: Accepted, amended November 18, 2026 for feature package distribution.

## Context

Versioned typed exports provide a composition contract for independently
compiled modules. Hosts need to select managed discovery or catalog composition
before compilation so NativeAOT can remove the unused bootstrap. The selection
also needs to work when compiler assets and producers are installed via NuGet.

## Decision

Deliver the C# generator, diagnostics and MSBuild imports with `Elsa.Features`.
A library referencing Features can generate its own registrations. Applications
referencing that library receive the tooling through its NuGet dependency graph.
Runtime assemblies have no dependency on Roslyn. The separately published
compiler packages preserve the installation path used by earlier hosts.

The compiler projects target netstandard2.0 and use compiler-host Roslyn
dependencies. They omit application weavers. This is a compiler tooling
exception to the net10.0 and net11.0 runtime target policy. The imports-only
package also uses netstandard2.0 for its project and ships only its build assets.

Resolve execution mode from `ElsaExecutionMode` and `PublishAot` before writing
compiler-visible properties. Track the mode, metadata and bootstrap settings as
compile inputs. `Auto` resolves to Generated for a native publication and Dynamic
for other builds. `Generated` can also execute using JIT. `Dynamic` with
`PublishAot=true` is a build error. Metadata generation is a separate switch.

For ordinary feature composition, the producer generator discovers feature
declarations and the generated host intercepts its application-source module
creation calls. Hosts with an authored partial module entry point can continue
to use it.
The Dynamic body invokes the managed hook. The Generated body composes explicitly
selected versioned exports into an existing feature catalog. The mode does not
select the composition policy or alter feature lifecycle behavior. A manual
entry point can use the same compile time selection with explicit or prebuilt
exports. The analyzer validates authored entry points and selected hooks.

## Consequences

Hosts can use the same catalog composition under JIT and NativeAOT.
Application-local choices stay local to the app. Shared imports or global
properties can also configure source dependencies. Repository source builds
apply the same compiler settings to feature projects and hosts through shared
MSBuild imports.

The [build selection guide](../wiki/nativeaot-build-selection.md) documents the
package references, authoring shape and validation workflow.
