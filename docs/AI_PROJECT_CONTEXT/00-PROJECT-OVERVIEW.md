# 00 — Project overview

Analysis date: 2026-11-03 (Africa/Cairo). Evidence is source code, migrations, and existing docs. No runtime database was connected. Application source was not modified.

## What the system is

**Licensing Platform** is a multi-tenant SaaS for software vendors. A **tenant** is the company that sells software. A **customer** is that company's end customer. The platform catalogs products and plans, starts subscriptions, issues product keys, and lets installed software activate, validate, heartbeat, and deactivate devices. Offline operation uses an ECDSA-signed license token.

Confirmed entry narrative: `README.md`, `docs/adr/README.md` (ADR-009).

## Solution

Single solution file: `LicensingPlatform.slnx`.

| Project | Path | Role |
|---|---|---|
| Licensing.Api | `src/Licensing.Api` | ASP.NET Core host, controllers, auth, rate limits, health, static SPA fallback |
| Licensing.Application | `src/Licensing.Application` | Application services for every module except Catalog commands/queries |
| Licensing.Domain | `src/Licensing.Domain` | Aggregates and domain events |
| Licensing.Infrastructure | `src/Licensing.Infrastructure` | EF Core, security, jobs, outbox, seeding |
| Licensing.SharedKernel | `src/Licensing.SharedKernel` | `Entity`, `AggregateRoot`, `Result`, `Guard`, `ValueObject` |
| Licensing.BuildingBlocks.Application | `src/BuildingBlocks/Licensing.BuildingBlocks.Application` | In-process CQRS dispatcher |
| Licensing.Modules.Catalog.Application | `src/Modules/Catalog/Licensing.Modules.Catalog.Application` | Catalog commands and queries |
| Licensing.Tests | `tests/Licensing.Tests` | xUnit unit, architecture, and API tests |
| Licensing.ReferenceAgent | `tools/Licensing.ReferenceAgent` | CLI that simulates a licensed desktop/device client |

The Angular portal lives in `portal/` and is not a project inside the `.slnx`.

## Runtime shape

One process (`Licensing.Api`) hosts HTTP, EF Core, the outbox dispatcher, and scheduled jobs. There is no separate worker, message broker, or second database. Redis is optional (`ConnectionStrings:Redis`). SQL Server is the production provider (`Database:Provider` default `SqlServer`). Tests use SQLite (`tests/Licensing.Tests/Infrastructure/TestApp.cs`).

## Existing documentation (left unchanged)

- `README.md` — local run, demo accounts, isolation summary, test commands.
- `docs/adr/README.md` — ADR-001 through ADR-015.
- `docs/operations.md` — production settings, migrations, backup, runbooks.
- `portal/README.md` — default Angular CLI readme (does not describe product behavior).

Where README and code disagree, code wins. Notable case: README says the first Development run loads demo data. `src/Licensing.Api/appsettings.Development.json` sets `Seed:DemoData` to `false`. Demo data loads only when that flag is true and `DbInitializer.InitializeAsync` runs (`src/Licensing.Infrastructure/Persistence/DbInitializer.cs`).

## Users of the system

| Actor | How they authenticate | What they do |
|---|---|---|
| PlatformAdmin | User JWT (`typ_lic=user`) | All tenants. Optional `X-Tenant-Id` narrows reads and writes |
| TenantAdmin | User JWT | One tenant: customers, catalog, subscriptions, licenses, users, API clients, audit, reports |
| TenantOperator | User JWT | Day-to-day customers, subscriptions, licenses, reports. No user or API-client management |
| CustomerUser | User JWT with `cid` | Read own customer subscriptions, licenses, published catalog, reports |
| API client | Client JWT (`typ_lic=client`) | Device licensing endpoints, scoped (`licenses.activate`, `licenses.validate`, and others) |
| Anonymous | None | Login, refresh, logout, client-token, public signing keys, media bytes by id, health, OpenAPI, SPA files |

Permission matrix: `Permissions.ForRole` in `src/Licensing.Domain/Identity/Roles.cs`.
