# ADR-0003 — MongoDB for Phase 1, PostgreSQL as the production target

- **Status:** Accepted
- **Date:** 2026-08-17
- **Deciders:** Product owner
- **Related:** Requirements D-1…D-6 · [ADR-0004](0004-persistence-abstraction-boundary.md) · [`../database/strategy-mongodb-to-postgresql.md`](../database/strategy-mongodb-to-postgresql.md)

## Context

Stated by the owner as requirements D-1 and D-2. This ADR records the reasoning and the conditions that make it sound.

Four things are genuinely unsettled, and each would force a relational schema migration:

| Unsettled | Schema impact |
|---|---|
| Exam structure (E-10, H-1) | Academic vs General Training; module combinations; Speaking as one session or three |
| AI evaluation shape | Criterion sets, feature payloads, provider response shapes — **and the provider itself is undecided** |
| Subscription/referral rules (B-4) | No entitlement model exists |
| UI/UX | Not designed; UI regularly surfaces missing fields |

Both databases are installed locally (MongoDB 7.0.26, PostgreSQL 16.11).

## Options considered

| Option | For | Against |
|---|---|---|
| **MongoDB → PostgreSQL** | Absorbs schema churn without a migration per discovery; commits to relational integrity once the model stabilises | One planned migration; two persistence implementations written over the project's life |
| PostgreSQL from the start | No migration; integrity from day one | A migration per domain discovery during the most volatile phase. Requirement D-6 explicitly forbids assuming a Postgres schema now |
| MongoDB permanently | No migration at all | Gives up referential integrity and cheap analytics precisely when score reporting and cohort analysis become valuable |
| PostgreSQL with `jsonb` throughout | Single database, flexible | Reproduces MongoDB inside Postgres and forfeits the reason for choosing either |

## Decision

**Use MongoDB for Phase 1. Migrate to PostgreSQL after requirement freeze**, before significant production data accumulates.

## Consequences

### Positive
- The domain model can change during Phases 1–3 without migration overhead — which is exactly when it will change most.
- PostgreSQL is adopted when its strengths (integrity, analytics, schema-as-documentation) start to matter.
- `jsonb` handles the genuinely variable parts (`Evaluation.rawOutput`, `AiJob.featureSnapshot`, scoring profiles) so the migration does not force over-normalisation.

### Negative
- One migration project must be executed.
- Two persistence implementations exist during the transition.
- No referential integrity during Phase 1 — application code must maintain consistency.

### Risks accepted
- `[TECHNICAL RISK]` [R9](../requirements/risks-and-dependencies.md) — migrating too early means migrating twice; too late means migrating production data. **Timing is the risk**, and it is managed by gating on requirement freeze.
- Mongo-specific idioms could leak into application logic. Prevented by [ADR-0004](0004-persistence-abstraction-boundary.md) and enforced by automated architecture tests.

## Notes

This is not "NoSQL because it scales". It is deferring schema commitment while the schema is genuinely unknown — and the deferral is time-boxed by an explicit trigger.

The strategy is coherent because MongoDB's weaknesses bite *later* than its strengths help. The failure mode is not choosing MongoDB; it is never leaving.
