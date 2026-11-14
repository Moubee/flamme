# Workflow run audit

Research date: 2026-11-14. Base: `origin/main` at `8c50b8669`, after
[Dependabot configuration PR #16](https://github.com/DenDeline/flamme/pull/16).

Follow-up on `main`: the root NuGet Dependabot version-update entry was removed
to stop Feedz scans. The shared restore graph requires Feedz-only package
versions, so this also stops routine nuget.org update PRs. Builds retain
`NuGet.Config`, package mappings, and locks; npm and GitHub Actions scans remain.
The findings and decisions below describe the original workflow audit.

## Findings

- Eight Dependabot PRs were open: Actions majors #17–19; production ClientLib
  webpack updates #20 and #24; video-project Remotion, React, and TypeScript
  updates #21–23. Provider groups are working; major upgrades remain individual.
- Each PR started the `pr` workflow with two .NET test lanes and a final
  `ubuntu-latest` check. The checkout update also exercised Docker and ADR
  workflows because it changed those workflow files.
- Packages already excludes `dependabot/*` pushes. Removing its branch-prefix
  triggers saves ordinary development-branch runs, not these eight bot runs.
  The previous configuration branch demonstrates the duplication:
  [Packages run 43626275319](https://github.com/DenDeline/flamme/actions/runs/43626275319)
  and [PR run 43626315879](https://github.com/DenDeline/flamme/actions/runs/43626315879)
  validated the same commit. Packages started five expensive jobs before being
  canceled; the PR run succeeded.
- Packages has four coverage test lanes, a coverage-report job, and a separate
  full-solution build/pack job. PR validation already builds the full solution
  and tests both supported frameworks.
- `design/video/elsa-readme-video` is an isolated Remotion asset project with no
  .NET build dependency. Its three current dependency PRs each start two .NET
  lanes; [the React run](https://github.com/DenDeline/flamme/actions/runs/43627055382)
  confirmed both were restoring/building/testing .NET projects.
- Production `src/modules/Elsa.Expressions.JavaScript.Libraries/ClientLib` is
  different: its checked-in `dist/*.js` files become embedded resources. Keep
  .NET checks for that directory. Existing workflows do not invoke npm/webpack,
  so they do not establish that a webpack major upgrade can rebuild the bundles.
- The NuGet Dependabot scan
  [43626825502](https://github.com/DenDeline/flamme/actions/runs/43626825502)
  failed, while the npm and Actions scans succeeded. Job logs show the proxy
  rejecting `f.feedz.io` with `egress not allowlisted`, HTTP 403, then
  `private_source_authentication_failure`. Review Dependabot access to the
  configured Feedz sources separately; do not remove those sources or infer that
  NuGet needs no CI. This branch does not change feed credentials or access.
- The branch-protection API returned HTTP 403 because this private repository's
  plan does not expose that feature. Preserve the existing `ubuntu-latest` check
  name and its failure behavior regardless of settings visibility.

## Success criteria and decomposition

1. Record the hosted baseline and current Dependabot capabilities in this document.
2. Remove automatic Packages runs on development branches, retain manual branch
   validation, make package artifact creation optional, and cancel superseded
   automatic Packages/Docker runs.
3. Skip the .NET matrix only for PR changes wholly within the isolated video
   directory. Preserve the PR workflow and `ubuntu-latest` result check. Manual
   runs, unknown paths, production npm changes, and mixed changes run all tests;
   classification errors fail the gate.
4. Lint workflows, exercise trigger/skip/failure scenarios, and independently
   review the combined change after the first three conventional commits.

Keep all existing test commands, frameworks, coverage checks, package commands,
and artifact failure handling. Measure hosted elapsed time and runner minutes
after rollout; structural job savings are not a measured percentage speedup.

## Dependabot decisions

The current official options support cooldown, provider groups, multiple
directories, `group-by: dependency-name`, and multi-ecosystem groups. Keep the
existing weekly Monday schedule, seven-day cooldown, minor/patch provider
groups, and individual major updates:

- Cooldown delays routine version updates; security updates bypass it. A weekly
  schedule limits scans, not the number of CI executions on rebased PRs.
- `rebase-strategy: disabled` would reduce refresh runs but also removes automatic
  maintenance of security PRs. Existing PRs can continue rebasing for 30 days
  after that setting changes. Retain automatic rebasing.
- `open-pull-requests-limit` defaults to five for each update entry. Lowering it
  smooths bursts but does not remove the eventual validation work; it also delays
  routine updates. Security updates have a separate fixed limit of ten. Retain
  the default while fixing redundant validation directly.
- `group-by: dependency-name` across directories adds no useful consolidation:
  the two current npm projects share no dependency names. Multi-ecosystem groups
  would mix independently reviewed runtime, video, and workflow changes.

Do not skip checks based on the Dependabot author, suppress security updates,
merge unrelated majors, or change repository security settings.

## Validation and review

The first three signed conventional commits were independently reviewed. No
actionable correctness or security findings remained. A documentation sentence
was clarified to say the package build is required only when enabled.

- actionlint 1.7.12 and `git diff --check` passed for the complete change.
- Executed the exact classifier Bash against nine real two-parent merge commits
  cloned at depth two: video-only, source-only, mixed, production ClientLib,
  moving a production file into video, filenames containing newlines inside and
  outside video, deleting a video file, and an empty diff.
- Confirmed depth-one history fails classification without emitting a decision;
  manual dispatch selects full .NET validation without a checkout.
- Exercised all 64 result-gate combinations. Only successful classification
  with `true` + successful tests or `false` + skipped tests succeeds.
- Compared parsed workflows with the base: Packages retains every original job,
  step, and coverage check apart from the optional build condition; PR retains
  all test steps and both framework lanes; Docker changes only concurrency.
  Existing event filters, permissions, and the `ubuntu-latest` name remain.
- Dependabot configuration, package versions, and lockfiles are unchanged.

This is a structural reduction: matching development-branch pushes lose the
six-job Packages graph; each video-only PR avoids both expensive .NET lanes and
uses path classification plus the existing result gate. Other PRs gain one short
classification job. No elapsed-time or billing percentage is claimed.

Hosted rollout checks remain: main Packages including coverage and artifacts,
manual branch Packages with `build_packages` both true and false, a video-only
PR with a successful result gate, a mixed PR with both frameworks, and
superseding automatic runs without canceling manual requests. Local simulations
do not establish GitHub scheduler or runner performance. The original NuGet
updater feed-access failure led to the follow-up scan removal noted above.

## Sources

- [Dependabot options](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference)
- [Multi-ecosystem updates](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/configuring-multi-ecosystem-updates)
- [Workflow syntax: inputs and path-filter limitations](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)
- [Workflow concurrency](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency)
- [Status checks and skipped jobs](https://docs.github.com/en/pull-requests/reference/status-checks)
- [Checkout: PR merge checkout and history depth](https://github.com/actions/checkout)
