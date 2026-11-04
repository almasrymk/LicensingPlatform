# 14 — Development roadmap

This is a maintenance order for future tasks, not a commitment to build any of it. The current request was analysis only.

Priority uses: business impact, dependency on other work, security, risk of doing it wrong, and how wide the change is.

## P1 — Correctness before new features

1. Decide and fix subscription plan change (`ChangePlan` duration and whether existing licenses follow). This touches domain, `SubscriptionService`, portal subscription UI, and domain tests. High regression risk around renew, which already reinstates licenses.
2. Make device trial and free issuance race-safe (unique constraint or equivalent). Touches licensing schema. Needs a migration, so it requires an explicit approval under the project rules.
3. Align README with `Seed:DemoData` so local setup is not misleading. Docs only.

## P2 — Security and operations

1. Document or implement shared rate limits if more than one API instance is expected. Redis is already a configuration key; the limiter does not use it.
2. Confirm production secret injection (JWT, pepper, data-protection ring, signing-key folder) using `docs/operations.md`. No code until the host is known.
3. Revisit anonymous media only if logos stop being the only content.

## P3 — Finish or hide incomplete contracts

1. Webhooks: either implement delivery (new hosted service, secret returned once, inbox-style retries) or stop advertising create as if events are sent. Product decision first. Touches integrations, outbox, portal integrations page.
2. Remove or enforce `licenses.issue` and `licenses.read`. Contract change if scopes disappear from OpenAPI examples. Snapshot test will fail until updated.

## P4 — Modular monolith continuation (ADR-015)

Only when a task asks to extract the next module. Order suggested by coupling:

1. Keep Catalog on the shared context until a real second module needs a port.
2. Next extraction candidates: Licensing (largest rule set) or Identity. Subscriptions depend on Catalog and are depended on by Licensing, so they should not move first.
3. Each extraction keeps the OpenAPI snapshot unchanged.

## P5 — Product gaps that are not bugs

Billing, email, self-service password reset, per-tenant signing keys, notification persistence. Each is a new business rule. Do not infer them from the revenue chart.

## Explicit non-goals unless requested

- Replacing services with MediatR or with the internal dispatcher across the whole solution.
- Adding foreign keys to every Guid column in one migration.
- Upgrading Angular to 22 or bumping NuGet packages for freshness.
- Splitting the host into API and worker deployments.
