# BPMN integration removal

BPMN execution and XML interchange have been removed. The `Elsa.Bpmn` and
`Elsa.Bpmn.Interchange` packages, `UseBpmn`/`UseBpmnInterchange` registrations,
`BpmnProcess` activity, and all `bpmn/*` API routes are no longer available.
The reference server accepts native Elsa workflow definitions through the
[workflow management APIs](workflow-management.md).

## Upgrade existing workflows

Before deploying this version, inventory definitions and instances that contain
`BpmnProcess` or other removed BPMN types. Export source documents from the old
server while its BPMN routes are still available. A native JSON export of an old
BPMN-backed definition still contains removed activity types and does not convert
it into a supported workflow.

Reimplement required behavior with native Elsa activities, flowcharts, bookmarks,
and triggers, then validate it before publishing the replacement definition.
There is no automatic BPMN-to-native conversion or migration of suspended BPMN
interpreter state. Let existing BPMN instances finish on the old version, or plan
an explicit business-level transition before removing that runtime.

Clients using BPMN analysis, import, export, or document editing must stop calling
those endpoints. See the [integration removal guide](../integration-removal.md)
for the full package and hosting migration.
