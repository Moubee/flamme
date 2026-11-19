# Historical finding reconciliation: Elsa Core assessment of 2026-05-20

> Historical, AI-assisted draft material. The original entries described upstream `elsa-workflows/elsa-core`, `release/3.7.0`, and labeled every finding Open. Those labels and severity ratings are not current Flamme vulnerability status.

This reconciliation was prepared on 2026-11-14 by reviewing source and existing test files at Flamme commit `baeef43312121b90059a87f90e009a3480dfd00c`. It preserves all 25 original finding titles and source references, including paths that have since moved. The original exploit narratives and reproduction payloads were removed from this document; earlier revisions remain in Git history and are still part of any publication review.

## How to read the status

- **Mitigation present** means current source contains a control addressing the named historical pattern. It is not a verified end-to-end fix, a fresh test pass, or evidence that every deployment is safe.
- **Explicit host trust boundary** means the capability is privileged by design and now requires host opt-in. Enabling it for untrusted authors remains unsafe.
- **Deployment-path validation outstanding** means supporting controls or unit tests exist but the complete relevant request/storage path was not verified.

The checks for this reconciliation were static source and test-file inspection plus documentation link checks. **No application tests, exploit reproductions, or production-deployment checks were run for this record.** Linked tests identify existing coverage to inspect and run; their presence does not assert that they passed in this review. Historical remote-code-execution titles are retained as titles of unverified claims, not as reproduced impact.

Report new or suspected current vulnerabilities through the [private reporting route](../../SECURITY.md). For current deployment guidance, see [identity and tenancy](../wiki/identity-tenancy-security.md), [scripting](../wiki/expressions-and-scripting.md), and the [reference server](../../src/apps/Flamme.Server.Web/README.md).

## Finding inventory

### 1. Unsandboxed Python.NET execution exposes the host process

Original reference: [src/modules/Elsa.Python/Services/PythonEvaluator.cs:1](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Python/Services/PythonEvaluator.cs#L1). Historical severity: **HIGH**; not reassessed here.

**Review status: Explicit host trust boundary.** The evaluator rejects execution unless the host sets `PythonOptions.AllowHostCodeExecution`. Python.NET remains privileged host code when enabled. The reference server explicitly enables Python in its base configuration; the root README quickstart disables it. Review the host setting and author trust rather than treating the switch as a sandbox.

Current implementation: [PythonNetPythonEvaluator.cs](../../src/modules/Flamme.Expressions.Python/Services/PythonNetPythonEvaluator.cs). Existing supporting tests: [PythonHostCodeExecutionTests.cs](../../test/unit/Flamme.Expressions.UnitTests/Python/PythonHostCodeExecutionTests.cs).

### 2. Path traversal through x-download-id header in ZipManager

Original reference: [src/modules/Elsa.Workflows.Api/Files/ZipManager.cs:1](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Workflows.Api/Files/ZipManager.cs#L1). Historical severity: **HIGH**; not reassessed here.

**Review status: Mitigation present.** Cache identifiers are checked against a length and character allow-list; cache and blob paths are checked before reads. The historical source location has moved.

Current implementation: [ZipManager.cs](../../src/modules/Flamme.Http/Services/ZipManager.cs). Existing supporting tests: [ZipManagerTests.cs](../../test/unit/Flamme.Activities.UnitTests/Http/ZipManagerTests.cs).

### 3. Unauthenticated polymorphic JSON deserialization enables remote code execution

Original reference: [src/modules/Elsa.Workflows.Api/Endpoints/Bookmarks/Resume/Endpoint.cs:30](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Workflows.Api/Endpoints/Bookmarks/Resume/Endpoint.cs#L30). Historical severity: **HIGH**; not reassessed here.

**Review status: Mitigation present.** Missing or invalid tokens cause an error response and immediate return before input parsing. The shared serialization resolver also rejects unregistered type aliases; this review does not reproduce the historical remote-code-execution claim.

Current implementation: [Endpoint.cs](../../src/modules/Flamme.Workflows.Api/Endpoints/Bookmarks/Resume/Endpoint.cs). Existing supporting tests: [ResumeEndpointSecurityTests.cs](../../test/integration/Flamme.Workflows.IntegrationTests/Api/Bookmarks/ResumeEndpointSecurityTests.cs).

### 4. Authenticated polymorphic JSON deserialization in workflow instance import

Original reference: [src/modules/Elsa.Workflows.Api/Endpoints/WorkflowInstances/Import/Endpoint.cs:112](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Workflows.Api/Endpoints/WorkflowInstances/Import/Endpoint.cs#L112). Historical severity: **HIGH**; not reassessed here.

**Review status: Mitigation present.** Workflow-state and payload serialization use the current registered-type resolver rather than the historical unrestricted CLR-name fallback. This is implementation evidence for the named primitive, not an end-to-end security review of instance import.

Current implementation: [TypeJsonConverter.cs](../../src/modules/Flamme.Workflows.Core/Serialization/Converters/TypeJsonConverter.cs). Existing supporting tests: [SerializationTypeResolverTests.cs](../../test/unit/Flamme.Workflows.Core.UnitTests/Serialization/Converters/SerializationTypeResolverTests.cs).

### 5. AdminApiKeyProvider grants admin to all-zero GUID API key

Original reference: [src/modules/Elsa.Identity/Providers/AdminApiKeyProvider.cs:15](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Identity/Providers/AdminApiKeyProvider.cs#L15). Historical severity: **HIGH**; not reassessed here.

**Review status: Mitigation present.** The provider accepts no key by default and compares supplied keys with an explicitly configured key. A host can still deliberately configure development credentials and must not do so in production.

Current implementation: [AdminApiKeyProvider.cs](../../src/modules/Flamme.Identity/Providers/AdminApiKeyProvider.cs). Existing supporting tests: [AdminCredentialProviderTests.cs](../../test/unit/Flamme.Identity.UnitTests/Providers/AdminCredentialProviderTests.cs).

### 6. AdminUserProvider returns admin for any username when password is `password`

Original reference: [src/modules/Elsa.Identity/Providers/AdminUserProvider.cs:19](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Identity/Providers/AdminUserProvider.cs#L19). Historical severity: **HIGH**; not reassessed here.

**Review status: Mitigation present.** The provider is disabled without configured credentials and checks the requested username against the configured username.

Current implementation: [AdminUserProvider.cs](../../src/modules/Flamme.Identity/Providers/AdminUserProvider.cs). Existing supporting tests: [AdminCredentialProviderTests.cs](../../test/unit/Flamme.Identity.UnitTests/Providers/AdminCredentialProviderTests.cs).

### 7. LocalHost permission handler grants admin to any request marked local

Original reference: [src/modules/Elsa.Identity/AuthorizationHandlers/LocalHostPermissionRequirementHandler.cs:1](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Identity/AuthorizationHandlers/LocalHostPermissionRequirementHandler.cs#L1). Historical severity: **HIGH**; not reassessed here.

**Review status: Mitigation present.** The SecurityRoot policy, localhost permission handler, and related grant switches have been removed. The migration guide documents replacement bootstrap options. Existing endpoint permission coverage is supporting evidence, not a proxy-topology test.

Current implementation: [authorization-model.md](../../doc/migrations/authorization-model.md). Existing supporting tests: [EndpointCoverageTests.cs](../../test/unit/Flamme.Identity.UnitTests/Authorization/EndpointCoverageTests.cs).

### 8. Privilege escalation through client-supplied role assignment on Users/Create

Original reference: [src/modules/Elsa.Identity/Endpoints/Users/Create/Endpoint.cs:1](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Identity/Endpoints/Users/Create/Endpoint.cs#L1). Historical severity: **HIGH**; not reassessed here.

**Review status: Mitigation present.** The endpoint checks `CanAssignRolesAsync` before creating a user. Supporting tests exercise the role-grant boundary; deployment authorization still depends on the configured permission model.

Current implementation: [Endpoint.cs](../../src/modules/Flamme.Identity/Endpoints/Users/Create/Endpoint.cs). Existing supporting tests: [RoleAuthorizationServiceTests.cs](../../test/unit/Flamme.Identity.UnitTests/Services/RoleAuthorizationServiceTests.cs).

### 9. HTTP endpoint trusts ContentLength header for body-size check

Original reference: [src/modules/Elsa.Http/Activities/HttpEndpoint.cs:303](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Http/Activities/HttpEndpoint.cs#L303). Historical severity: **HIGH**; not reassessed here.

**Review status: Mitigation present.** In addition to checking declared length, the activity wraps the body in `RequestSizeLimitStream` when a request limit is configured, so the header is not the sole enforcement point.

Current implementation: [HttpEndpoint.cs](../../src/modules/Flamme.Http/Activities/HttpEndpoint.cs). Existing supporting tests: [HttpEndpointTests.cs](../../test/unit/Flamme.Activities.UnitTests/Http/HttpEndpointTests.cs).

### 10. Default admin credentials with reversible password baked into config

Original reference: [src/apps/Elsa.Server.Web/appsettings.json:1](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/apps/Elsa.Server.Web/appsettings.json#L1). Historical severity: **HIGH**; not reassessed here.

**Review status: Mitigation present.** The base reference-server configuration does not seed production admin users or API keys. Development configuration still contains sample identities, documented by the root README. The admin providers require explicit configuration; this is not a claim that arbitrary deployment configuration is safe.

Current configuration: [appsettings.json](../../src/apps/Flamme.Server.Web/appsettings.json). Existing supporting tests: [AdminCredentialProviderTests.cs](../../test/unit/Flamme.Identity.UnitTests/Providers/AdminCredentialProviderTests.cs).

### 11. Hardcoded JWT signing key shipped in default server configuration

Original reference: [src/apps/Elsa.Server.Web/appsettings.json:71](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/apps/Elsa.Server.Web/appsettings.json#L71). Historical severity: **HIGH**; not reassessed here.

**Review status: Mitigation present.** Startup options validation rejects missing, weak, and known public signing keys. Known defaults are tolerated only in explicitly Development or Demo environments. Hosts must supply their own production key.

Current implementation: [ValidateIdentityTokenOptions.cs](../../src/modules/Flamme.Identity/OptionConfigurators/ValidateIdentityTokenOptions.cs). Existing supporting tests: [IdentityTokenOptionsValidationTests.cs](../../test/unit/Flamme.Identity.UnitTests/Options/IdentityTokenOptionsValidationTests.cs).

### 12. Role overwrite via id collision on Roles/Create

Original reference: [src/modules/Elsa.Identity/Endpoints/Roles/Create/Endpoint.cs:1](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Identity/Endpoints/Roles/Create/Endpoint.cs#L1). Historical severity: **HIGH**; not reassessed here.

**Review status: Mitigation present.** Role creation checks for an existing stored or provided role ID and rejects collisions; the endpoint maps the rejection to a conflict response. This source review does not prove concurrency behavior for every backing store.

Current implementation: [RoleManager.cs](../../src/modules/Flamme.Identity/Services/RoleManager.cs). Existing supporting tests: [RoleManagerTests.cs](../../test/unit/Flamme.Identity.UnitTests/Services/RoleManagerTests.cs).

### 13. Unsandboxed Roslyn C# script execution exposes the host process

Original reference: [src/modules/Elsa.CSharp/Services/RoslynCSharpEvaluator.cs:1](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.CSharp/Services/RoslynCSharpEvaluator.cs#L1). Historical severity: **HIGH**; not reassessed here.

**Review status: Explicit host trust boundary.** The evaluator rejects execution unless the host sets `CSharpOptions.AllowHostCodeExecution`. When enabled, Roslyn scripts have host privileges and are intended only for trusted authors; there is no sandbox or per-caller script permission.

Current implementation: [CSharpEvaluator.cs](../../src/modules/Flamme.Expressions.CSharp/Services/CSharpEvaluator.cs). Existing supporting tests: [CSharpHostCodeExecutionTests.cs](../../test/unit/Flamme.Expressions.UnitTests/CSharp/CSharpHostCodeExecutionTests.cs).

### 14. Polymorphic JSON deserialization resolves attacker-supplied .NET type via _type discriminator

Original reference: [src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs:341](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs#L341). Historical severity: **MEDIUM**; not reassessed here.

**Review status: Mitigation present.** Polymorphic type resolution uses the registered serialization type registry and `SerializationTypeResolver`. The presence of this boundary does not validate every type a custom host chooses to register.

Current implementation: [PolymorphicObjectConverter.cs](../../src/modules/Flamme.Workflows.Core/Serialization/Converters/PolymorphicObjectConverter.cs). Existing supporting tests: [SerializationTypeResolverTests.cs](../../test/unit/Flamme.Workflows.Core.UnitTests/Serialization/Converters/SerializationTypeResolverTests.cs).

### 15. Non-cryptographic randomness used to generate secrets and passwords

Original reference: [src/modules/Elsa.Identity/Services/DefaultRandomStringGenerator.cs:29](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Identity/Services/DefaultRandomStringGenerator.cs#L29). Historical severity: **MEDIUM**; not reassessed here.

**Review status: Mitigation present.** Secret-string generation uses `RandomNumberGenerator.GetString`, replacing the historical non-cryptographic generator.

Current implementation: [DefaultRandomStringGenerator.cs](../../src/modules/Flamme.Identity/Services/DefaultRandomStringGenerator.cs). Existing supporting tests: [DefaultRandomStringGeneratorTests.cs](../../test/unit/Flamme.Identity.UnitTests/Services/DefaultRandomStringGeneratorTests.cs).

### 16. Polymorphic deserialization resolves attacker-controlled .NET types

Original reference: [src/modules/Elsa.Workflows.Core/Serialization/Converters/TypeJsonConverter.cs:50](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Workflows.Core/Serialization/Converters/TypeJsonConverter.cs#L50). Historical severity: **MEDIUM**; not reassessed here.

**Review status: Mitigation present.** Unknown aliases throw a JSON error. Supported compound aliases and legacy names resolve only through the registered types, rather than loading arbitrary CLR names.

Current implementation: [SerializationTypeResolver.cs](../../src/modules/Flamme.Common/Serialization/SerializationTypeResolver.cs). Existing supporting tests: [SerializationTypeResolverTests.cs](../../test/unit/Flamme.Workflows.Core.UnitTests/Serialization/Converters/SerializationTypeResolverTests.cs).

### 17. Password and secret hashing uses unsalted single-pass SHA-256

Original reference: [src/modules/Elsa.Identity/Services/DefaultSecretHasher.cs:1](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Identity/Services/DefaultSecretHasher.cs#L1). Historical severity: **MEDIUM**; not reassessed here.

**Review status: Mitigation present.** New hashes use PBKDF2-SHA256 with a random salt and 600,000 iterations. Verification retains legacy-hash migration support; this is not a claim that existing stored credentials have all been rehashed.

Current implementation: [DefaultSecretHasher.cs](../../src/modules/Flamme.Identity/Services/DefaultSecretHasher.cs). Existing supporting tests: [DefaultSecretHasherTests.cs](../../test/unit/Flamme.Identity.UnitTests/Services/DefaultSecretHasherTests.cs).

### 18. Refresh tokens are indistinguishable from access tokens

Original reference: [src/modules/Elsa.Identity/Services/DefaultRefreshTokenIssuer.cs:1](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Identity/Services/DefaultRefreshTokenIssuer.cs#L1). Historical severity: **MEDIUM**; not reassessed here.

**Review status: Mitigation present.** Bearer validation checks the `token_use` claim; normal API authentication expects access tokens and the refresh flow expects refresh tokens.

Current implementation: [IdentityTokenOptions.cs](../../src/modules/Flamme.Identity/Options/IdentityTokenOptions.cs). Existing supporting tests: [IdentityTokenOptionsTokenUseTests.cs](../../test/unit/Flamme.Identity.UnitTests/Options/IdentityTokenOptionsTokenUseTests.cs).

### 19. SignalR workflow instance hub allows cross-tenant observation by id

Original reference: [src/modules/Elsa.Workflows.Runtime.SignalR/Hubs/WorkflowInstanceHub.cs:1](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Workflows.Runtime.SignalR/Hubs/WorkflowInstanceHub.cs#L1). Historical severity: **MEDIUM**; not reassessed here.

**Review status: Mitigation present.** Observation checks authentication, view permission, instance existence, and current tenant before joining the group. Tenant comparison applies when a tenant accessor is registered; custom-host composition remains part of the deployment boundary.

Current implementation: [WorkflowInstanceHub.cs](../../src/modules/Flamme.Workflows.Api/RealTime/Hubs/WorkflowInstanceHub.cs). Existing supporting tests: [WorkflowInstanceHubTests.cs](../../test/unit/Flamme.Workflows.Api.UnitTests/RealTime/Hubs/WorkflowInstanceHubTests.cs).

### 20. Dynamic LINQ injection through TimestampFilter.Column

Original reference: [src/modules/Elsa.Workflows.Management/Filters/WorkflowInstanceFilter.cs:165](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Workflows.Management/Filters/WorkflowInstanceFilter.cs#L165). Historical severity: **MEDIUM**; not reassessed here.

**Review status: Mitigation present.** Timestamp column names are normalized against a fixed allowed list before constructing a dynamic query. Tests include rejected injected column values.

Current implementation: [WorkflowInstanceFilter.cs](../../src/modules/Flamme.Workflows.Management/Filters/WorkflowInstanceFilter.cs). Existing supporting tests: [AlterationsApiTimestampFilterTests.cs](../../test/integration/Flamme.Alterations.IntegrationTests/AlterationsApiTimestampFilterTests.cs).

### 21. SAS token expiration not enforced (broken-by-design issuance)

Original reference: [src/modules/Elsa.SasTokens/Contracts/DataProtectorTokenService.cs:25](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.SasTokens/Contracts/DataProtectorTokenService.cs#L25). Historical severity: **MEDIUM**; not reassessed here.

**Review status: Mitigation present.** Expiring tokens use time-limited data protection. Token formats distinguish time-limited and explicitly non-expiring tokens, and fallback parsing guards legacy tokens.

Current implementation: [DataProtectorTokenService.cs](../../src/modules/Flamme.SasTokens/Contracts/DataProtectorTokenService.cs). Existing supporting tests: [DataProtectorTokenServiceTests.cs](../../test/unit/Flamme.SasTokens.UnitTests/Contracts/DataProtectorTokenServiceTests.cs).

### 22. Unauthenticated Resilience SimulateResponse endpoint enables memory exhaustion and unsafe deserialization

Original reference: [src/modules/Elsa.Resilience/Endpoints/SimulateResponse/Endpoint.cs:15](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Resilience/Endpoints/SimulateResponse/Endpoint.cs#L15). Historical severity: **MEDIUM**; not reassessed here.

**Review status: Mitigation present.** The endpoint requires simulation execution permission, bounds query input, and rejects new sessions when capacity is reached. It uses bounded numeric JSON parsing rather than the historical deserialization pattern.

Current implementation: [Endpoint.cs](../../src/modules/Flamme.Resilience/Endpoints/SimulateResponse/Endpoint.cs). Existing supporting tests: [SimulateResponseEndpointTests.cs](../../test/integration/Flamme.Resilience.IntegrationTests/SimulateResponseEndpointTests.cs).

### 23. Workflow import persists before authorization check

Original reference: [src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/Import/Endpoint.cs:45](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/Import/Endpoint.cs#L45). Historical severity: **MEDIUM**; not reassessed here.

**Review status: Mitigation present.** Script-policy and workflow-import authorization checks complete before calling the importer. Component tests check forbidden imports leave storage unchanged.

Current implementation: [Endpoint.cs](../../src/modules/Flamme.Workflows.Api/Endpoints/WorkflowDefinitions/Import/Endpoint.cs). Existing supporting tests: [ImportAuthorizationTests.cs](../../test/component/Flamme.Workflows.ComponentTests/Scenarios/RestApis/Endpoints/WorkflowDefinitions/Import/ImportAuthorizationTests.cs).

### 24. ImportFiles endpoint omits per-definition read-only/system check

Original reference: [src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/ImportFiles/Endpoint.cs:52](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Workflows.Api/Endpoints/WorkflowDefinitions/ImportFiles/Endpoint.cs#L52). Historical severity: **MEDIUM**; not reassessed here.

**Review status: Mitigation present.** The endpoint authorizes the complete set of imported definitions before persisting any of them, including existing read-only targets. The component tests cover the read-only target case; this review does not independently validate every system-definition policy.

Current implementation: [Endpoint.cs](../../src/modules/Flamme.Workflows.Api/Endpoints/WorkflowDefinitions/ImportFiles/Endpoint.cs). Existing supporting tests: [ImportAuthorizationTests.cs](../../test/component/Flamme.Workflows.ComponentTests/Scenarios/RestApis/Endpoints/WorkflowDefinitions/Import/ImportAuthorizationTests.cs).

### 25. Cross-tenant bookmark resolution in HTTP workflow middleware

Original reference: [src/modules/Elsa.Http/Middleware/HttpWorkflowsMiddleware.cs:152](https://github.com/elsa-workflows/elsa-core/blob/release/3.7.0/src/modules/Elsa.Http/Middleware/HttpWorkflowsMiddleware.cs#L152). Historical severity: **MEDIUM**; not reassessed here.

**Review status: Deployment-path validation outstanding.** Bookmark lookup no longer requests tenant-agnostic reads. The existing middleware test asserts `TenantAgnostic` is false with a capturing store. EF Core also applies ambient tenant filters. An authenticated HTTP request through tenant resolution, PostgreSQL lookup, and actual bookmark resumption still needs an end-to-end isolation check; the historical allegation is not confirmed by this review.

Current implementation: [HttpWorkflowsMiddleware.cs](../../src/modules/Flamme.Http/Middleware/HttpWorkflowsMiddleware.cs). Existing supporting tests: [HttpWorkflowsMiddlewareTests.cs](../../test/unit/Flamme.Http.UnitTests/Middleware/HttpWorkflowsMiddlewareTests.cs).

## Remaining validation

Before changing any of these observations to a verified resolution, run the relevant existing tests on the reviewed revision and add missing coverage for the actual deployment path. In particular, exercise cross-tenant HTTP bookmark resumption against PostgreSQL with the configured tenant resolver, and separately check the in-memory path if it is exposed to multiple tenants. Record the commands, configuration, and results rather than inferring isolation from a store interface.

Host-specific extension points remain relevant: registered serialization types, tenant accessor registration, custom authorization, scripting opt-in, and explicitly supplied credentials can change the trust boundary. The historical report and this reconciliation do not certify a production configuration.
