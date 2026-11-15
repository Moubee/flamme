# Compose hosts statically and retire inherited runtime integrations

## Status

Accepted for the breaking integration-removal release.

## Context

The repository maintains classic `AddElsa` registration and a separate CShells
feature/service-provider lifecycle. The modular sample additionally loads packages
with Nuplane and applies Loom Platform recipes. These integrations add parallel
hosting, API and packaging contracts to a fork focused on distributed process
orchestration and versioned Kubernetes deployments.

## Decision

Use build-time module/activity references and classic `AddElsa` composition.
Retire CShells adapters, modular hosting and reload APIs, Nuplane package loading,
Loom Platform deployment, generated package catalog manifests, BPMN modules and
WebhooksCore integration. Change enabled modules by deploying a new application
or container image. Do not introduce another dynamic hosting system.

Retain FastEndpoints, Fody, Newtonsoft.Json, locks, resilience, PostgreSQL and
in-memory persistence, and the other scripting, AI and blob integrations. Classic
host drain, tenant lifecycle, authorization and persisted pause recovery remain
required. Runtime pause keys from shell deployments need explicit migration;
multiple independent shell states cannot be combined automatically.

## Consequences

Public shell feature types and reload clients are removed. Downstream hosts must
use classic registrations and explicitly opt into required retained modules.
Existing recipes, dynamic packages, BPMN workflows, and webhook activities need
application-specific migration before upgrading; suspended BPMN state has no
automatic conversion to native activities. Generic HTTP and RunTask remain.

Package restore no longer needs Feedz. Dependency graph and lock inspection must
verify the retired package families have disappeared, including transitive paths.
This decision makes no unmeasured throughput, startup or memory claims.

See the [migration guide](../integration-removal.md).
