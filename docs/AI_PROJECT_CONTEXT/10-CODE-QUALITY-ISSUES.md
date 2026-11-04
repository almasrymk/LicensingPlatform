# 10 — Code quality issues

Analysis only. Nothing in `src/` or `portal/` was changed. Confidence is how directly the statement was read in code.

## High

### H1 — `Subscription.ChangePlan` ignores a non-null duration

- File: `src/Licensing.Domain/Subscriptions/Subscription.cs`, method `ChangePlan` (around lines 100–110).
- Caller: `SubscriptionService.ChangePlanAsync` passes `plan.DurationDays`.
- The method sets `EndDate = null` only when `durationDays` is null. A finite duration is discarded. Licenses are not updated (unlike `RenewAsync`).
- Impact: changing to a 30-day plan can leave the old end date, or changing to a lifetime plan (null duration) clears the end date. The two cases are inconsistent, and entitlements on existing licenses stay at issue-time snapshot (that part matches ADR-013).
- Suggestion: decide the product rule, then either apply the new end date and reinstatement or remove the unused parameter so callers cannot think it works.
- Confidence: high.

### H2 — Trial and free markers are not unique

- File: `DeviceLicensingService.StartTrialAsync` / `StartFreeAsync`.
- Uniqueness is `Subscriptions.Notes` equality. Two concurrent requests can both pass `AnyAsync` and insert two trials. No unique index.
- Impact: a device can receive more than one trial or free license under concurrency.
- Suggestion: a unique filtered index on `(TenantId, ProductId, Notes)` for the marker prefix, or a dedicated table.
- Confidence: high on the missing constraint. Race not executed in this run.

### H3 — Outbox batch is not claimed

- File: `src/Licensing.Infrastructure/Messaging/OutboxDispatcher.cs`, `ProcessBatchAsync`.
- Two processes can load the same messages. Current handlers are idempotent. A future handler that sends email or charges money would not be safe.
- Suggestion: update `NextAttemptAt` or a claim token in the same statement that selects, or use SQL Server locking hints in a single dispatcher deployment.
- Confidence: high on the code path. ADR-006 already says one process is the MVP.

## Medium

### M1 — Dashboard and overview issue many sequential queries

- `ReportService.DashboardAsync` awaits a long series of `CountAsync` calls, then loads 14 days of attempts into memory.
- `OverviewAsync` loads every subscription row in scope to sum prices (`SubscriptionRowsAsync`).
- Impact: fine for demo volumes; a large tenant will hold the request thread and the SQL connection for many round trips.
- Suggestion: one grouped SQL query when this becomes hot. Do not rewrite during unrelated features.
- Confidence: high.

### M2 — `ListActivationsAsync` builds `Concat` per heartbeat interval

- `LicenseService.ListActivationsAsync` loads distinct intervals then unions filtered queries.
- Correctness comment says providers cannot translate a per-row cutoff. The shape is easy to get wrong if a new filter is added after `Concat`.
- Confidence: medium (read, not profiled).

### M3 — Customer user can read a draft plan by id

- `ListPlansHandler` filters `Published` when `scope.CustomerId` is set. `CatalogReads.GetPlanAsync` does not.
- Impact: unpublished price and features inside the same tenant.
- Confidence: high.

### M4 — `IsCurrent` has no database uniqueness

- Migration `PlanKindAndCurrentOffer` adds `Kind` and `IsCurrent` with no filtered unique index.
- `MakeCurrent` relies on the handler clearing the previous row. Concurrent calls can mark two plans current. `CurrentOfferAsync` then takes the highest version.
- Confidence: high on the missing index.

### M5 — Webhook secret is discarded

- `ApiClientService.CreateWebhookAsync` hashes `hasher.GenerateToken()` and returns the DTO without the secret.
- Even after a sender exists, the tenant cannot verify a signature.
- Confidence: high.

### M6 — Application layer depends on EF Core

- `Licensing.Application.csproj` references EF Core. `IAppDbContext` exposes `DbSet`.
- This matches the current architecture tests. It is debt relative to a strict domain core, not a bug.
- Confidence: high.

### M7 — README demo-data claim does not match Development settings

- `README.md` says the first Development run loads demo data.
- `appsettings.Development.json` has `"DemoData": false`.
- Operators following the README will not see demo tenants unless they flip the flag.
- Confidence: high.

### M8 — Unused API scopes

- `licenses.issue` and `licenses.read` are valid scopes and are placed on client tokens. No action uses `[RequireScope]` for them.
- Impact: an integrator can think those scopes authorize issue/read.
- Confidence: high.

## Low

### L1 — `ErrorKind` has `Gone` and `TooManyRequests` with almost no domain use

Rate limiting builds an `Error` directly. Not a defect.

### L2 — Portal tests cover core helpers only

`portal/src/app/core/core.spec.ts`. Feature components have no component tests in the tree that was listed.

### L3 — Reference agent JWT package lags the API

Agent `System.IdentityModel.Tokens.Jwt` 8.16.0 versus API 8.23.0. Both are direct references. Not a functional defect by itself.

### L4 — No TODO/FIXME markers

Search of `*.cs` and `*.ts` found no `TODO` or `FIXME`. Incomplete work is expressed as comments ("Foundation only", "Phase 4") rather than markers.

## Not treated as bugs

- License entitlements stay snapshotted after plan edits. ADR-013 and `Plan` comments state that.
- Anonymous `GET /signing-keys` returns public keys. Required for offline verify.
- `Media` GET by random Guid is an explicit design comment on `MediaController`. Risk is recorded under security, not as an accidental missing attribute.
