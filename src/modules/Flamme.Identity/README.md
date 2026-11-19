# Elsa.Identity

## JWT Signing Key Configuration

Identity token signing requires a secure random key. Configure it through environment variables or a secrets manager and keep it out of committed appsettings files.

- Code-first hosts using `Identity:Tokens` should set `Identity__Tokens__SigningKey`.
- Production startup rejects missing keys, keys shorter than 32 ASCII characters, and known public defaults. Known public defaults are tolerated only in the explicit `Development` or `Demo` environments.

## Default Admin User Bootstrap

Elsa supports bootstrapping an initial admin role and user through the `DefaultAdminUser` feature.

This is the recommended way to initialize identity access now that user-management endpoints are permission-based and no longer rely on the `SecurityRoot` policy.

See `doc/adr/0010-default-admin-user-bootstrap-for-initial-identity-access.md` for the architectural decision.

### Code-first registration

Call `UseDefaultAdmin` while configuring Identity in the host. Shell features and
shell configuration paths have been removed.

```csharp
services.AddElsa(elsa =>
{
    elsa
        .UseIdentity(identity =>
        {
            identity.TokenOptions += options =>
            {
                options.SigningKey = builder.Configuration.GetRequiredSection("Identity:Tokens")["SigningKey"]!;
            };

            identity.UseDefaultAdmin(admin => admin
                .WithAdminUserName("admin")
                .WithAdminPassword(builder.Configuration.GetRequiredSection("Identity:Bootstrap")["Password"]!)
                .WithAdminRoleName("admin")
                .WithAdminRolePermissions(new List<string> { "*" }));
        })
        .UseDefaultAuthentication();
});
```

Supply the bootstrap password through deployment-owned configuration, for example
`Identity__Bootstrap__Password`, or a secrets manager. You can also use the shorthand
overload with an already resolved password:

```csharp
identity.UseDefaultAdmin("admin", bootstrapPassword, "admin", new List<string> { "*" });
```

### Operational notes

- The initializer is idempotent: existing admin role/user are not recreated.
- Do not keep development defaults in production.
- Prefer environment variables or a secret manager for admin credentials.
- After first bootstrap, rotate credentials according to your security policy.
- The `SecurityRoot` policy and localhost permission grant have been removed. Use the configured bootstrap administrator or admin API key for initial access.

## Secret Hashing

New identity passwords, client secrets, and API keys are hashed with PBKDF2-SHA256 using 600,000 iterations, a per-record salt, and version metadata. Existing legacy SHA-256 hashes remain valid and are upgraded opportunistically after a successful user login or API-key validation.

## External Authentication Compatibility

External Authentication is additive to Elsa Identity:

- Existing `/identity/login`, `/identity/refresh-token` and `/identity/logout` contracts remain the direct local-credential flow. `POST /identity/logout` revokes the sign-in session of the refresh token in its body; see [Signing Out](../../../doc/wiki/identity-tenancy-security.md#signing-out).
- The optional broker exposes separate local and external completion endpoints that return a short-lived, PKCE-bound authorization code before issuing Elsa credentials.
- Externally provisioned users may have no local password hash or salt. Such users fail direct local login with the same public result as any other invalid credential.
- Elsa remains the issuer of access tokens and the authority for their `permissions` claim, regardless of how the user authenticated.

See [the External Authentication migration guide](../../../doc/migrations/external-authentication.md) before changing a Studio host from direct OpenID Connect to brokered mode.
