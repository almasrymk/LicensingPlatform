# 01 — Solution structure

## Top level

```
LicensingPlatform.slnx
Directory.Build.props          net10.0, nullable enable, implicit usings
.gitignore
.gitleaks.toml
.github/workflows/ci.yml
.github/workflows/codeql.yml
README.md
docs/adr/README.md
docs/operations.md
docs/AI_PROJECT_CONTEXT/       this knowledge base
portal/                        Angular 21 standalone app
src/
  Licensing.SharedKernel/
  Licensing.Domain/
  Licensing.Application/
  Licensing.Infrastructure/
  Licensing.Api/
  BuildingBlocks/Licensing.BuildingBlocks.Application/
  Modules/Catalog/Licensing.Modules.Catalog.Application/
tests/Licensing.Tests/
tests/load/validate.js         k6 script, not part of dotnet test
tools/Licensing.ReferenceAgent/
```

`Directory.Build.props` sets `TargetFramework` to `net10.0` for every SDK project.

## Project references (confirmed in csproj)

```
Licensing.Api
  -> Licensing.Infrastructure
       -> Licensing.Application
            -> Licensing.Domain
                 -> Licensing.SharedKernel
       -> Licensing.Modules.Catalog.Application
            -> Licensing.Application (via its own csproj; Catalog uses IAppDbContext and domain Catalog types)
            -> Licensing.BuildingBlocks.Application
                 -> Licensing.SharedKernel
Licensing.Tests -> Licensing.Api
Licensing.ReferenceAgent -> no project reference (standalone console, JWT package only)
```

Catalog project file: `src/Modules/Catalog/Licensing.Modules.Catalog.Application/Licensing.Modules.Catalog.Application.csproj` (reference inferred from `using Licensing.Application` and `Licensing.Domain.Catalog` in `Plans/Plans.cs`; architecture tests forbid Catalog from referencing Infrastructure or Api).

## Domain folders (`src/Licensing.Domain`)

| Folder | Types |
|---|---|
| Tenants | `Tenant`, `TenantStatus`, domain events |
| Identity | `User`, `RefreshToken`, `Roles`, `Permissions` |
| Customers | `Customer`, `CustomerContact` |
| Catalog | `Product`, `Plan`, `PlanKind`, `PlanStatus`, `Entitlements` |
| Subscriptions | `Subscription`, `SubscriptionHistory`, subscription events |
| Licensing | `License`, `LicenseActivation`, `ActivationAttempt`, `SigningKey`, `LicenseErrors` |
| Integrations | `ApiClient`, `Webhook`, `ApiScopes` |
| Audit | `AuditRecord` |
| Reporting | `UsageDaily` |
| Media | `MediaFile` |
| Common | `Money`, `DateRange`, `EmailAddress`, `PlanLimit`, `FeatureCode` |

## Application folders (`src/Licensing.Application`)

Services are concrete classes registered in `DependencyInjection.AddApplication`, not MediatR handlers: `AuthService`, `UserService`, `TenantService`, `CustomerService`, `SubscriptionService`, `LicenseService`, `DeviceLicensingService`, `ApiClientService`, `AuditQueryService`, `ReportService`, `MediaService`. Cross-cutting: `Abstractions/Abstractions.cs`, `Common/Common.cs`, `Validation.cs`, `Events/Handlers.cs`.

## Catalog module

`src/Modules/Catalog/Licensing.Modules.Catalog.Application/`

- `DependencyInjection.cs` — `AddCatalogModule` registers handlers and validators.
- `Products/Products.cs`, `Plans/Plans.cs` — `ICommand` / `IQuery` records and `internal` handlers.
- `CatalogReads.cs`, `Dtos.cs`, `Validation.cs`.

ADR-015: Catalog application was extracted first. Domain types and `IAppDbContext` are still shared. There is no `Licensing.Modules.Catalog.Domain` or Catalog `DbContext`.

## API

`src/Licensing.Api/Program.cs` is the only web entry point (`public partial class Program` for `WebApplicationFactory`).

Controllers: `AuthController`, `AdminControllers` (tenants, customers, users, audit), `BusinessControllers` (products, plans, subscriptions, licenses, API clients, webhooks, reports, signing keys), `LicensingController`, `MediaController`.

Infrastructure helpers: `ApiResults`, `Authorization`, `Middleware`, `RateLimits`.

## Infrastructure

`Persistence/` — `AppDbContext`, `Configurations`, `InfrastructureEntities`, `DbInitializer`, `Migrations/`.

`Security/` — password hash, HMAC pepper, product keys, JWT, ECDSA license signer.

`Jobs/ScheduledJobs.cs`, `Messaging/OutboxDispatcher.cs`, `Identity/CurrentUser.cs`, `Services/InfraServices.cs`, `ModuleAdapters/CatalogUsageReader.cs`.

## Portal

Angular standalone, signals, no UI kit (ADR-007).

- `portal/src/main.ts` bootstraps `app.config.ts`.
- Routes: `portal/src/app/app.routes.ts`.
- Core: `api.service.ts`, `api.models.ts`, `auth.service.ts`, `http.ts`, `i18n.*`, `toast.service.ts`.
- Features: one component file per screen under `portal/src/app/features/`.
- Layout: `layout/shell.ts`. Shared: `ui.ts`, `charts.ts`, `icon.ts`, `media.ts`.
- Dev proxy: `portal/proxy.conf.json` (README: `/api` to the API).

## Namespaces

Match folder names: `Licensing.Domain.*`, `Licensing.Application.*`, `Licensing.Infrastructure.*`, `Licensing.Api.*`, `Licensing.Modules.Catalog.Application`, `Licensing.BuildingBlocks.Application`, `Licensing.SharedKernel`.

Database schemas (not C# namespaces): `tenants`, `identity`, `customers`, `catalog`, `subscriptions`, `licensing`, `integrations`, `audit`, `reporting`, `media`, `messaging`. See `Configurations.cs`.
