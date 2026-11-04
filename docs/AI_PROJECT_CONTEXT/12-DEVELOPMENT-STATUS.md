# 12 — Development status

Status is based on whether a caller can complete the business flow in code, not on the existence of a class. Flows were not executed against SQL Server in this run, so "Completed" means the path is implemented and covered by tests that exist in the repo, or the path is fully wired with no stub. "Needs verification" means the code looks finished and this analysis did not execute it.

## Completed (implemented end to end in code)

| Feature | Evidence |
|---|---|
| User login, refresh rotation, reuse detection, logout, password change, language | `AuthService`, `AuthController`, tests in `AuthAndIsolationTests` |
| Client credentials | `ClientTokenAsync`, licensing tests |
| Tenant CRUD, suspend, resume | `TenantService` |
| Customer CRUD, contacts, status | `CustomerService` |
| Users CRUD, activate, reset password, role change for staff | `UserService` |
| Products and plans including publish, archive, version, current trial/free | Catalog handlers |
| Subscription start, renew, suspend, resume, cancel, hourly expire | `SubscriptionService` |
| License issue, suspend, resume, revoke, regenerate key, reset device | `LicenseService` |
| Device activate, validate, heartbeat, deactivate, trial, free | `DeviceLicensingService`, `LicensingTests` |
| Idempotency on activate | `LicensingController.Activate` |
| Reports dashboard, overview, trend, expiring, devices, failed activations, usage | `ReportService` |
| Audit list and retention purge | `AuditQueryService` |
| API clients create, rotate, disable, enable, revoke | `ApiClientService` |
| Signing key public list and rotate | `SigningKeysController` |
| Image upload and delete | `MediaService`, `MediaAndFilterTests` |
| Outbox + inbox for subscription end → license expire, and cache invalidation | `Handlers.cs`, `OutboxDispatcher` |
| Portal screens for the routes in `app.routes.ts` | feature components exist and call `Api` |
| Reference agent CLI | `tools/Licensing.ReferenceAgent` |
| CI build, test, OpenAPI pending-model check, portal test and build | `.github/workflows/ci.yml` |

## Partially implemented

| Feature | What is missing |
|---|---|
| Webhooks | Table, validation (https URL), list, create. No delivery worker, no event subscription matching, secret not returned |
| API scopes `licenses.issue` and `licenses.read` | Stored and issued on tokens. No endpoint requires them |
| Catalog modularization | Application assembly only. Domain and DbContext still shared (ADR-015 says that is intentional and unfinished) |
| Billing | Revenue KPI is plan price. No invoices or payments |
| Notifications | Portal page is a projection of overview attention counts. No notification store |
| Multi-instance hosting | Jobs and outbox and rate limits assume one process or idempotent overlap. Redis is optional and does not back the rate limiter |
| Demo seed | Implemented, off in Development appsettings. README describes it as on |

## Not implemented

- Self-service password reset and email.
- Payment provider.
- Per-tenant license signing keys.
- External secret manager (Key Vault) implementation.
- Webhook sender.
- Soft delete / recycle bin.
- Separate worker process.

## Broken

No feature was executed and observed failing in this analysis. `ChangePlan` duration handling is incorrect relative to its parameter (`10-CODE-QUALITY-ISSUES.md` H1). Treat that operation as behaviorally wrong, not as an absent endpoint.

## Legacy or deprecated

No obsolete API version. All routes are `api/v1`. ADR-005 (no mediator) and ADR-014 (internal dispatcher for new modules) both apply: old services stay as services; new extracted modules use the dispatcher. Do not mass-convert services to commands unless a task asks for that.

## Tests present

xUnit `[Fact]` / `[Theory]` attributes were counted in `tests/Licensing.Tests` (about 99 attributes; several theories have extra `InlineData` rows). README says 125 tests. **The README count was not re-run.** Do not quote 125 as a fresh result until `dotnet test` is run.

Areas with API tests: auth and tenant isolation, licensing concurrency and idempotency, media and filters, OpenAPI snapshot, end-to-end scenario, catalog module, domain state machines, dispatcher, architecture rules.

Portal: `core.spec.ts` only.

No test project for the reference agent.

## Build status

Not run in this analysis. CI definition expects `dotnet test` on SQLite and `ng test` / `ng build`. Pending EF model changes fail `dotnet ef migrations has-pending-model-changes`.
