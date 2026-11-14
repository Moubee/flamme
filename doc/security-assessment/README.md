# Historical security and quality assessments

This directory retains AI-assisted draft assessments dated 2026-05-20 against upstream `elsa-workflows/elsa-core`, `release/3.7.0`. They are historical research inputs, not approved vulnerability advisories, current Flamme test results, or a security certification.

- [Finding reconciliation](elsa-workflows-elsa-core-findings-2026-05-20.md) preserves the 25 historical finding titles and source locations, maps them to current implementation and existing tests, and records validation limits.
- [Technology profile](elsa-core-profile.md), [architecture patterns](elsa-core-architecture-patterns.md), [software quality](elsa-core-software-quality.md), and [ISO 25010 assessment](elsa-core-iso25010.md) describe the assessed revision. Their provider lists, commands, and counts may no longer apply.
- `software-quality-iso25010-assessment.skill` is assessment-method reference material, not evidence that its checks passed.

Use the [security policy](../../SECURITY.md) for current maintenance scope and private reporting. When updating an assessment status, record the reviewed commit, the actual checks run, and any remaining deployment-specific uncertainty. The presence of code or a test file alone does not establish that a vulnerability is fixed in every supported deployment.
