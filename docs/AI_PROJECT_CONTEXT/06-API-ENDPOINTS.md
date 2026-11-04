# 06 — API endpoints

Base path `api/v1`. JSON enums are strings (`JsonStringEnumConverter` in `Program.cs`). Errors are ProblemDetails with extension `code` and `traceId` (`ApiResults.Problem`).

Auth shorthand:

- **anon** — `[AllowAnonymous]`
- **user** — policy `any-user` (`typ_lic=user`)
- **perm:X** — user token plus permission claim
- **scope:X** — client token plus scope

## Auth — `AuthController`

| Method | Path | Auth | Service |
|---|---|---|---|
| POST | `/api/v1/auth/login` | anon, rate `login` | `AuthService.LoginAsync` |
| POST | `/api/v1/auth/refresh` | anon, rate `login` | `RefreshAsync` |
| POST | `/api/v1/auth/logout` | anon | `LogoutAsync` → 204 |
| POST | `/api/v1/auth/client-token` | anon, rate `client-token` | `ClientTokenAsync` |
| GET | `/api/v1/auth/me` | user | `GetProfileAsync` |
| PUT | `/api/v1/auth/me/language` | user | `SetLanguageAsync` (`en` or `ar`) |
| PUT | `/api/v1/auth/me/password` | user | `ChangePasswordAsync` (revokes refresh tokens) |

## Tenants, customers, users, audit — `AdminControllers.cs`

| Method | Path | Auth |
|---|---|---|
| GET | `/api/v1/tenants` | perm:tenants.read |
| GET | `/api/v1/tenants/{id}` | perm:tenants.read |
| POST | `/api/v1/tenants` | perm:tenants.manage |
| PUT | `/api/v1/tenants/{id}` | perm:tenants.manage |
| POST | `/api/v1/tenants/{id}/suspend` | perm:tenants.manage |
| POST | `/api/v1/tenants/{id}/resume` | perm:tenants.manage |
| GET | `/api/v1/customers` | perm:customers.read |
| GET | `/api/v1/customers/countries` | perm:customers.read |
| GET | `/api/v1/customers/{id}` | perm:customers.read |
| POST | `/api/v1/customers` | perm:customers.manage |
| PUT | `/api/v1/customers/{id}` | perm:customers.manage |
| PUT | `/api/v1/customers/{id}/status` | perm:customers.manage |
| POST | `/api/v1/customers/{id}/contacts` | perm:customers.manage |
| DELETE | `/api/v1/customers/{id}/contacts/{contactId}` | perm:customers.manage |
| GET | `/api/v1/users` | perm:users.manage |
| GET | `/api/v1/users/{id}` | perm:users.manage |
| POST | `/api/v1/users` | perm:users.manage |
| PUT | `/api/v1/users/{id}` | perm:users.manage |
| POST | `/api/v1/users/{id}/activate` | perm:users.manage |
| POST | `/api/v1/users/{id}/deactivate` | perm:users.manage |
| POST | `/api/v1/users/{id}/reset-password` | perm:users.manage |
| GET | `/api/v1/audit` | perm:audit.read |

## Catalog, subscriptions, licenses, integrations, reports — `BusinessControllers.cs`

Products and plans go through `IDispatcher`. Other controllers call services.

| Method | Path | Auth |
|---|---|---|
| GET, GET `{id}`, POST, PUT `{id}` | `/api/v1/products` | catalog.read / catalog.manage |
| GET, GET `{id}`, GET `{id}/entitlements` | `/api/v1/plans` | catalog.read |
| POST, PUT `{id}` | `/api/v1/plans` | catalog.manage |
| POST `{id}/publish`, `/archive`, `/make-current`, `/clear-current`, `/new-version` | `/api/v1/plans` | catalog.manage |
| DELETE `{id}` | `/api/v1/plans` | catalog.manage |
| GET, GET `{id}` | `/api/v1/subscriptions` | subscriptions.read |
| POST, POST `{id}/renew`, `/change-plan`, `/suspend`, `/resume`, `/cancel` | `/api/v1/subscriptions` | subscriptions.manage |
| GET, GET `{id}` | `/api/v1/licenses` | licenses.read |
| POST, POST `{id}/suspend`, `/resume`, `/revoke`, `/regenerate-key` | `/api/v1/licenses` | licenses.manage |
| GET | `/api/v1/activations` | licenses.read |
| POST | `/api/v1/licenses/{id}/activations/{activationId}/reset` | licenses.manage |
| GET, POST | `/api/v1/api-clients` | apiclients.manage |
| PUT `{id}`, POST `{id}/rotate-secret`, `/disable`, `/enable`, `/revoke` | `/api/v1/api-clients` | apiclients.manage |
| GET, POST | `/api/v1/webhooks` | apiclients.manage |
| GET | `/api/v1/reports/dashboard` | reports.read |
| GET | `/api/v1/reports/overview` | reports.read |
| GET | `/api/v1/reports/trend` | reports.read |
| GET | `/api/v1/reports/expiring-licenses` | reports.read |
| GET | `/api/v1/reports/active-devices` | reports.read |
| GET | `/api/v1/reports/failed-activations` | reports.read |
| GET | `/api/v1/reports/usage` | reports.read |
| GET | `/api/v1/signing-keys` | **anon**, response cache 300s |
| POST | `/api/v1/signing-keys/rotate` | perm:signingkeys.manage |

`GET /api/v1/signing-keys` returns public EC JWK (`kty`, `crv`, `x`, `y`, `kid`, `alg`, `use`, `status`) and PEM. Private keys are not in this payload (`SigningKey` stores public PEM only).

## Device licensing — `LicensingController`

Rate limit policy `licensing` (sliding window, partitioned by client id).

| Method | Path | Scope |
|---|---|---|
| POST | `/api/v1/licensing/activate` | licenses.activate. Optional header `Idempotency-Key` |
| POST | `/api/v1/licensing/validate` | licenses.validate |
| POST | `/api/v1/licensing/heartbeat` | licenses.validate |
| POST | `/api/v1/licensing/trial` | licenses.activate |
| POST | `/api/v1/licensing/free` | licenses.activate |
| POST | `/api/v1/licensing/deactivate` | licenses.activate |

`licenses.issue` and `licenses.read` exist on `ApiScopes` and can be stored on a client. No controller action requires them. **Confirmed** unused scopes.

## Media — `MediaController`

| Method | Path | Auth |
|---|---|---|
| GET | `/api/v1/media/{id}` | anon |
| PUT | `/api/v1/tenants/{id}/image` | tenants.manage |
| PUT | `/api/v1/customers/{id}/image` | customers.manage |
| PUT | `/api/v1/products/{id}/image` | catalog.manage |
| PUT | `/api/v1/users/{id}/image` | users.manage |
| PUT | `/api/v1/auth/me/image` | user |
| DELETE | same paths + `/image` | same permissions |

Upload limit: `MediaFile.MaxBytes` (2 MB) plus 64 KB. Content type is sniffed from bytes (PNG, JPEG, GIF, WebP). SVG rejected.

## Platform endpoints — `Program.cs`

| Method | Path | Notes |
|---|---|---|
| GET | `/openapi/v1.json` | `MapOpenApi` |
| GET | `/health/live` | tag `live`, process up |
| GET | `/health/ready` | tag `ready`, `Database.CanConnectAsync` |
| * | static files + fallback `index.html` | anonymous SPA |

## HTTP status mapping

`ApiResults.StatusFor`: Validation 400, NotFound 404, Conflict 409, Forbidden 403, Unauthorized 401, Locked 423, TooManyRequests 429, Gone 410. Unknown kinds fall through to 400.

Rate limit rejection writes ProblemDetails `RATE_LIMITED` and `Retry-After` when the lease provides it.

## Contract test

`tests/Licensing.Tests/Api/OpenApiContractTests.cs` compares the generated OpenAPI document to `tests/Licensing.Tests/Contracts/openapi.v1.json`. API shape changes must update that snapshot (ADR-015).
