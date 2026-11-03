# 08 — Integrations

## External systems that exist in code

| Integration | Status | Where |
|---|---|---|
| SQL Server | Required system of record | `AddInfrastructure` |
| Redis distributed cache | Optional. Empty connection string selects in-memory `IDistributedCache` | `DependencyInjection.cs` |
| HTTP API clients (customer software) | Implemented. Client-credentials JWT then licensing routes | `AuthService.ClientTokenAsync`, `LicensingController` |
| Reference agent | Dev tool that calls the API and verifies offline tokens | `tools/Licensing.ReferenceAgent/Program.cs` |
| Webhooks | Persistence and create/list only. No sender | `Webhook`, `ApiClientService.CreateWebhookAsync` |
| Email / SMS | Not present | — |
| Payment provider | Not present. Revenue is plan price | `ReportService.OverviewAsync` |
| Key Vault | Mentioned in ADR-004 and `docs/operations.md` as the production replacement for `ISecretStore`. No Key Vault package in csproj | — |
| OpenTelemetry | Not referenced | — |

## API client scopes

`ApiScopes`: `licenses.activate`, `licenses.validate`, `licenses.issue`, `licenses.read`.

Only activate and validate are enforced on endpoints. Issue and read are accepted into `ScopesValue` and copied onto the client JWT, and nothing checks them.

Secret is shown once at create and rotate (`ApiClientSecretDto` in `ApiClientService`). Stored as HMAC. Rotation refused when status is Revoked.

## License signing

`EcdsaLicenseTokenSigner` (`src/Licensing.Infrastructure/Security/LicenseTokenSigner.cs`, read for algorithm and rotation responsibilities; ADR-004 states ES256 / P-256).

`SigningKey` statuses: Active (signs), Retiring (verifies, does not sign), Retired.

`POST /api/v1/signing-keys/rotate` creates a new active key and begins retirement of the previous one (`ILicenseTokenSigner.RotateAsync`). Audit action `signing_key.rotated`.

`GET /api/v1/signing-keys` is anonymous so installed software can refresh verification keys without a portal user. Response cached 300 seconds.

Private key material is not a column. It is in the file secret store protected by ASP.NET Data Protection.

## Outbox as an internal integration

Domain events serialized to `messaging.OutboxMessages` in the same `SaveChanges` as the aggregate (`AppDbContext.WriteDomainEventsToOutbox`). Payload is `JsonSerializer.Serialize(domainEvent, domainEvent.GetType())`. Type name is the CLR name (`SubscriptionExpiredV1`, not an assembly-qualified name).

Handlers registered in `Licensing.Application/DependencyInjection.cs`:

| Event | Handler | Effect |
|---|---|---|
| `SubscriptionExpiredV1` | `ExpireLicensesOnSubscriptionExpired` | `LicenseService.ExpireForSubscriptionAsync` |
| `SubscriptionCancelledV1` | same class | same |
| `LicenseStatusChangedV1` | `InvalidateLicenseCache` | cache remove |

Events with no handler still sit in the outbox. `OutboxProcessor` throws on an unknown CLR type name and retries until dead-letter (`MaxAttempts` default 8). Known types with zero handlers complete successfully (handler count 0).

Tenant events (`TenantCreated`, `TenantSuspended`, `TenantResumed`) and plan/license/subscription events other than the three above have no handlers. **Confirmed** by the DI registrations. Cache invalidation for license status is also done synchronously in `LicenseService.MutateAsync`, so the outbox handler is a second path.

## Idempotency

`EfIdempotencyStore` stores status code and body for activate retries. Retention default 48 hours (`JobsOptions.IdempotencyRetentionHours`).

## Reference agent

Project `tools/Licensing.ReferenceAgent`, assembly name `license-agent`. README commands: `activate`, `check` (offline verify with public key), `heartbeat`, `run --minutes`, `deactivate`. Package `System.IdentityModel.Tokens.Jwt` 8.16.0 (older than the API's 8.23.0). It does not reference the platform projects.

## Load script

`tests/load/validate.js` is k6. Not executed by `dotnet test`. Documented in `docs/operations.md`.

## CORS

Default policy: origins from `Cors:Origins` or `http://localhost:4200`. Any header, any method, exposed header `X-Correlation-Id`.
