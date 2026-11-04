# 17 — Testing strategy

## Backend

Command (from README, not re-run in this analysis):

```bash
dotnet test
```

SQL Server opt-in:

```bash
LICENSING_TEST_SQLSERVER=1 dotnet test
```

Project: `tests/Licensing.Tests/Licensing.Tests.csproj`. Host: `Infrastructure/TestApp.cs` (`WebApplicationFactory`). The API exposes `public partial class Program`.

| Area | File | What it locks |
|---|---|---|
| Domain state machines | `Unit/DomainTests.cs` | Subscription and license transitions, plan rules, keys |
| Dispatcher | `Unit/DispatcherTests.cs` | Pipeline and handler resolution |
| Architecture | `Architecture/ArchitectureTests.cs` | Layer and module rules |
| Auth and isolation | `Api/AuthAndIsolationTests.cs` | Tenant and customer boundaries, permissions |
| Licensing HTTP | `Api/LicensingTests.cs` | Activation limit, idempotency, cache, expiry, offline token, outbox. README describes a 50-way race against a limit of 5 |
| End to end | `Api/EndToEndScenarioTests.cs` | Longer scenarios |
| Media and filters | `Api/MediaAndFilterTests.cs` | Images and list filters |
| OpenAPI | `Api/OpenApiContractTests.cs` | Snapshot `Contracts/openapi.v1.json` |
| Catalog module | `Modules/CatalogModuleTests.cs` | Catalog commands through the dispatcher |

CI (`.github/workflows/ci.yml`) on push to `main` and on pull requests:

1. Restore, vulnerability gate (fail on High or Critical), Release build, `dotnet test` with coverage, `dotnet ef migrations has-pending-model-changes`.
2. gitleaks.
3. Portal `npm ci`, npm audit, `ng test --watch=false`, `ng build`.

CodeQL workflow: C# manual build, JavaScript/TypeScript `build-mode: none`, queries `security-extended`, weekly schedule plus PRs.

## Portal

`cd portal && npx ng test --watch=false`. Runner is Vitest (`package.json`). The spec that exists is `portal/src/app/core/core.spec.ts`.

## Load

`k6 run -e BASE=http://localhost:5200 tests/load/validate.js`. Not in CI.

## Gaps

- Reference agent has no automated test project.
- Portal screens are not component-tested.
- This analysis did not execute `dotnet test` or `ng test`. Do not treat the README figure of 125 as freshly verified.
- SQLite is the default. Money and `DateTimeOffset` conversions differ from SQL Server (`AppDbContext.ConfigureConventions`). Periodic SQL Server runs matter for migrations and for `ExecuteUpdate` SQL.

## How to add a test for a future fix

- Domain rule: extend `DomainTests` with the transition and the error code.
- Tenant leak: extend `AuthAndIsolationTests` using two tenants from the existing factory helpers in `TestApp`.
- HTTP contract: extend the API test and regenerate the OpenAPI snapshot the way `OpenApiContractTests` expects.
- Do not add a test that requires demo product keys unless it seeds through the API. Demo constants are for manual runs.
