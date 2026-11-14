# Retired upstream automation

These helpers were inherited from Elsa and are retained for reference and local tests.
They are outside the agent skill directory and do not define Flamme release procedures.
The release, release-train, and Discord publisher command-line entry points exit with
an error before reading credentials, contacting services, or changing release state.

Flamme has no configured public package publisher, website refresh, social announcement,
or bounty program. A future release process must define Flamme-owned destinations and
package identities before enabling publication.

The old release profile is a test fixture only. Its upstream repositories, feeds, and
account identifiers are not runtime configuration. The test suites mock remote release
operations; Discord delivery tests use a temporary local HTTP server.

Run the retained suites locally:

```sh
python3 -m unittest discover -s scripts/archive/elsa-release/tests -v
python3 -m unittest discover -s scripts/archive/elsa-release-announcements/tests -v
```
