# 03 — Modules

"Module" here means a business area. Only Catalog is a separate assembly. Others are namespaces that share `AppDbContext`.

## Tenants

- Domain: `src/Licensing.Domain/Tenants/Tenant.cs` — `Create`, `Rename`, `Suspend`, `Resume`, `SetImage`. Status `Active` or `Suspended`.
- Application: `TenantService` (`ListAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`, `SuspendAsync`, `ResumeAsync`).
- API: `TenantsController` `api/v1/tenants`. Permissions `tenants.read` / `tenants.manage` (PlatformAdmin only).
- Not `ITenantOwned`. Platform-wide table. Counts in the DTO use `IgnoreQueryFilters` (`TenantService` lines 32–36).

## Identity

- Domain: `User`, `RefreshToken`, `Roles`, `Permissions`.
- Application: `AuthService`, `UserService`.
- API: `AuthController`, `UsersController`.
- Users are not `ITenantOwned` because platform admins have `TenantId = null`. `UserService.Scoped()` applies the tenant predicate in code.
- Email is globally unique (`UserConfig` index on `Email`).

## Customers

- Domain: `Customer` (`ITenantOwned`), `CustomerContact` (`ITenantOwned`). Contacts cascade-delete with the customer.
- Application: `CustomerService`.
- API: `CustomersController` including contacts and `GET countries`.
- Customer users see only their customer via a dedicated filter in `AppDbContext` (lines 78–85), because `Customer` is not `ICustomerOwned` (the customer *is* the customer).

## Catalog (extracted module)

- Domain still in `Licensing.Domain.Catalog`: `Product`, `Plan`.
- Application assembly: commands/queries in `Products/Products.cs` and `Plans/Plans.cs`.
- Port: `ICatalogUsageReader` implemented by `CatalogUsageReader` so the module does not query `License` or `Subscription` types.
- API: `ProductsController`, `PlansController` dispatch through `IDispatcher`.
- Published plans are immutable (`Plan.Update` throws `PLAN_NOT_DRAFT`). New commercial terms are `Plan.NewVersion`.

## Subscriptions

- Domain: `Subscription` (`ICustomerOwned`), `SubscriptionHistory`.
- Application: `SubscriptionService`.
- API: `SubscriptionsController`.
- Lifecycle in `Subscription.AllowedActions` and ADR-013. See `04-BUSINESS-LOGIC.md`.

## Licensing

- Domain: `License`, `LicenseActivation`, `ActivationAttempt`, `SigningKey`.
- Application: `LicenseService` (portal issue/suspend/revoke/regenerate/reset) and `DeviceLicensingService` (device protocol).
- API: `LicensesController`, `LicensingController`, `SigningKeysController`.
- Signing keys are global, not per tenant. `SigningKeysManage` is only on PlatformAdmin.

## Integrations

- Domain: `ApiClient`, `Webhook`.
- Application: `ApiClientService`.
- API: `ApiClientsController` and webhook list/create on the same controller file.
- Webhook delivery is not implemented. Class comment: "Foundation only". `CreateWebhookAsync` hashes a generated secret and does not return the plaintext, and nothing reads `Webhooks` to POST events.

## Audit

- `AuditRecord` is not an aggregate and not `ITenantOwned` (`TenantId` nullable).
- `AuditLogger.Add` joins the current unit of work. `WriteNowAsync` uses a new scope so failed logins survive rollback.
- `AuditQueryService.ListAsync` filters by scope. `PurgeOlderThanAsync` deletes old rows (retention job). The table is not append-only at the database level.

## Reporting

- `UsageDaily` (`ICustomerOwned`) filled by `ReportService.AggregateUsageAsync`.
- `ReportService` also computes dashboard, overview, trend, expiring licenses, devices, failed activations on the fly.
- API: `ReportsController`.

## Media

- `MediaFile` stores image bytes in SQL (`nvarchar`/`varbinary` max). Not `ITenantOwned`.
- Upload authorization checks the owner through scoped queries (`MediaService.FindOwnerAsync`).
- `GET /api/v1/media/{id}` is anonymous. Ids are `Guid.NewGuid()` (not time-ordered v7).

## Messaging (infrastructure, not a business module)

`OutboxMessage`, `InboxMessage`, `IdempotencyRecord` in `InfrastructureEntities.cs`. Schema `messaging`.

## Building blocks

`ICommand<T>`, `IQuery<T>`, `IDispatcher`, pipeline behaviors. Used by Catalog only.
