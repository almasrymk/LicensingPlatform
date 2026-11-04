# 09 — Execution flows

## Startup — Confirmed

`Program.cs`:

1. Serilog from configuration, console plus rolling warning file under `App_Data/logs`.
2. `AddApplication`, `AddCatalogModule`, `AddInfrastructure`.
3. Controllers, validation filter, ProblemDetails, OpenAPI v1, JWT, authorization, rate limiter, CORS, health checks.
4. Middleware order: correlation id, security headers, exception handler, Serilog request logging, HSTS and HTTPS outside Development and Testing, CORS, authentication, rate limiter, authorization.
5. Map OpenAPI, controllers, health, static files, SPA fallback.
6. `await app.Services.InitializeDatabaseAsync()` then `RunAsync`.

`InitializeDatabaseAsync` resolves `DbInitializer` (extension in the persistence namespace; called from `Program.cs`). Migrations and optional demo seed run before the host accepts traffic. A migration failure prevents startup.

Background services start with the host: `OutboxDispatcher`, `ScheduledJobsService`.

## Login — Confirmed

`POST /api/v1/auth/login` → `ValidationFilter` (`LoginValidator`) → `AuthService.LoginAsync`.

Normalize email trim+lower. Load user (no tenant filter). Lockout check. PBKDF2 verify (`Pbkdf2PasswordHasher`, 210_000 iterations, SHA256). On failure increment counter and save. On success check `IsActive` and tenant status, reset counter, issue access JWT and refresh token, audit `auth.login`, save.

Portal: `AuthService.login` stores refresh in sessionStorage and profile in a signal (`portal/src/app/core/auth.service.ts`). Later calls attach `Authorization: Bearer`.

## Refresh and logout — Confirmed

Refresh hashes the presented token, loads `RefreshTokens` (no tenant filter; hash is global unique). Reuse detection described in `07`. Logout is anonymous and idempotent if the token is unknown.

## Issue a license — Confirmed

Portal user with `licenses.manage` → `POST /api/v1/licenses` body `subscriptionId` → `LicenseService.IssueAsync`.

Subscription must be in scope (query filter) and `IsUsable`. Plan joined to product for entitlements. `ProductKeyGenerator.Generate` returns plaintext once. `License.Issue` raises `LicenseIssuedV1` (outbox, no consumer). Audit stores number and prefix only. Response DTO includes `productKey`.

## Renew a subscription — Confirmed

`POST /api/v1/subscriptions/{id}/renew` → `SubscriptionService.RenewAsync` → optional version check → `Subscription.Renew` → non-revoked licenses `Reinstate` → audit → save (outbox gets `SubscriptionRenewedV1`) → `LicenseCacheInvalidation.ForSubscriptionAsync`.

There is no handler that expires licenses on renew. Reinstate is synchronous in this method. Expiry of licenses after subscription expiry is asynchronous via outbox.

## Device activate — Confirmed

1. Client `POST /api/v1/auth/client-token`.
2. `POST /api/v1/licensing/activate` with bearer client token and scope `licenses.activate`.
3. Rate limiter partition `client:{client_id}`.
4. Optional idempotency lookup.
5. `DeviceLicensingService.ActivateAsync` (rules in `04`).
6. Response includes signed token, `checkAfter`, `offlineValidUntil`.
7. On failure, an `ActivationAttempt` row and audit `license.activation_failed` are saved, then ProblemDetails.

Reference agent `check` verifies the token offline. That path does not hit the API. **Confirmed** by README and the tool's purpose; the agent's verify routine was not line-traced in this pass (see coverage).

## Validate and heartbeat — Confirmed

Validate may return a cached snapshot for 60 seconds, including a snapshot that fails `CheckRules` (the cache is written before the rule check in `CheckAsync`). A suspend removes the cache in `LicenseService.MutateAsync` before the next request. If removal fails, a revoked license can still validate until TTL. **Confirmed** ordering.

Heartbeat always reads SQL for the snapshot (`heartbeat ? null : cache`) and updates the activation row.

## Hourly jobs — Confirmed

`ScheduledJobsService` loops until cancelled. Each iteration:

1. `SubscriptionService.ExpireDueAsync`
2. `LicenseService.ExpireDueAsync`
3. `ReportService.AggregateUsageAsync`
4. If maintenance interval elapsed (default 1440 minutes): refresh-token cleanup, idempotency cleanup, audit purge

Each job opens a scope and `RunAsSystem`. Exceptions are logged and the loop continues. Interval default 60 minutes (`Jobs:ExpiryIntervalMinutes`). `Jobs:Enabled=false` returns immediately.

Expire-subscriptions raises domain events. The outbox dispatcher, not the job, expires related licenses for subscription-ended events. `ExpireLicensesAsync` expires licenses whose own `ExpiresAt` is due. Both paths exist. **Confirmed** by the two methods.

## Outbox — Confirmed

`OutboxDispatcher` polls every `Outbox:PollSeconds` (default 5) when the batch was empty; it continues immediately when a batch was non-empty.

`ProcessBatchAsync` loads unprocessed, not dead-lettered, due messages, ordered by `OccurredAt`, take `BatchSize`. No `UPDLOCK` / status claim column. Two API instances can read the same row. Handlers are idempotent for the registered cases (`TryExpire`, cache delete). Inbox unique key `(MessageId, Handler)` stops a second commit of the same handler after the first commits. A true parallel double-execution before either insert is possible. ADR-006 says overlapping runs are safe because jobs are idempotent; that claim is reasonable for the current handlers and is not a general lock.

Dead-letter after `MaxAttempts` (default 8) with exponential backoff capped at 3600 seconds.

## Exception path — Confirmed

`DomainException` → ProblemDetails with the domain code, status from `ErrorKind`.

`BadHttpRequestException` → 400 `BAD_REQUEST`.

Anything else → log error, audit `system.error` with method, path, exception type, message, first stack frame (truncated 1900), HTTP 500 `INTERNAL_ERROR`. Audit failure is swallowed.

`DbUpdateConcurrencyException` is converted to `DomainException` `CONCURRENCY_CONFLICT` before it leaves `SaveChangesAsync`, so it follows the domain path (409), not the 500 path.

## Portal navigation — Confirmed

`app.routes.ts` home redirect: if the user has `reports.read`, `/dashboard`, else `/settings`. All feature routes sit under `Shell` and `authGuard`. Permission guards match the permission matrix. Notifications page is not a separate backend resource; it reads `api.overview()` and links to licenses and reports (`notifications.ts`).
