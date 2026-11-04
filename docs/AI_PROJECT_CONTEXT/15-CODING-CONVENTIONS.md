# 15 — Coding conventions

Observed from the code. Follow these on future edits.

## C#

- Nullable reference types on, implicit usings on, `net10.0` (`Directory.Build.props`).
- File-scoped namespaces.
- Primary constructors for services, controllers, and DI helpers.
- `sealed` classes for aggregates, services, and controllers.
- Domain collections are private lists exposed as `IReadOnlyCollection`, with EF field access (`UsePropertyAccessMode(PropertyAccessMode.Field)`).
- Aggregates protect invariants with `DomainException` and stable `Error` codes (`SCREAMING_SNAKE` or `LIC_*` / `AUTH_*` / `PLAN_*`).
- Application methods return `Result` / `Result<T>` or `PagedResult<T>`. They do not throw for not-found.
- Guards for strings: `Guard.NotEmpty`. Emails: `EmailAddress.Create` (lowercase).
- Ids: `Guid.CreateVersion7()` from `Entity` unless there is a reason (media uses `Guid.NewGuid()`).
- Time: `TimeProvider` injected as `TimeProvider.System`. Do not call `DateTime.UtcNow` in new domain code. Jobs still use `DateTimeOffset.UtcNow` in `ScheduledJobsService` for the maintenance interval.
- Persistence: add configuration classes in `Configurations.cs`, schema per module, explicit max lengths. Do not rely on the 256 convention alone for business fields.
- New tenant-owned entities must implement `ITenantOwned` or `ICustomerOwned` or they will not be filtered. Add the type name to the architecture test array if it is tenant-owned.
- Cross-module reads go through a port in Infrastructure (`ICatalogUsageReader` is the template).
- New Catalog-style modules: `internal` handlers, one handler per request, register with `AddHandlersFrom`. Controllers call `IDispatcher` only.
- Existing modules: add a method on the existing service. Do not create a second service for the same aggregate.
- Audit: `IAuditLogger.Add` inside the business transaction for success; `WriteNowAsync` for failures that must survive rollback. Never put a full product key, password, refresh token, or client secret in `details`.
- Errors at the edge: `this.ToActionResult(result)` or `ToProblem`. Do not return a different JSON shape.
- Authorization: `[HasPermission]` or `[RequireScope]` on the action. Do not rely on the portal guard.

## TypeScript / Angular

- Standalone components, `inject()`, signals, inline templates.
- HTTP only through `Api` (`api.service.ts`). Feature pages should not build URLs ad hoc.
- User-facing strings go through `i18n.dictionaries.ts` and the `t` pipe. The UI is Arabic and English.
- Route guards: `authGuard` plus `permissionGuard` with `Perm` constants from `api.models.ts`.
- Styling uses logical CSS properties so RTL works without duplicated stylesheets (`styles.scss`, ADR-007).
- No new UI framework.

## Tests

- xUnit, `WebApplicationFactory` via `TestApp`.
- Architecture rules are tests. A new project reference that breaks a layer should fail `ArchitectureTests`.
- Public HTTP changes update `tests/Licensing.Tests/Contracts/openapi.v1.json` by the project's existing contract test, not by hand-editing blindly.
- Default test database is SQLite. SQL Server is opt-in with `LICENSING_TEST_SQLSERVER=1` (README).

## What not to introduce

- MediatR.
- A second `DbContext` without an ADR.
- Hardcoded connection strings, peppers, or keys.
- Catching exceptions in controllers. The global handler is the contract.
- Public setters on entities.
