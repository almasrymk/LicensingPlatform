# 07 — Authentication and authorization

## Schemes

One JWT bearer scheme (`Program.cs`). `MapInboundClaims = false`. Issuer and audience from `Jwt` options. Algorithm pinned to HS256. Clock skew 30 seconds. Role claim is `ClaimTypes.Role`. Name claim is `name`.

Token kind is claim `typ_lic`: `user` or `client` (`ClaimNames.TokenType`). Policies refuse the wrong kind, so a client token cannot call portal routes and a user token cannot call device routes.

## User access token

`JwtTokenService.CreateUserToken`. Lifetime `Jwt:AccessTokenMinutes` (default 15).

Claims: `sub` (user id), `email`, `name`, role, `typ_lic=user`, `lang`, `jti`, optional `tid`, optional `cid`, one `perm` claim per permission from `Permissions.ForRole` at login time.

Changing a user's role does not rewrite existing access tokens. They keep old `perm` claims until expiry (15 minutes). **Confirmed** by issue-time claim copy. Refresh builds a new access token from the current role (`AuthService.IssueAsync` calls `Permissions.ForRole` again).

## Refresh tokens

`AuthService.RefreshTokenLifetime` is 7 days. Plaintext is random (`ISecretHasher.GenerateToken`). Database stores HMAC (`HmacSecretHasher`) with `Security:SecretPepper`.

Rotation: presenting a live token revokes it and points `ReplacedById` at the new row, same `FamilyId`. Presenting an already-rotated token revokes every active token in the family (`auth.refresh_reuse_detected`).

Logout revokes the presented token only, not the family.

Password change revokes all active refresh tokens for that user.

Cleanup job deletes tokens expired or revoked for more than one day (`JobRunner.CleanupRefreshTokensAsync`).

## Client credentials

`POST /api/v1/auth/client-token` with `clientId` and `clientSecret`. Lookup uses `IgnoreQueryFilters` because the caller is anonymous. Secret compared with `CryptographicOperations.FixedTimeEquals` on the hex hashes. Client must be `Active` and tenant `Active`. Disabled or revoked clients fail as `AUTH_INVALID_CLIENT`.

Access token lifetime `Jwt:ClientTokenMinutes` (default 60). Claims: `sub` (client row id), `client_id`, `name`, `tid`, `typ_lic=client`, `scope` (space-separated), `jti`.

`LastUsedAt` is set with `ExecuteUpdateAsync` before the audit `SaveChanges`. There is no refresh token for clients.

## Login failures

Unknown email and wrong password both return `AUTH_INVALID_CREDENTIALS`. Lockout returns `AUTH_LOCKED` (HTTP 423). Inactive user or inactive tenant also returns `AUTH_INVALID_CREDENTIALS` (no distinct code). Failed-attempt audit is written; unknown email uses `WriteNowAsync` because there is no user row to save with.

## Authorization policies

`AuthPolicies.Register` in `src/Licensing.Api/Infrastructure/Authorization.cs`.

`HasPermissionAttribute` → policy `perm:{permission}`: authenticated, `typ_lic=user`, claim `perm`.

`RequireScopeAttribute` → policy `scope:{scope}`: authenticated, `typ_lic=client`, scope string contains the scope.

`platform-admin` policy exists (`AuthPolicies.PlatformAdmin`) and is not the attribute used by tenant routes; tenant routes use permissions, which only PlatformAdmin has for `tenants.*`.

`ProblemAuthorizationResultHandler` writes 401 `AUTH_UNAUTHORIZED` or 403 `AUTH_FORBIDDEN` as ProblemDetails.

## Tenant scope

`HttpCurrentUser` reads only claims. `TenantContext`:

- System (`RunAsSystem`): unrestricted. Optional system tenant id.
- PlatformAdmin: unrestricted. `TenantId` is `X-Tenant-Id` when the header parses as a Guid, else null (all tenants).
- Everyone else: `TenantId` and `CustomerId` from claims. Header is ignored.
- Anonymous: not unrestricted, tenant null. Filters then hide tenant-owned rows (`BuildFilter` requires a tenant match unless unrestricted and tenant null).

Customer-owned queries add `CustomerId` when the claim is set. `ActivationAttempt` is only tenant-owned, so a customer user's raw query of attempts would see the whole tenant. The failed-activation report adds `scope.CustomerId` itself. There is no public list-all-attempts endpoint.

## Data-protection and secrets

`AddDataProtection().SetApplicationName("LicensingPlatform")`. Optional file key ring `DataProtection:KeyRingPath` (`FileSystemXmlRepository`).

`ISecretStore` / `FileSecretStore` holds license private keys under `LicenseSigning:KeyStorePath` (default `App_Data/signing-keys`). Public keys are rows in `licensing.SigningKeys`.

`Jwt:SigningKey` shorter than 32 bytes throws when a token is created or the bearer options call `GetKey`. Empty pepper throws on first HMAC (`HmacSecretHasher` constructor via options).

Development values are in `appsettings.Development.json` (committed, prefixed `DEV-ONLY-`). Production file leaves them empty. `docs/operations.md` says to supply them from a secret store.

## Frontend session

`portal/src/app/core/auth.service.ts`: access token in a signal (memory). Refresh token in `sessionStorage` key `lic.refresh`. Platform tenant scope in `sessionStorage` `lic.tenantScope`, sent as `X-Tenant-Id` only when `role === PlatformAdmin'`.

`authInterceptor` retries once on 401 via refresh. `authGuard` requires a user profile. `permissionGuard` checks permission strings from the profile, which came from the server at login. Hiding a screen is not the security boundary.
