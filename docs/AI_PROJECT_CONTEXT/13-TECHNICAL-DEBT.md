# 13 — Technical debt

Debt that is already accepted by ADRs should stay until a task asks to continue the ADR. Do not clean it up opportunistically.

## Accepted, documented by the project

1. Single `AppDbContext` and shared `Licensing.Domain` while modules are extracted one at a time (ADR-015). Catalog is step one.
2. Service classes instead of commands for every module except Catalog (ADR-005, ADR-014).
3. In-process `BackgroundService` instead of Hangfire/Quartz (ADR-006).
4. File secret store instead of Key Vault (ADR-004, operations doc).
5. SQL Server rather than the original PostgreSQL note (ADR-001).
6. Angular 21 because the documented Node baseline is 22.19; Angular 22 needs a newer Node (README and ADR-007).
7. In-memory cache when Redis is unset (ADR-012).

## Structural

- Missing database foreign keys between aggregates (`05-DATABASE.md`). Integrity is application-level. Orphan rows will not be rejected by SQL Server.
- `IAppDbContext` in the application layer makes every service an EF query. A module split has to keep that interface or introduce ports (Catalog already did this for license counts).
- Signing keys and JWT keys are process-wide. A tenant compromise of the signing store affects every tenant's offline tokens.
- `Webhook` and unused scopes look like finished API surface.

## Operational

- Jobs, outbox, and rate limits inside the API process. Scaling out the API scales out the workers.
- Startup applies migrations when `Seed:ApplyMigrations` is true. Production guidance is to set it false and run a script. A mis-set flag migrates on boot.
- Audit retention deletes rows. There is no legal-hold flag.
- Image bytes in the OLTP database increase backup size (`MediaFiles.Data`).

## Test and contract

- OpenAPI snapshot must be updated with any public contract change.
- README test count and demo-seed description drift from config.
- Portal feature coverage is thin relative to the API suite.
- SQLite `decimal`→`double` conversion can hide precision bugs in money tests.

## Dependency hygiene

Direct versions observed (not upgraded; rule is no unsolicited upgrades):

- ASP.NET / OpenAPI packages 10.0.x (JwtBearer 10.0.12, OpenApi 10.0.6, EF SqlServer 10.0.9, EF Sqlite 10.0.12).
- Serilog.AspNetCore 10.0.0.
- FluentValidation 12.1.1.
- BouncyCastle.Cryptography 2.7.0.
- System.IdentityModel.Tokens.Jwt 8.23.0 (API) and 8.16.0 (reference agent).
- `System.Security.Cryptography.Pkcs` 10.0.13 pinned in Infrastructure csproj with a GHSA comment.
- Angular 21.2.x, TypeScript 5.9, Vitest 4, npm 10.9.3.

CI fails the build on High/Critical NuGet vulnerabilities and on npm high (prod) / critical (all).

## Style drift to respect, not "fix"

- Primary constructors everywhere.
- Domain exceptions for invariant failures; `Result` for application failures.
- Feature files in the portal are single-file components with inline templates.
- Arabic-first i18n dictionaries (`i18n.dictionaries.ts`) with logical CSS for RTL.
- Audit action names are dotted strings (`license.issued`), not an enum.
