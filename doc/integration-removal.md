# Static hosting and inherited integration removal

## Scope and migration

This breaking change retires Nuplane runtime package loading, CShells modular
hosting and reload APIs, Loom Platform recipe deployment, package catalog manifest
generation, BPMN execution/import/export, and WebhooksCore integration. Raw console
diagnostics were already retired by the baseline's OpenTelemetry migration.
Unused central dependency declarations are removed after checking retained uses.

Deploy the statically composed `Elsa.Server.Web` host or an application configured
with `AddElsa`. Register modules and activities at build time; roll out a new
process/container image to change them. Existing CShells feature configuration,
package feeds/catalogs, shell reload clients, and Platform recipe artifacts are no
longer supported. Move recipe workflow imports to the workflow definition API.
Migrate BPMN definitions and webhook activities before upgrading persisted
workflows. Generic HTTP endpoints/requests and RunTask remain available.

FastEndpoints, ConfigureAwait.Fody/Fody, Newtonsoft.Json, distributed locks,
resilience and PostgreSQL/in-memory persistence remain supported. All other
scripting, AI, and blob-storage integrations remain available. Classic tenant
activation, host drain, persisted administrative pause, authorization, and
workflow execution remain required behavior.

The old modular sample also opted into AI, UserTasks, Secrets, external
authentication and resilience modules. Enable each required retained capability
explicitly through its classic `Use...` registration in a custom `AddElsa` host;
the reference server does not enable every module by default. Move tenant
configuration to the classic `Multitenancy` section and each provider's classic
registration; shell-specific connection settings are no longer read.

Before starting a migrated deployment, preserve administrative pause state. Shell
pause keys use the exact shell ID (often `Default`), while the classic runtime
reads `elsa.quiescence.host-pause.default` and its legacy
`elsa.quiescence.pause.default` fallback in the agnostic tenant. With every old
and new runtime stopped, migrate the intended shell's existing pause value to
the classic host-pause key using the configured key-value store tooling. Preserve
the stored value and tenant context. Resolve multiple shell states explicitly;
they cannot be merged automatically. Verify pause recovery before enabling
ingress or background dispatch. Re-applying pause after an unrestricted startup
does not close the interval in which workflows could already execute.
