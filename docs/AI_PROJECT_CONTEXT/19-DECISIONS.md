# 19 — Decisions

## Already recorded by the previous team

`docs/adr/README.md` is the authority for ADR-001 through ADR-015. This knowledge base does not restate them as new decisions. If implementation and an ADR disagree, the code is the runtime truth and the ADR is the intended design. Known alignments:

- ADR-003 key format matches `ProductKeyGenerator` (5×6).
- ADR-009 roles match `Permissions.ByRole`.
- ADR-010 lockout and refresh rotation match `User` and `AuthService`.
- ADR-011 device routes match `LicensingController`.
- ADR-013 subscription and license states match the aggregates, with the `ChangePlan` duration gap noted as a defect against the method signature, not against a sentence in the ADR (the ADR says ChangePlan is allowed from Trial/Active; it does not specify end-date math).
- ADR-015 Catalog extraction matches the module layout.

## Decisions made by this analysis

### D-A1 — Documentation only

The initial engagement is analysis. No refactor, bugfix, or migration was applied. Future fixes need a separate request.

### D-A2 — Evidence labels

Business behavior is tagged Confirmed, Inferred, or Missing inside `04-BUSINESS-LOGIC.md`. Untested runtime claims stay out of the "completed" column unless the code path is fully wired.

### D-A3 — Do not add foreign keys from this document

`05-DATABASE.md` lists logical relationships. They are not a migration plan. Adding them changes delete behavior and can fail on existing data.

### D-A4 — Keep service-style modules

New work in Tenants, Identity, Customers, Subscriptions, Licensing, Integrations, Reports, and Media extends the existing service. The dispatcher is for modules under `src/Modules`.

### D-A5 — OpenAPI snapshot is a compatibility gate

Public route or schema changes are breaking for the contract test even when old clients might still work. Call that out before editing controllers.

## Open decisions (not taken)

| Topic | Why it is open |
|---|---|
| Should plan change rewrite license end dates and entitlements? | Code is inconsistent. Product owner input needed |
| Ship webhook delivery or remove the endpoints? | Foundation comment versus a public API |
| One API instance or many? | Decides whether rate limits and outbox claiming are urgent |
| Is anonymous image GET still acceptable? | Current design is deliberate |
