# Retired Elsa release helpers

This archive preserves release version parsing, notes, artifact verification, and
checkpoint tests inherited from Elsa. It is not an agent skill or an operational
release runbook. The `release.py` and `release_train.py` CLI entry points are disabled.
Imported legacy functions exist only to support the retained regression tests.

`release_notes.py` can still draft notes from local Git history, and
`verify_packages.py` can verify explicitly supplied manifests and artifacts. Neither
publishes packages. No default Flamme release profile is provided; the legacy profile
under `tests/fixtures` exists solely for tests.

See [the archive policy](../README.md) before reusing this code.
