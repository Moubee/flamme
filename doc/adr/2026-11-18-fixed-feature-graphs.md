# Compile generated hosts with fixed feature graphs

Status: Accepted for Generated mode.

## Context

An earlier generator emitted one registration method per referenced assembly.
The host built a catalog at startup, discovered its active graph through
callbacks and traversed dependencies during `Apply`. An unselected constructor
could become reachable through that catalog during native publication.

## Decision

Resolve feature membership, required dependencies, presence conditions and
lifecycle order at host compilation. Read the selections from the host and
from the metadata that participating feature libraries publish for their
configuration methods. A producer owns the constructor bindings for its
internal types. Emit calls to the selected bindings and direct, ordered
configuration and application calls in the host.

Each module owns its instances. Its membership is sealed before user code runs.
Runtime options, custom factories, DI and hosted-service priority remain per
host. A configuration callback can customize a selected feature. An attempted
addition through opaque runtime code fails before invoking that feature's
factory. Diagnosable runtime-dependent feature selection is a build error.

## Consequences

Changing feature membership requires rebuilding the host. Conditional deployments
can use separate statically composed entry points. Feature packages must be
rebuilt to expose their selections across assembly boundaries. Activity
contributions to other features belong in the configuration phase so they are
available when those features register their services.

Generated C# in the host shows the activation order, and the linker sees the
individual constructor bindings selected by the host. Workflow execution,
serialization and dynamically discovered activities have their own AOT
requirements. See the [build and migration guide](../wiki/nativeaot-build-selection.md).
