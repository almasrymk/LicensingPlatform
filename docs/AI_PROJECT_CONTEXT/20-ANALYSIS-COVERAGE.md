# 20 — Analysis coverage

Date: 2026-11-03 (Africa/Cairo). Workspace file count excluding `.git` and `node_modules`: **172** before this knowledge base, plus the files in `docs/AI_PROJECT_CONTEXT/`.

No database connection, `dotnet test`, or `ng test` was run. Findings are from source, migrations, and existing docs.

## Inventory

| Area | Files (approx.) | Coverage |
|---|---|---|
| Solution, props, gitignore, gitleaks, README | 6 | Read |
| GitHub workflows | 2 | Read |
| Existing docs (`docs/adr`, `docs/operations`) | 2 | Read |
| SharedKernel `*.cs` | 5 | Read in full |
| Domain `*.cs` | 13 | Read in full |
| Application `*.cs` | 16 | Services that own rules read in full or through the method that implements the rule. See partial list |
| Infrastructure `*.cs` | 22 including 9 migration files | DI, DbContext, configurations, jobs, outbox, crypto, JWT, current user, initializer header, snapshot relationships. Designer files not read line by line |
| API `*.cs` plus appsettings and csproj | 10 cs + json | `Program.cs`, auth, licensing controller, media, middleware, authorization, rate limits, results read in full. Admin and business controllers covered by route and attribute search plus signing-key action |
| BuildingBlocks | 4 cs | Read in full |
| Catalog module | 6 cs | DI, reads, plan query handlers read. Product command bodies and plan command bodies after line 80 sampled via signatures and domain rules |
| Tests | 10 cs + openapi json + k6 | Architecture test read. Other test files identified by name and attribute count, not every assertion |
| Reference agent | 1 cs + csproj | Purpose and package read. Command switch not fully traced |
| Portal | 37 ts, 2 scss, html, package.json, proxy | Routes, auth, http interceptors, package.json, notifications page read. Other feature templates not opened one by one. Dictionaries (721 lines) not read |
| Images / favicon | 6 | Not analyzed (binary assets) |

C# distribution: SharedKernel 5, Domain 13, Application 16, Infrastructure 22, Api 10, BuildingBlocks 4, Catalog 6, Tests 10, Reference agent 1.

## Analyzed (enough to implement against)

- Host startup, middleware order, DI registrations.
- Tenant filters, write guard, outbox write path.
- Role and permission matrix.
- Auth, refresh, client credentials.
- Subscription and license state machines.
- Device activate / validate / heartbeat / deactivate / trial / free.
- Report formulas for revenue, online devices, and "suspicious" counts.
- Endpoint table and which attribute guards each route.
- EF table map, the two real foreign keys, and the absence of the others.
- Job and outbox lifecycles.
- Portal route map and where the bearer token and `X-Tenant-Id` are attached.

## Partially analyzed

- `LicenseTokenSigner` algorithm details beyond ADR-004 and the public-key endpoint.
- `DistributedLicenseCache` and `FileSecretStore` bodies in `InfraServices.cs` (interfaces and DI confirmed; branch-level error handling not quoted).
- `DbInitializer.SeedDemoAsync` body (constants and gate conditions read; row-by-row seed not transcribed).
- `CustomerService`, `TenantService`, `UserService` after create, `ApiClientService` mutate helpers.
- Catalog product update/publish handlers (domain methods they call were read).
- Portal feature components other than routes, shell existence, notifications, and core services.
- Individual xUnit assertions.
- `tests/Licensing.Tests/Contracts/openapi.v1.json` contents.
- `tools/Licensing.ReferenceAgent/Program.cs` command implementation.

## Not analyzed

- Pixel content of `portal/public`.
- `portal/package-lock.json` tree.
- Historical git commits beyond the working tree.
- Runtime behavior on SQL Server (migrations apply, concurrency under load, backup).
- Whether CI is currently green on the default branch.

## Excluded on purpose

- `.git/`
- `node_modules/` (not installed in this workspace)
- `bin/` and `obj/` (not present)
- EF `*.Designer.cs` migration files, except the relationship section of `AppDbContextModelSnapshot.cs`
- This knowledge base as an input to itself

## Coverage statement

This is a **full pass of the backend business rules and host**, and a **structural pass of the portal**. It is not a line-by-line reading of every test, every Angular template, or the license signer. Before changing a partially analyzed file, read that file again. The rules in `15-CODING-CONVENTIONS.md` say to re-check `docs/AI_PROJECT_CONTEXT` against the code at the start of the next task.

## Remaining work for a later analysis session

1. Read `LicenseTokenSigner` and `FileSecretStore` line by line and append key-rotation edge cases if they differ from ADR-004.
2. Read `SeedDemoAsync` if demo data behavior becomes a task.
3. Skim each portal feature for API calls that do not match `06-API-ENDPOINTS.md`.
4. Run `dotnet test` and record the real count in `17` and `18`.
5. Diff `openapi.v1.json` paths against the controller table.
