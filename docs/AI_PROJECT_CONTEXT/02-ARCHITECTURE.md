# 02 — Architecture

Verified from project references, `Program.cs`, DI, `AppDbContext`, and `tests/Licensing.Tests/Architecture/ArchitectureTests.cs`. Folder names were not treated as proof.

## Pattern that the code actually implements

**Modular monolith, one deployable, one database, layered inside the process.**

It is not microservices. It is not a full Clean Architecture core: the application layer references `Microsoft.EntityFrameworkCore` (`Licensing.Application.csproj`) and `IAppDbContext` exposes `DbSet<T>`. Domain entities do not reference EF or ASP.NET (architecture test 3).

| Pattern | Present? | Evidence |
|---|---|---|
| Layered (kernel → domain → application → infrastructure → api) | Yes | Project references and architecture tests 1–5 |
| Modular monolith | Partial | Only Catalog has `src/Modules/<Name>`. Other modules are namespaces inside Domain/Application. ADR-015 |
| DDD aggregates | Yes, pragmatic | `AggregateRoot`, private setters, domain exceptions, domain events. No separate repository per aggregate |
| Repository | No | Services query `IAppDbContext` directly |
| Unit of work | Yes, as `DbContext.SaveChangesAsync` | `AppDbContext.SaveChangesAsync` also writes the outbox and rejects cross-tenant writes |
| CQRS | Partial | Catalog products and plans go through `IDispatcher`. All other modules are service methods. ADR-005 plus ADR-014 |
| Event-driven | In-process outbox | `WriteDomainEventsToOutbox` then `OutboxDispatcher`. No external bus |
| Global query filters for tenancy | Yes | `AppDbContext.OnModelCreating` |
| Optimistic concurrency | Subscription only | `Subscription.Version` is `IsConcurrencyToken()` |

## Request path

Most portal operations:

`Angular feature` → `Api` (`portal/src/app/core/api.service.ts`) → controller with `[HasPermission]` → application service → domain method → `IAppDbContext` → SQL Server.

Catalog writes and reads:

controller in `BusinessControllers.cs` → `IDispatcher.SendAsync` / `QueryAsync` → `LoggingBehavior` then `ValidationBehavior` → internal handler → same `IAppDbContext`.

Device licensing:

API client JWT → `LicensingController` `[RequireScope]` → `DeviceLicensingService` → license aggregate rules + atomic `ExecuteUpdate` for activation slots → `ILicenseTokenSigner`.

## Dependency rules enforced in CI

`ArchitectureTests` (NetArchTest):

1. SharedKernel does not reference Domain, Application, Infrastructure, Api.
2. Domain does not reference Application, Infrastructure, Api.
3. Domain does not reference EF Core, ASP.NET, or `System.Net.Http`.
4. Application does not reference Infrastructure or Api.
5. Infrastructure does not reference Api.
6. Controllers do not use `AppDbContext`, `Licensing.Infrastructure.Services`, or EF directly.
7. Domain entity properties have no public setters (records excluded).
8. Named tenant-owned types implement `ITenantOwned`.
9. BuildingBlocks depend only on SharedKernel (plus its own packages: FluentValidation, logging, DI).
10. Catalog module does not reference Infrastructure, Api, or other modules' domain namespaces.
11. Shared layers do not reference `Licensing.Modules`.
12. Each Catalog command/query has exactly one handler, and handlers are `internal`.

Known architectural debt (do not "fix" unless asked):

- One `AppDbContext` for every module. Catalog still uses `Licensing.Domain.Catalog`.
- `IAppDbContext` is a persistence seam, not a domain port.
- Application services mix commands and queries in the same class.
- `SigningKeysController` injects `IAppDbContext` only to `SaveChanges` after rotate (`BusinessControllers.cs`). The architecture test allows this because the controller is in `Licensing.Api.Controllers` and the forbidden dependency is `Licensing.Infrastructure.Persistence`, while `IAppDbContext` lives in Application. The controller still reaches the unit of work.

## Cross-cutting pipeline

API:

- `CorrelationIdMiddleware` (`X-Correlation-Id`, max 64, alphanumeric/`-`/`_`).
- `SecurityHeadersMiddleware`.
- `GlobalExceptionHandler` maps `DomainException` to ProblemDetails; other exceptions become HTTP 500 `INTERNAL_ERROR` and an audit row `system.error`.
- `ValidationFilter` runs FluentValidation on action arguments.
- Model binding failures use `InvalidModelStateResponseFactory` in `Program.cs` (`VALIDATION_FAILED`).
- Serilog request log is method, path, status, elapsed. Comment in `Program.cs`: query strings and bodies are not logged.

CQRS pipeline (`Behaviors.cs`): logging outermost, then validation. Validation returns `Result` failure when `TResult` is `Result` or `Result<T>`; otherwise throws `ValidationException`.

## Transactions

Default: one `SaveChangesAsync` is one implicit transaction.

Outbox handler path (`OutboxProcessor.DispatchAsync`): explicit `BeginTransaction` per handler, inbox row inserted in the same transaction.

Activation slot reservation is a separate `ExecuteUpdateAsync` before the activation row is saved (`DeviceLicensingService.ActivateAsync`). A unique index on `(LicenseId, DeviceId)` plus slot release on `DbUpdateException` handles the same-device race.

## What was not found

No MediatR, Hangfire, Quartz, MassTransit, separate read database, API gateway, or second bounded-context host.
