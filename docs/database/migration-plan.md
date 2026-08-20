# MongoDB → PostgreSQL Migration Plan

**Not yet scheduled.** Executed after requirement freeze. This document exists now so Phase 1 decisions do not quietly make the migration harder.

Requirement D-6 forbids assuming a PostgreSQL schema before requirements stabilise, so this describes the *process*, not the schema.

---

## Preconditions

Do not start until all are true:

| # | Precondition | Why |
|---|---|---|
| 1 | Requirements frozen (Phase 2 complete) | Migrating a moving model means migrating twice |
| 2 | Domain model stable for a meaningful period | The signal that churn has actually stopped |
| 3 | Architecture tests passing — no persistence types outside `Infrastructure` | The whole plan depends on this boundary holding |
| 4 | Integration test suite covers every repository | The only practical way to verify behavioural equivalence |
| 5 | Production data volume still modest | Cost scales with data; migrate early |

Precondition 3 is the one that silently fails. Verify it with an automated dependency test, not by inspection.

---

## Phases

### 1 — Schema design

Derive the relational schema from the *frozen* domain model, not from the Mongo documents. A schema reverse-engineered from documents inherits denormalisation that was only ever a document-store accommodation.

Guidance:

| Data | Shape in Postgres |
|---|---|
| Users, roles, permissions, exams, sessions, answers, results | Normalised tables with foreign keys |
| `Evaluation.rawOutput` | `jsonb` — provider-shaped, genuinely variable |
| `AiJob.featureSnapshot` | `jsonb` |
| `ScoringProfile` / `TimingProfile` | `jsonb`, versioned |
| `RewardLedgerEntry` | Table, append-only, no `UPDATE` grant |
| `AuditEvent` | Table with `jsonb` before/after |

Use `jsonb` where the shape is genuinely variable. Do not use it to avoid designing — that reproduces MongoDB inside Postgres and forfeits the reason for moving.

### 2 — Parallel implementation

Add a Postgres implementation of every repository interface **alongside** the Mongo one. Both compile; selection is by configuration.

`Domain`, `Application`, `Api`, and `Worker` do not change. If they need to change, the boundary was already broken and that must be fixed first.

### 3 — Equivalence testing

Run the full integration suite against both implementations. Every repository test must pass identically.

Watch specifically for semantics that differ quietly:

| Behaviour | Mongo | Postgres |
|---|---|---|
| Case sensitivity in comparisons | Default case-sensitive | Collation-dependent |
| Sort order for mixed types | Type-ordering rules | Type-strict |
| Null vs missing field | Distinguishable | `NULL` only |
| Decimal handling | `Decimal128` | `numeric` |
| Empty array vs absent | Distinguishable | Usually `NULL` vs `'{}'` |

The null-vs-missing row causes the most surprises: a document where a field is *absent* and one where it is *explicitly null* are different in Mongo and identical in Postgres. Any code branching on that distinction breaks.

### 4 — Data migration

Batched migration utility: read from Mongo, map to domain entities, write via the Postgres repositories. Going through the domain layer — rather than a direct document-to-row ETL — means every record is validated by the same invariants the application enforces.

Requirements:

- **Idempotent and resumable.** It will be interrupted at least once.
- **Verifies counts and checksums per entity type.**
- **Logs every skipped or coerced record.** Silent data loss during migration is the worst possible outcome.
- **Dry-run mode** that reports without writing.

### 5 — Cutover

`[ASSUMPTION]` A short maintenance window is acceptable. If it is not, this becomes a substantially harder dual-write project and should be re-planned.

```
1. Enable maintenance mode
2. Drain the job queue (no in-flight AI evaluations)
3. Final incremental data migration
4. Verify counts and spot-check records
5. Flip configuration to Postgres
6. Smoke test: login, start exam, submit, evaluate, view result
7. Disable maintenance mode
8. Keep Mongo running, read-only, for the rollback window
```

Step 2 matters: an AI job that starts against Mongo and completes after cutover writes to the wrong database.

### 6 — Decommission

Keep MongoDB read-only for an agreed rollback window (`[ASSUMPTION]` two weeks). Remove the Mongo implementation only after the window closes without incident.

---

## Rollback

| Point of failure | Action |
|---|---|
| Equivalence tests fail | Do not cut over. Fix and repeat |
| Data migration fails mid-run | Resume — the utility is idempotent |
| Post-cutover failure inside the window | Flip configuration back to Mongo. Any data written to Postgres after cutover must be reconciled manually — this is why the window is short |
| Post-cutover failure after the window | Forward-fix only |

---

## Things that would make this migration hard

Each is currently prevented by design. If any appears in the codebase, treat it as a defect against [ADR-0004](../decisions/0004-persistence-abstraction-boundary.md):

- Mongo driver types (`ObjectId`, `BsonDocument`) outside `Infrastructure`
- `[Bson*]` attributes on domain entities
- Aggregation pipelines expressing business logic
- `Application` code branching on storage behaviour
- Raw documents returned from repositories instead of domain entities
- Relying on Mongo's absent-vs-null distinction for meaning
