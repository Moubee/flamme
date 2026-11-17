# Versioned module exports before feature activation

Status: Accepted

## Context

Explicit feature catalogs provide typed factories, metadata and hosted-service
identities without reflective discovery. Independently compiled producers need a
stable export contract, module dependency checks, deterministic composition and
explicit replacement. Requiring every producer to build a complete local catalog
would duplicate cross-module dependencies and conceal ownership conflicts.

## Decision

Place immutable BCL-only schema, module identity, positive numeric contract
revision, requirements, and declared capabilities in Elsa.CodeGeneration.Abstractions.
Keep Features-specific typed fragments and composition in Elsa.Features. Runtime
libraries retain the repository's application frameworks and Fody policy; no
Roslyn/runtime dependency or generator-target exception is introduced.

Schema compatibility is exact for the initial schema1. Module requirements match
positive contract revisions exactly; they are independent of schema and NuGet
versions. Requirements can name declared capabilities. Future compatibility rules
must be explicit rather than inferred from package versions.

Producers freeze typed fragments without requiring external strong dependencies
locally. Hosts explicitly select exports, validate module/revision/capability and
cycle constraints, then merge declarations and validate the final feature graph.
Cross-export strong feature dependencies require the owning module in the
requester's declared requirement closure. Conditional presence remains optional.
No factory, lifecycle callback, or hosted-service constructor runs during import.

Use dependency-first ordering with original first-add priority among ready
modules. Re-adding the currently selected identical export object is idempotent;
different objects claiming an ID conflict. Explicit replacement requires the
currently selected expected object and the same logical module ID, retains its
slot, and revalidates the final selection. This avoids guessing delegate
semantics from identical metadata. Cross-owner feature, installed-identity and
hosted-service declarations never silently overwrite one another.

Immutable export singletons may be shared across hosts as compiled declarations.
Module instances, mutable configuration and service/scoped state remain host-local.
Existing catalog construction and per-host factory overrides remain supported.

## Consequences

The new public surface is additive. A producer can move from local Build to
CreateExport and declare external contracts; a host composes those exports before
creating its existing module. Existing callers and stored workflow/payload formats
are unchanged. The [API inventory](../wiki/nativeaot-module-exports.md#public-api-and-migration-inventory)
records the ownership and opt-in path.

A compiled producer and a fresh package-only consumer test the real boundary,
including scoped/hosted behavior and isolated hosts. Package provenance and locked
restore must be verified; source references cannot establish package consumption.
Generators, authored generation attributes, build-mode selection, packed generated
parity and broader execution/payload/worker capabilities remain later milestones.
