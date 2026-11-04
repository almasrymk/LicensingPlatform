# 11 — Security risks

These are observations for maintainers of this repository. They are not exploit procedures. Do not weaken filters or auth while "fixing" them.

## Critical

None confirmed that gives unauthenticated cross-tenant business data or a hardcoded production signing key that the app uses when `ASPNETCORE_ENVIRONMENT` is Production.

`appsettings.json` leaves `Jwt:SigningKey` and `Security:SecretPepper` empty. The process throws if they stay empty. Production misconfiguration fails closed for token creation.

## High

### S1 — Development secrets are in source control

- File: `src/Licensing.Api/appsettings.Development.json`.
- Values: `Jwt:SigningKey` and `Security:SecretPepper` prefixed `DEV-ONLY-`. Seed admin password `Admin@12345` when bootstrap runs.
- `DemoAccounts` in `DbInitializer.cs` and `README.md` contain demo client secrets and product keys. They are inert unless `Seed:DemoData=true`.
- Impact: anyone who runs the app with the Development environment and a copied database can forge development JWTs and recompute development HMAC hashes. Production is safe only if it does not use these values.
- Suggestion: keep the file for local boot, and confirm deploy pipelines set `ASPNETCORE_ENVIRONMENT=Production` with secrets from a store. `.gitleaks.toml` and the gitleaks CI job already exist; these strings are allowlisted or worded so the scan stays green. Do not copy them into production appsettings.
- Confidence: high.

### S2 — Multi-instance rate limits are per process

- `RateLimits` uses in-memory fixed and sliding windows (`AddLicensingRateLimits`).
- Comment in that file says a multi-instance deployment should move them to Redis. `docs/operations.md` Redis runbook covers cache, not the limiter.
- Impact: login and licensing quotas multiply by instance count.
- Confidence: high.

## Medium

### S3 — Uploaded images are world-readable given the id

- `GET /api/v1/media/{id}` is `[AllowAnonymous]` and cached `public, max-age=86400`.
- `MediaFile` has no query filter. `GetAsync` loads by id only.
- Ids are random UUIDs, which is the stated control (`MediaController` comment). Upload still requires permission and the owner must be visible in the caller's scope.
- Impact: a leaked URL (referrer, log, shared screenshot) exposes the image for a day in shared caches. User photos and customer logos are in this bucket.
- Suggestion: if the product later stores anything sensitive, require a short-lived signed URL or an auth cookie. Do not change this without checking every `<img>` in the portal (`shared/media.ts`), because browsers will not send a bearer token on a plain image GET unless cookies are introduced.
- Confidence: high on the behavior. Severity is medium because content is logos and photos, not product keys.

### S4 — Access tokens keep permissions until they expire

- Role or deactivation is not consulted on each request. JWT validation is signature, issuer, audience, lifetime.
- Deactivated users and suspended tenants cannot refresh (`AuthService.RefreshAsync` checks `IsActive` and tenant). Existing access tokens work until `AccessTokenMinutes` (15).
- Client disable/revoke does not revoke the client JWT. It expires within `ClientTokenMinutes` (60). `docs/operations.md` states the client case.
- Confidence: high.

### S5 — Cross-tenant safety depends on a few manual filters

Global filters cover `ITenantOwned` / `ICustomerOwned`. These do not:

- `Users`, `RefreshTokens` (by design for login).
- `AuditRecords` (filtered in `AuditQueryService` only).
- `MediaFiles`.
- `SigningKeys`.
- `Tenants` (platform list is permission-gated; the table itself is not filtered, so any future query from a tenant scope must not assume a filter).

`IgnoreQueryFilters` sites are listed in `05-DATABASE.md`. A new query that copies one of them without a tenant predicate is the likely regression.

`EnforceTenantOnWrites` returns immediately when `IsUnrestricted`, which includes every PlatformAdmin request even when `X-Tenant-Id` is set. A platform admin can write another tenant's id if a service assigns `TenantId` from the body. `ResolveWriteTenant` is the control for services that call it. Not every write path was re-checked line by line in this pass (Customer, Product, Subscription start were sampled and use the customer/plan tenant). Confidence: medium.

### S6 — Audit details can contain exception messages

`GlobalExceptionHandler.RecordAsync` stores exception type and message in `audit.AuditRecords.Details`. Messages sometimes include SQL or file paths. Product keys are not placed there by the license services (they log prefixes). A future exception from a binder could. Retention is 365 days by default.

### S7 — No security headers for CSP on HTML

`SecurityHeadersMiddleware` sets `nosniff`, `DENY` frames, referrer, permissions policy, COOP. It does not set `Content-Security-Policy` on the SPA (CSP is set on image responses only). HSTS is off in Development. Acceptable for the current inline-template Angular app; revisit before a strict CSP.

## Low

### S8 — CORS `AllowAnyHeader` and `AllowAnyMethod` for the configured origin

Origin list defaults to localhost:4200. Production must set `Cors:Origins`. Confidence: high that the default is local.

### S9 — Login audit records the failure reason "unknown email"

The HTTP body does not. The audit log lets a tenant reader with `audit.read` see that an email was unknown only for users in their tenant; unknown emails are stored with null tenant (`WriteNowAsync` tenant null), and tenant audit queries filter `TenantId == scope`. Platform admins see them. Low information disclosure inside the audit permission.

## Controls that are in good shape

- PBKDF2 passwords, HMAC pepper for secrets and product keys, fixed-time compare on client secrets.
- Product key never returned except issue, regenerate, trial, and free start.
- Query filters plus write guard for tenant-owned rows.
- Permission claims checked on the server, separate from the Angular guards.
- Device endpoints require a client token and a scope.
- Request logging omits query strings.
- Image sniffing rejects non-raster types.
- CI: `dotnet list package --vulnerable`, npm audit, CodeQL `security-extended`, gitleaks.
- Architecture tests stop controllers from taking a raw `DbContext`.
