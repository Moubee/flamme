# ElsaScript removal

Flamme no longer includes the ElsaScript workflow-authoring DSL. This is a
breaking change for hosts and stored workflow definitions that use it. Author
workflows in [C# or JSON](../wiki/activities-and-authoring.md) instead.

## Removed surface

- `Elsa.Dsl.ElsaScript`, including its AST, parser, compiler, feature, and
  `ElsaScriptWorkflowMaterializer`.
- `Elsa.WorkflowProviders.BlobStorage.ElsaScript`, including its blob format
  handler and feature.
- `UseElsaScript()` and `UseElsaScriptBlobStorage()` registrations.
- The `ElsaScript` materializer and built-in `.elsa` file loading.
- The reference server's `.elsa` samples and the DSL integration test project.

Remove the retired package/project references, namespace imports, and feature
registrations from custom hosts. The remaining
`Elsa.WorkflowProviders.BlobStorage` module still loads `.json` definitions with
`UseFluentStorageProvider()` and supports custom format handlers. Its built-in
JSON handler does not load `.elsa` files.

C# workflow classes, JSON import/export, designer-authored definitions, and
C#, JavaScript, Python, and Liquid expression providers remain available. Generic
workflow providers, custom materializers, and `OriginalSource` preservation also
remain available.

## Migrate before upgrading

There is no automatic DSL-to-JSON conversion or database migration. Stored
definitions with `MaterializerName = "ElsaScript"` cannot be materialized by the
upgraded host, even when their `OriginalSource` is preserved.

1. Inventory `.elsa` workflow files and stored definitions using the `ElsaScript`
   materializer while the previous host is still available. Back up the source
   and persisted workflow data.
2. Rewrite each workflow as a C# workflow class or a JSON definition. Preserve
   the intended variables, expressions, activity connections, triggers, and
   workflow references. Renaming a file to `.json` or changing only
   `MaterializerName` does not convert its source.
3. Register C# workflows or publish/import the replacement JSON definitions.
   For blob loading, deploy valid `.json` files and remove the old `.elsa` files.
   Choose definition identities and versions deliberately, then update consuming
   workflow references as needed.
4. Test execution and persistence of the replacements before deploying the
   upgraded host. Complete old running or suspended instances on the previous
   deployment, or establish and verify an explicit migration plan for their
   saved state and referenced definition versions.

The removal does not delete existing workflow records or rewrite their source.
