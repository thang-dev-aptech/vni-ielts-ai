# MongoDB → PostgreSQL Migration Runbook

**Not yet scheduled, and deliberately not started.** No PostgreSQL adapter exists, and no dual-write is enabled — verified 2026-08-28 (`F3.2`): no `Npgsql` or EntityFrameworkCore package reference anywhere in `backend/`, and no provider-selection flag. Every mention of PostgreSQL in the source today is a comment explaining why something was shaped the way it was.

This document exists so that decisions taken now do not quietly make the migration harder, and so whoever eventually runs it inherits a plan rather than a guess.

Strategy and rationale — *why* MongoDB first and PostgreSQL later — live in [`strategy-mongodb-to-postgresql.md`](strategy-mongodb-to-postgresql.md) and [ADR-0003](../decisions/0003-database-mongodb-first-postgresql-target.md). This is the runbook: what to do, in what order, and what breaks.

> **Rewritten 2026-08-28 against the code that now exists.** The previous version was written before the persistence layer was built and its schema-mapping table named entities that were never implemented — `Evaluation`, `AiJob`, `RewardLedgerEntry`, `AuditEvent` — while omitting six collections that do exist, including the two hardest to migrate. That is the failure mode [`docs/README.md`](../README.md) warns about: an architecture document is not evidence of implementation. The inventory below was read out of `MongoContext` and the index definitions, not recalled.

---

## Preconditions

Do not start until all are true:

| # | Precondition | Why |
|---|---|---|
| 1 | Requirements frozen | Migrating a moving model means migrating twice |
| 2 | Domain model stable for a meaningful period | The signal that churn has actually stopped |
| 3 | Architecture tests passing — no persistence types outside `Infrastructure` | The whole plan depends on this boundary holding |
| 4 | A persistence contract suite exists for **every** port | The only practical way to verify behavioural equivalence |
| 5 | Production data volume still modest | Cost scales with data; migrate early |

Preconditions 3 and 4 are the ones that silently fail, so both are now automated rather than inspected:

- **3** → `PersistenceBoundaryTests` (Domain/Application reference no driver) and `PersistenceRepresentationTests` (the shapes crossing the boundary stay portable).
- **4** → `UserRepositoryContract` is the pattern. Today it covers `IUserRepository` only; see [The rule for every new aggregate](#the-rule-for-every-new-aggregate).

---

## What is actually stored

Eighteen collections, read from `MongoContext` and `AuditLog` — twelve on 2026-08-28, plus `content_sources` added on 2026-08-29 with the content rights registry (`FS0.1`), plus `canonical_explanations` and `personalized_explanations` with the R/L explanation pipeline (`FS5`), plus `learner_goals`, `learner_activity_days` and `coaching_advice` added on 2026-09-04 with goal coaching and the study streak. The right-hand column is the target shape, and the notes column is where the work actually is.

| Collection | Target in PostgreSQL | What makes it non-trivial |
|---|---|---|
| `users` | Table | Unique index `ux_users_email` → `UNIQUE` constraint. The catch site translating a duplicate into `DuplicateEmailException` must translate Postgres' `23505` as well as Mongo's duplicate-key category |
| `user_identities` | Table, FK to `users` | Unique `ux_identities_provider_subject` on (provider, subject) → composite `UNIQUE` |
| `roles` | Table | Unique `ux_roles_name` |
| `refresh_tokens` | Table, FK to `users` | **TTL index `ttl_refresh_expiry`** — see below. Unique `ux_refresh_token_hash` |
| `exam_versions` | Table + `jsonb` for section/question trees | The deep nested content tree is genuinely variable in shape; `ScoringProfile` and timing stay `jsonb`, versioned |
| `exam_sessions` | Table, FK to `users` and `exam_versions` | `SectionAttempt` children → child table. The transition compare-and-swap needs a Postgres equivalent (conditional `UPDATE … WHERE status = $expected`) |
| `answer_sheets` | Table + `jsonb` answers map | **Two layers and a CAS** — see below |
| `section_results` | Table, FK to `exam_sessions` | Band values are `decimal` → `numeric`, never `double precision` |
| `section_markings` | Table + `jsonb` for criterion detail | Band values `numeric` |
| `marking_jobs` | Table | **A lease queue** — see below |
| `idempotency_keys` | Table | **TTL index `ttl_idempotency`** (24h) — see below |
| `content_sources` | Table + child table for files | The rights record per source: owner, licence proof, allowed environments, expiry, reviewer, and the paths and SHA-256 of its files. `allowedEnvironments`, `examVersionIds` and `examDefinitionIds` are Mongo arrays with multikey indexes — in Postgres either `text[]` with GIN, or three small join tables, and the join tables are the better fit because a grant will eventually want its own audit trail. Environment names are stored as **strings**, never ordinals: an ordinal shift would turn a fixture-only record into a publication right silently |
| `canonical_explanations` | Table, FK to `exam_versions` (+ question id) | Authored/cacheable R/L explanation text keyed by exam version and question. Provider metadata is stamped on the row; the explanation must never be able to change a band (`A-11`). Shape of evidence spans is variable → `jsonb` for evidence only |
| `personalized_explanations` | Table, FK to `exam_sessions` | Per-sitting explanation jobs. Lookup index `ix_personalized_explanations_lookup` on `(sessionId, questionId, answerHash)` — Postgres needs the same composite unique or unique-ish constraint so a retry with the same answer hash does not double-call a provider. Operation id + answer-hash cache is the idempotency seam |
| `learner_goals` | Table, FK to `users` (one row per learner) | `_id` is the user id and the write is a replace-upsert; in Postgres a `PRIMARY KEY (user_id)` and `INSERT … ON CONFLICT DO UPDATE`. `target_band numeric(2,1)` |
| `learner_activity_days` | Table, FK to `users` | `_id = {userId}:{yyyy-MM-dd}` with an upsert that `$inc`s a counter and `$addToSet`s the kind — the day is computed in `Learning:TimeZone`, so the stored `day` is a local calendar date, not a UTC timestamp. Postgres: `PRIMARY KEY (user_id, day)`, `ON CONFLICT DO UPDATE SET count = count + 1, kinds = array_union`. Index `ix_activity_user_day` |
| `coaching_advice` | Table (a cache) | **TTL index `ttl_coaching_advice`, 7 days** on `createdAt`; `_id` is a hash of the standing (target + four bands), not a learner id, so rows are shared and carry no personal data. Postgres needs a sweep for the TTL |
| `audit_log` | Table, append-only, no `UPDATE` grant | Append-only is a grant, not a convention |

Use `jsonb` where the shape is genuinely variable. Do not use it to avoid designing: that reproduces MongoDB inside PostgreSQL and forfeits the reason for moving.

### The three that will bite

**1. TTL indexes have no PostgreSQL equivalent.** Two collections rely on the server deleting rows on a clock:

| Index | Collection | Rule |
|---|---|---|
| `ttl_idempotency` | `idempotency_keys` | `ExpireAfter = 24h` after `createdAt` |
| `ttl_refresh_expiry` | `refresh_tokens` | `ExpireAfter = 0` — deleted at the `ExpiresAt` value |

PostgreSQL has no such index. Something must sweep — `pg_cron`, an application background job, or partition-drop. **If this is forgotten, nothing errors**: `idempotency_keys` grows without bound, and expired refresh tokens stop being purged. The second one is security-relevant, not merely housekeeping, so the sweep ships *with* the cutover, not after it.

**2. `answer_sheets` carries two layers and an optimistic-concurrency counter.** Answers written before 2026-08-27 live in an `answers` **array**; everything since lives in a **map** keyed by question id, read *under* the array. A migration that reads only one layer silently loses a learner's work. Alongside them, `Revision` (and `ClosedRevision`) is an `int` advanced with `$inc` and used for compare-and-swap — Postgres needs the same guarantee, either an explicit revision column with a conditional `UPDATE` or `SERIALIZABLE`; a plain `UPDATE` re-introduces the lost-update race the counter exists to prevent. Note this counter lives only in the persistence layer: no domain entity has a version field.

**3. `marking_jobs` is a lease queue, not a table of rows.** Claiming a job is a single atomic find-and-update filtering on `NextAttemptAt <= now` and (`LeaseUntil` null or in the past), setting `LeaseToken`/`LeaseUntil` and incrementing `Attempts`. The Postgres equivalent is `UPDATE … WHERE … RETURNING` (optionally `FOR UPDATE SKIP LOCKED`). If the atomicity is lost, two workers process one job — and a job is a paid provider call.

---

## Phases

### 1 — Schema design

Derive the relational schema from the *frozen domain model*, not from the Mongo documents. A schema reverse-engineered from documents inherits denormalisation that was only ever a document-store accommodation.

### 2 — Parallel implementation

Add a PostgreSQL implementation of every repository port **alongside** the Mongo one. Both compile; selection is by configuration.

`Domain`, `Application`, `Api` and `Worker` do not change. If they need to, the boundary was already broken and that is the bug to fix first.

### 3 — Equivalence testing

Run each port's persistence contract suite against **both** implementations. This is what `UserRepositoryContract` is for: the PostgreSQL suite is a subclass supplying the implementation, and it inherits every assertion unchanged. A behaviour that differs is then a named failing test rather than a discovery in production.

Semantics that differ quietly, and are therefore worth an explicit test:

| Behaviour | MongoDB | PostgreSQL |
|---|---|---|
| Case sensitivity in comparisons | Case-sensitive by default | Collation-dependent |
| Sort order for mixed types | Type-ordering rules | Type-strict |
| Null vs missing field | Distinguishable | `NULL` only |
| Decimal | `Decimal128` | `numeric` |
| Empty array vs absent | Distinguishable | `NULL` vs `'{}'` |
| Duplicate key | `MongoWriteException`, category `DuplicateKey` | `SQLSTATE 23505` |

The null-vs-missing row causes the most surprises: a field that is *absent* and one that is *explicitly null* are different in Mongo and identical in Postgres. Any code branching on that distinction breaks.

### 4 — Backfill

A batched utility: read from Mongo, map to **domain entities**, write through the PostgreSQL repositories. Going through the domain rather than a document-to-row ETL means every record is validated by the same invariants the application enforces.

Requirements:

- **Idempotent and resumable.** It will be interrupted at least once.
- **Dry-run mode** that reports without writing.
- **Logs every skipped or coerced record.** Silent data loss here is the worst possible outcome.
- **Reads both answer-sheet layers** (array *and* map).
- Records a high-water mark per collection so the incremental pass at cutover only has to carry the tail.

### 5 — Validation

Backfill is not complete because it finished; it is complete because it was checked.

| Check | Granularity |
|---|---|
| Row/document count | Per collection |
| Checksum over a stable projection of each record | Per collection |
| Referential integrity — every FK resolves | Per relationship |
| Spot-check of the invariants themselves | A sample per aggregate: a sitting's answers, a result's band, an audit chain |

Band values get an **exact equality** check, not a tolerance: they are `decimal` on both sides precisely so no rounding is acceptable.

### 6 — Interim dual-write, only if a maintenance window is refused

`[ASSUMPTION]` A short maintenance window is acceptable, and [Cutover](#7--cutover) assumes it. **Prefer it.** A window is minutes of planned unavailability; the alternative below is a distributed-consistency problem that runs for days.

If the business refuses a window, the shape is:

1. **Dual-write, Mongo authoritative.** Every write goes to both; reads still come from Mongo. A PostgreSQL write that fails is logged and queued for repair — it must **not** fail the request, or a migration mechanism becomes an outage.
2. **Backfill runs underneath** the dual-write, oldest-first, skipping records the dual-write already placed.
3. **Reconciliation runs continuously** until the divergence rate reaches zero and stays there (see below).
4. **Flip reads** to PostgreSQL while keeping dual-write on. This is the reversible step — reads flip back instantly.
5. **Stop writing to Mongo** only after a full window with no divergence and no read errors.

Two things must be true of the dual-write or it will do more harm than the window it avoided:

- **It is not a distributed transaction.** Writes land in two stores; they will diverge. Reconciliation is the mechanism that makes that acceptable, so dual-write without reconciliation is not an option, it is data loss on a delay.
- **Non-idempotent operations need care.** `marking_jobs` in particular must be dual-written on *state*, never dual-claimed — two stores each handing the same job to a worker is a doubled provider bill.

`[BUSINESS DECISION]` Whether a maintenance window is acceptable determines which of the two cutover paths is taken. It is the product owner's call and is not assumed here.

### 7 — Reconciliation

Required in the dual-write path, and worth running once in the window path as a final gate.

A sweep that walks both stores and, per record, compares the same stable projection used in validation:

| Divergence | Resolution while Mongo is authoritative |
|---|---|
| Present in Mongo, absent in PostgreSQL | Re-write from Mongo |
| Present in both, fields differ | Mongo wins; log the field and the record |
| Absent in Mongo, present in PostgreSQL | Investigate — this is a bug in the dual-write, not a repairable record |
| TTL-expiring collections | Compare only unexpired records; a purged row is not divergence |

Reconciliation reports a **rate**, not a count, and the go/no-go for flipping reads is that rate being zero across a full period — not the absence of a report.

### 8 — Cutover

```
1. Enable maintenance mode
2. Drain the marking queue — no job holding a lease, no in-flight evaluation
3. Final incremental backfill from the high-water mark
4. Run validation; run reconciliation once
5. Flip configuration to PostgreSQL
6. Confirm the TTL replacement sweep is scheduled and has run once
7. Smoke test: sign in, start a sitting, autosave, submit, mark, view result
8. Disable maintenance mode
9. Keep MongoDB running, read-only, for the rollback window
```

Step 2 is not optional: a marking job claimed against Mongo that completes after the flip writes its result to the wrong database, and the learner sees a dash forever. The worker's shutdown window (`Worker:ShutdownTimeoutSeconds`, default 150s) is what makes draining bounded — stop accepting claims, let the held job finish.

Step 6 exists because the TTL loss is the one failure with no symptom on cutover day.

### 9 — Decommission

Keep MongoDB read-only for an agreed rollback window — `[ASSUMPTION]` two weeks. Remove the Mongo implementation only after the window closes without incident.

---

## Rollback

| Point of failure | Action |
|---|---|
| Contract suites differ between providers | Do not cut over. Fix and repeat |
| Backfill fails mid-run | Resume — the utility is idempotent |
| Validation mismatch | Do not cut over. The mismatch is the finding |
| Reconciliation rate not zero (dual-write path) | Do not flip reads |
| Post-cutover failure, dual-write path | Flip reads back to Mongo. Mongo is still authoritative and still being written, so this is genuinely reversible |
| Post-cutover failure, window path, inside the window | Flip configuration back to Mongo. Anything written to PostgreSQL after cutover must be reconciled by hand — which is why the window is short |
| Post-cutover failure after the window | Forward-fix only |

---

## The rule for every new aggregate

Any aggregate added from now on ships three things together, in the same change:

1. its persistence document and mapping;
2. a **persistence contract suite** for its port, written provider-agnostically — an `abstract` class with the implementation as a hole, following `UserRepositoryContract`;
3. a row in [What is actually stored](#what-is-actually-stored), including anything that makes it non-trivial: a TTL, a unique index, a compare-and-swap, a lease.

The reason is the shape of this document's own history. The previous version drifted from the code because nothing forced it to move when the code did, and a migration plan that is wrong about what is stored is worse than none: it sends whoever runs it looking for tables that were never built while omitting the two that need a sweep job.

---

## Things that would make this migration hard

Each is currently prevented, and the first three are enforced by tests rather than by discipline. If any appears, treat it as a defect against [ADR-0004](../decisions/0004-persistence-abstraction-boundary.md):

- Mongo driver types (`ObjectId`, `BsonDocument`) outside `Infrastructure` → `PersistenceBoundaryTests`
- `[Bson*]` attributes on domain entities → `PersistenceBoundaryTests`
- An enum stored as its ordinal, a band stored as `double`, an id that is not a string → `PersistenceRepresentationTests`
- Aggregation pipelines expressing business logic
- `Application` code branching on storage behaviour
- Raw documents returned from repositories instead of domain entities
- Relying on Mongo's absent-vs-null distinction for meaning
