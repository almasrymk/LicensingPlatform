# 04 — Business logic

Labels: **Confirmed** (read in code), **Inferred** (follows from code but not separately tested in this analysis), **Missing** (type or endpoint exists, behavior does not).

## Domains

1. Platform tenancy and staff identity.
2. Vendor catalog (products, plans, entitlements).
3. Customer subscriptions.
4. License issuance and device activation.
5. Machine API clients.
6. Operational reporting and audit.

There is no invoicing, payment capture, email delivery, or webhook delivery.

## Tenant lifecycle — Confirmed

`Tenant.Create` requires name and code (code uppercased, max 32). Optional contact email via `EmailAddress`. Status starts `Active` and raises `TenantCreated`.

`Suspend` is illegal if already suspended (`TENANT_ALREADY_SUSPENDED`). `Resume` is illegal unless suspended. Suspended tenant users cannot log in: `AuthService.TenantIsActiveAsync` returns false and login responds `AUTH_INVALID_CREDENTIALS` (same message as a bad password). Device checks treat an inactive tenant as `LIC_INVALID_LICENSE` (`DeviceLicensingService.CheckRules`), not a distinct "tenant suspended" code.

## Roles — Confirmed

`src/Licensing.Domain/Identity/Roles.cs`.

| Permission | PlatformAdmin | TenantAdmin | TenantOperator | CustomerUser |
|---|---|---|---|---|
| tenants.read / tenants.manage | yes | no | no | no |
| customers.read | yes | yes | yes | yes |
| customers.manage | yes | yes | yes | no |
| catalog.read | yes | yes | yes | yes |
| catalog.manage | yes | yes | no | no |
| subscriptions.read | yes | yes | yes | yes |
| subscriptions.manage | yes | yes | yes | no |
| licenses.read | yes | yes | yes | yes |
| licenses.manage | yes | yes | yes | no |
| apiclients.manage | yes | yes | no | no |
| users.manage | yes | yes | no | no |
| audit.read | yes | yes | no | no |
| reports.read | yes | yes | yes | yes |
| signingkeys.manage | yes | no | no | no |

Tenant staff roles can change only between `TenantAdmin` and `TenantOperator` (`User.Update`). PlatformAdmin and CustomerUser roles cannot be switched in place.

Password policy (`PasswordPolicy.Check`): at least 8 characters, at least one letter and one digit. Lockout: 5 failures, then 15 minutes (`User.MaxFailedAttempts`, `LockoutDuration`). Counter resets when lockout starts.

## Catalog rules — Confirmed

Product code uppercased, unique per tenant (`IX` on `TenantId, Code`). Platforms must be one of `Windows`, `Linux`, `macOS`, `Android`, `iOS`, `Web`. Icon is optional, max 32 ASCII letters or digits.

Plan:

- Draft can be edited. Published cannot (`PLAN_NOT_DRAFT`).
- Duration empty means lifetime; if set, at least 1 day.
- Trial days 0–90; 0 is stored as null.
- Heartbeat interval 1 hour to 30 days. Offline grace 0–365 days.
- Max activations null means unlimited (`PlanLimit`).
- Features: lowercase, start with a letter, `[a-z0-9._-]`.
- `PlanKind.Free` requires price 0 (`PLAN_FREE_PRICE`).
- `PlanKind.Trial` requires trial days (`PLAN_TRIAL_DAYS_REQUIRED`).
- `MakeCurrent` only for published trial or free plans. Caller is responsible for clearing the previous current plan (no unique index on `IsCurrent`).
- Archive clears `IsCurrent`.
- Delete is a command for drafts not in use (`DeletePlanCommand` comment). In-use check is `ICatalogUsageReader.IsPlanInUseAsync`.

Customer users listing plans see only `Published` (`ListPlansHandler`). `GetPlanAsync` does not repeat that status filter, so a customer who knows a draft id inside their tenant can read it. **Confirmed** gap, low impact (same tenant, read of unpublished commercial terms).

## Subscription state machine — Confirmed

`Subscription.Start`:

- If `trialDays > 0`: status `Trial`, `TrialEndsAt = EndDate = start + trialDays`.
- Else: status `Active`, `EndDate = start + durationDays` or null for lifetime.
- `StartSubscriptionRequest.Notes` is stored on the subscription. Device trials also store notes `device-trial:{deviceId}` and free plans `device-free:{deviceId}`. An operator who sets the same notes string could collide with that marker. **Confirmed** mechanism; collision is an edge case.

| From | Allowed |
|---|---|
| Trial | Renew, ChangePlan, Suspend, Cancel, Expire |
| Active | Renew, ChangePlan, Suspend, Cancel, Expire |
| Suspended | Resume, Cancel, Expire |
| Expired | Renew |
| Cancelled | none (terminal) |

`Renew`: extends from current end if that end is still in the future and status is not Trial; otherwise from now. Null duration clears the end (lifetime). Clears `TrialEndsAt`. Status becomes `Active`. Then `SubscriptionService.RenewAsync` calls `License.Reinstate(sub.EndDate)` for every non-revoked license.

`ChangePlan`: rejects the same plan (`SUBSCRIPTION_SAME_PLAN`). Sets `PlanId`. If `durationDays` is null, sets `EndDate` null. If `durationDays` is not null, the value is not applied. Licenses are not reinstated and entitlements are not rewritten. **Confirmed** in `Subscription.ChangePlan` and `SubscriptionService.ChangePlanAsync`. Treat the ignored duration as a defect (see `10-CODE-QUALITY-ISSUES.md`), not as a documented product rule.

`Resume`: `Trial` if `TrialEndsAt > now`, else `Active`.

`TryExpire`: only if Expire is allowed, `EndDate` is set, and `EndDate <= now`. Idempotent. Raises `SubscriptionExpiredV1`.

`Cancel` raises `SubscriptionCancelledV1`.

Optimistic check: if the client sends `ExpectedVersion` and it differs, `CONCURRENCY_CONFLICT` before the domain method. `Version` also increments inside `Record` and is an EF concurrency token, so a lost update throws `DbUpdateConcurrencyException`, mapped to the same code in `AppDbContext.SaveChangesAsync`.

Issuing a license requires status Trial or Active (`IsUsable`). Inactive customer cannot start a subscription (`CUSTOMER_INACTIVE`). Plan must be published and belong to the customer's tenant.

## License state — Confirmed

Issue copies entitlements (product code, plan code, version, features, max activations, heartbeat, offline grace) and sets `ExpiresAt` from the subscription end. Product key is returned once (`IssuedLicenseDto.ProductKey`). Stored form is HMAC of `pk:` + normalized key (`ProductKeyGenerator`). Prefix is the first 6 characters. Format: 5 groups of 6 from alphabet `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (no 0/O/1/I).

| Action | Rule |
|---|---|
| Suspend | only from Active |
| Resume | only from Suspended |
| Revoke | any status except already Revoked. Terminal for reinstate (`Reinstate` skips status change when not Expired, but revoked licenses are excluded by the renew loop) |
| TryExpire | no-op if Revoked or Expired. If `force`, expires even without a date (used when the subscription ends). Reason `subscription expired` when forced |
| RegenerateKey | not allowed if Revoked. Resets `ActiveActivations` to 0. Service deactivates active device rows and drops cache for old and new hashes |

`License.CheckUsable` is the aggregate helper. The device service uses `CheckRules` on a snapshot instead, and that order differs: revoked, suspended, then subscription expired/suspended/cancelled, then license expired. An expired license on an expired subscription returns `LIC_SUBSCRIPTION_EXPIRED`, not `LIC_EXPIRED`. **Confirmed**.

## Device protocol — Confirmed

Caller must be an API client. Tenant id comes from the token (`caller.TenantId!.Value`). A key that hashes to a license outside that tenant is invisible because of the query filter, so the response is `LIC_INVALID_LICENSE`.

Rules in `CheckRules`: tenant active, customer active, optional product code match, then license/subscription status as above. Device id: 8–128 chars, ASCII letter, digit, `-`, `_`, `:`, `.`.

Activate:

- Same device already active: success, `AlreadyActivated=true`, does not consume another slot.
- Otherwise `TryReserveActivationSlotAsync`: single SQL update `ActiveActivations + 1` where `MaxActivations` is null or `ActiveActivations < MaxActivations`.
- Unique `(LicenseId, DeviceId)`: on conflict the slot is released and the winner's activation is returned.
- Idempotency-Key header (max 100) on `POST /licensing/activate` only. Scope is `activate:{tenantId}:{actorId}`. Replay header `Idempotent-Replayed`.

Validate uses cache 60 seconds (`CacheTtl`) keyed by tenant and key hash. Heartbeat bypasses the cache read, updates `LastHeartbeatAt`, and still refreshes the cache on miss. Cache failures are not specially caught in the service; `ILicenseCache` is documented in ADR-012 as fail-open. Implementation is `DistributedLicenseCache` in `InfraServices.cs` (read during this analysis at the type level; ADR text matches the interface comment).

Signed token fields (`LicenseTokenClaims`): license number and id, tenant, product, plan, device, features, license expiry, `CheckAfter`, `OfflineValidUntil`.

`CheckAfter = now + heartbeat hours + jitter` (jitter is `0 .. heartbeatHours * 6` minutes). `OfflineValidUntil = now + heartbeat hours + offline grace days`, capped by `ExpiresAt`.

Deactivate sets the activation row to `Deactivated` and then decrements the slot. Cache removed.

Trial (`StartTrialAsync`):

- Needs an active product and either the current published trial plan or, if none, the first published plan with `TrialDays > 0` and kind not Free, ordered by code.
- One trial per device per product, detected by subscription notes `device-trial:{deviceId}`. No unique index.
- Customer named `Trial devices` is created per tenant if missing. Inactive trial customer yields `LIC_TRIAL_UNAVAILABLE`.
- License max activations forced to 1. Key returned once.

Free (`StartFreeAsync`): current published free plan only (`IsCurrent`). Marker `device-free:{deviceId}`. Customer `Free plan devices`. `LIC_FREE_UNAVAILABLE` / `LIC_FREE_USED`.

## Reporting formulas — Confirmed

`ReportService.OverviewAsync` comment: revenue is the sum of **current plan prices** of subscriptions in status Active. Trial subscriptions count as running subscriptions but not as revenue. This is booked contract value, not collected cash. Currency is the mode of plan currencies, default `USD`.

KPI change is percent versus the same population created on or before 30 days ago. If the previous value is 0 and the current is not, change is null.

"Online" device: activation active and `LastHeartbeatAt` within `2 * HeartbeatIntervalHours` (`LicenseService.GetAsync`, `ListActivationsAsync`).

`Attention.SuspiciousActivations` is the count of failed `ActivationAttempt` rows in the last 7 days (customer-filtered for customer users). It is not a separate fraud model.

`AggregateUsageAsync` recomputes today's `UsageDaily` per customer that has at least one active license. Customers with only failed attempts and no active license get no row. **Confirmed**.

## Permissions vs UI — Confirmed

Portal `permissionGuard` only hides routes (`http.ts` comment: the API enforces the same permission). Server policies require claim `typ_lic=user` and claim `perm`.

`TenantResolution.ResolveWriteTenant`: non-platform users always write their own `TenantId`. A body `tenantId` is ignored for them. Platform admins must pass `tenantId` or have `X-Tenant-Id` set, else `TENANT_REQUIRED`.

## Missing behavior

- Webhook HTTP delivery, retries, and signature verification material returned to the tenant.
- Payments, invoices, taxes beyond a free-text `Customer.TaxNumber`.
- Email notifications (password reset is an admin `reset-password`, not a self-service mail flow).
- Per-tenant signing keys.
- Soft delete. Records are deactivated, suspended, revoked, or hard-deleted (draft plans, old refresh tokens, old idempotency rows, old audit, replaced images).
