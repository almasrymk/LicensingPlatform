# 16 — Dependency map

## Compile-time

```
Licensing.SharedKernel
  (no project references)

Licensing.Domain
  -> SharedKernel

Licensing.BuildingBlocks.Application
  -> SharedKernel
  packages: FluentValidation 12.1.1, Microsoft.Extensions.DependencyInjection.Abstractions 10.0.0,
            Microsoft.Extensions.Logging.Abstractions 10.0.0

Licensing.Application
  -> Domain
  packages: EF Core 10.0.9, FluentValidation 12.1.1
  framework: Microsoft.AspNetCore.App

Licensing.Modules.Catalog.Application
  -> Application (and therefore Domain and SharedKernel)
  -> BuildingBlocks
  uses ICatalogUsageReader defined in the Catalog assembly and implemented in Infrastructure

Licensing.Infrastructure
  -> Application
  -> Catalog Application
  packages: EF SqlServer 10.0.9, EF Sqlite 10.0.12, EF Design, BouncyCastle 2.7.0,
            StackExchangeRedis cache 10.0.12, Jwt 8.23.0, Pkcs 10.0.13

Licensing.Api
  -> Infrastructure
  packages: ASP.NET OpenApi 10.0.6, JwtBearer 10.0.12, Serilog.AspNetCore 10.0.0,
            Microsoft.OpenApi 2.7.5, EF Design

Licensing.Tests
  -> Api
  packages: xunit 2.9.3, Mvc.Testing 10.0.9, NetArchTest 1.3.2, coverlet 6.0.4

Licensing.ReferenceAgent
  -> none
  packages: Jwt 8.16.0
```

## Runtime service graph (portal request)

`Controller` → application service or `IDispatcher` → `IAppDbContext` (`AppDbContext`) → SQL Server.

Scoped with the request: `AppDbContext`, `HttpCurrentUser`, `TenantContext`, all application services, `IAuditLogger`, `IIdempotencyStore`, `ILicenseTokenSigner`, catalog handlers.

Singleton: `TimeProvider`, password hasher, secret hasher, product key generator, JWT service, secret store, distributed cache, `ILicenseCache`, `OutboxProcessor`, `JobRunner`.

Hosted: `OutboxDispatcher`, `ScheduledJobsService`.

## Runtime service graph (background)

Hosted service → new DI scope → `TenantContext.RunAsSystem()` → same services with filters unrestricted.

## Portal packages

`portal/package.json`: Angular 21.2 (`@angular/core`, common, compiler, forms, platform-browser, router), rxjs 7.8, tslib. Dev: Angular CLI/build 21.2.24, TypeScript 5.9, Vitest 4, jsdom 28, Prettier 3.8.

No NgRx, no component library, no Tailwind config in the files that were listed.

## Who calls whom (business)

```
AuthController -> AuthService -> Users, RefreshTokens, ApiClients, Tenants, Jwt, hashers, audit

UsersController -> UserService -> Users, Tenants, Customers

TenantsController -> TenantService -> Tenants + counts via IgnoreQueryFilters

CustomersController -> CustomerService -> Customers, Contacts

ProductsController / PlansController -> IDispatcher -> Catalog handlers -> Products, Plans, ICatalogUsageReader

SubscriptionsController -> SubscriptionService -> Subscriptions, Plans, Customers, Products, Licenses, cache

LicensesController -> LicenseService -> Licenses, Activations, Subscriptions, Plans, Products

LicensingController -> DeviceLicensingService -> Licenses, Activations, Attempts, Subscriptions, Products, Plans, Customers, Tenants, signer, cache

ReportsController -> ReportService -> most tenant-filtered sets + UsageDaily

ApiClientsController -> ApiClientService -> ApiClients, Webhooks

SigningKeysController -> ILicenseTokenSigner, audit, IAppDbContext.SaveChanges

MediaController -> MediaService -> MediaFiles + owner aggregates

OutboxDispatcher -> handlers -> LicenseService / ILicenseCache

ScheduledJobsService -> SubscriptionService, LicenseService, ReportService, AuditQueryService, AppDbContext deletes
```

## Events

Raised in domain, persisted by `AppDbContext`, dispatched by `OutboxProcessor`.

Consumed: `SubscriptionExpiredV1`, `SubscriptionCancelledV1`, `LicenseStatusChangedV1`.

Raised and not consumed: `TenantCreated`, `TenantSuspended`, `TenantResumed`, `PlanPublished`, `SubscriptionStartedV1`, `SubscriptionRenewedV1`, `SubscriptionPlanChangedV1`, `SubscriptionSuspendedV1`, `SubscriptionResumedV1`, `LicenseIssuedV1`. They still occupy outbox rows until the dispatcher marks them processed (zero handlers is success).
