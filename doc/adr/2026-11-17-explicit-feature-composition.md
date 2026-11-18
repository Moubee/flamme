# Explicit feature composition with independent admission policies

Status: Accepted

## Context

The managed module discovers constructors, dependency attributes, and display
metadata at runtime. The NativeAOT baseline reproduces missing constructor
metadata during activation. Preserving whole assemblies would hide that problem
and would not define a contract usable by generated producers.

Composition also has observable legacy behavior: conditional presence is filtered
in one initial pass, features introduced during Apply bypass that initial filter,
and repeated Apply reapplies existing instances without rerunning configuration.
A provider switch must not silently change those semantics.

## Decision

Keep one module implementation with an internal metadata/construction provider.
The existing constructor uses the managed reflection provider. An additive
catalog constructor and CreateModule overload consume only explicit metadata and
factories; they never construct or fall back to the reflection provider.
Feature-specific runtime contracts remain in Elsa.Features, with no Roslyn or
Workflow Core dependency.

ExplicitFeatureCatalogBuilder snapshots factory declarations, closed feature
keys, dependency collections, and installed-feature metadata. Build rejects
ambiguous identities, invalid feature types, missing strong dependencies, and
strong dependency cycles. Conditional-presence keys can be absent. Catalogs are
immutable and reusable; installed descriptors and feature instances belong to
individual modules. Factory overrides require declared coverage and exact type
identity. Configuration values and callbacks still execute at runtime.

Admission policy is an explicit composition input:

- LegacyV1 remains the default for both providers and preserves characterized
  conditional, late-addition, and repeated-Apply behavior.
- ValidatedV1 uses fixed-point conditional admission and revalidates surviving
  strong dependencies before initial or late feature application. Managed
  discovery can select this policy too.

Explicit modules and managed ValidatedV1 modules become faulted after admission,
factory, or callback failures. They reject further composition, including when a
callback catches a nested failure. Arbitrary callback effects are not rolled
back; recovery requires a fresh module and service collection.

Hosted-service declarations create closed singleton implementation descriptors
while their generic service type is statically known. Public constructor
requirements flow through a narrow DynamicallyAccessedMembers annotation. Runtime
Type requests only look up catalog entries. Priority, stable ties, implementation
identity deduplication, and insertion relative to direct registrations retain the
existing module contract. DI scope validation can inspect constructor dependencies;
opaque hosted-service factory declarations are not added to this API.

## Consequences

Existing AddElsa/IModule callers and default managed behavior remain available.
Explicit catalogs remain available to handwritten hosts. Compiler-generated
hosts use the [fixed feature graph](2026-11-18-fixed-feature-graphs.md).
The native component probe exercises explicit catalog behavior.
Generators, versioned cross-module exports, source diagnostics, payload codecs,
workflow execution, and worker/server capability migration remain separate work.

The standalone Features probe uses a handwritten catalog. It tests
both policies, lifecycle, failures, hosted-service identities and owned scopes
under managed execution and NativeAOT. Broader source diagnostics in the legacy
reflection provider remain visible; no package-wide compatibility flag or blanket
metadata roots are introduced. Linux acceptance requires execution on Linux.
