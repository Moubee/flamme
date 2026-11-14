# Security policy

## Supported versions

Flamme is preparing its first independent release. Security fixes currently target the latest `main` branch. There is no supported Flamme release series or commitment to backport fixes to older commits yet; review the release notes and this policy when releases become available.

Flamme is an independent fork of Elsa Workflows. Upstream Elsa releases and packages have their own maintenance policies. Retained `Elsa.*` identifiers do not make a Flamme build an upstream Elsa release.

## Report a vulnerability privately

Email the repository maintainer at [contact@dendeline.com](mailto:contact@dendeline.com?subject=Flamme%20security%20report) with the subject **Flamme security report**. This is the current reporting route while the repository is private and remains the fallback after publication.

Include the affected commit or version, the relevant configuration and hosting topology, a description of the security boundary involved, and a minimal reproduction when available. Explain what access an attacker would need and the impact you observed. Remove credentials, personal data, and production workflow contents; coordinate sensitive attachments with the maintainer first.

Do not disclose vulnerability details in issues, pull requests, or public discussions. Private repository issues can become public when the repository visibility changes. If GitHub later offers a **Report a vulnerability** button on this repository's [security advisories page](https://github.com/DenDeline/flamme/security/advisories), that private form is also suitable. Its availability depends on the maintainer enabling private vulnerability reporting after publication; this policy does not imply that it is enabled now.

The maintainer will assess reports and coordinate investigation, fixes, and disclosure with the reporter. There is no guaranteed response time or paid bounty program. If an issue also affects upstream Elsa, coordinate disclosure with both projects rather than assuming that a Flamme report reaches upstream maintainers.

## Deployment and historical assessments

The reference server is a development host. Follow its [configuration notes](src/apps/Elsa.Server.Web/README.md), the [identity and tenancy guide](doc/wiki/identity-tenancy-security.md), and the [scripting trust boundaries](doc/wiki/expressions-and-scripting.md) before exposing a host to untrusted users.

The [security assessment directory](doc/security-assessment/README.md) contains historical, AI-assisted draft material. Its dated observations and severity labels are not a list of confirmed current vulnerabilities or a security certification. The [reconciliation record](doc/security-assessment/elsa-workflows-elsa-core-findings-2026-05-20.md) distinguishes current implementation evidence from validation still needed.
