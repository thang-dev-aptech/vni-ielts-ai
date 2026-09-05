# ADR-0011 — MongoDB runs as a single-node replica set, and token deduction needs no transaction

- **Status:** Accepted
- **Date:** 2026-08-20
- **Resolves:** `H-10`
- **Related:** [ADR-0003](0003-database-mongodb-first-postgresql-target.md) · [ADR-0004](0004-persistence-abstraction-boundary.md) · threat `T22` in [`../security/threat-model.md`](../security/threat-model.md)

## Context

Two current documents contradicted each other.

[`../development/nfr.md`](../development/nfr.md) specified a **"Single MongoDB node"** for MVP.
[`../database/strategy-mongodb-to-postgresql.md`](../database/strategy-mongodb-to-postgresql.md)
noted that MongoDB supports **multi-document transactions only on a replica set**.

Most of the product tolerates a standalone node without difficulty. A single-document update is
atomic, and the job queue, answers, and evaluations each work within one document at a time.

**Token deduction does not.** Debiting the ledger and creating an exam session are two writes that
must both happen or neither. Without atomicity an aggressive mobile retry debits twice — precisely
the failure `nfr.md` warns about under Idempotency, and threat `T22`.

The 2026-08-20 requirement freeze made this urgent rather than theoretical. `F-4` put **live token
spending in the first release**, so the debit path is a launch requirement, not a later concern.

### Why this contradiction was dangerous rather than merely untidy

On a standalone node in development, **the code works**. Nothing fails, no test goes red, and no
reviewer sees a problem. The failure appears only under retry concurrency — which mobile clients on
unreliable networks generate by design, and which a developer on a wired connection never produces.

A bug that is invisible in every environment except production is not a bug you find by being
careful. It is one you prevent structurally or ship.

## Decision

**Both remedies. They are complementary, not alternatives.**

### (a) Run a single-node replica set (`rs0`) in every environment, starting with local development

`infra/docker/compose.yaml` starts Mongo with `--replSet rs0` and initiates the set in its
healthcheck. Cost is one process and one flag — the same container, the same resource footprint.

This is the safety net for **every multi-write flow that has not been designed yet**, and those are
the ones that matter: the flows needing a transaction tend to be discovered late, by which point the
environment is already assumed.

### (b) Design token deduction as one atomic update on a single ledger document

Independently of (a), the deduction path does not rely on a transaction. `RewardLedgerEntry` is
append-only, the balance is **derived** (`Balance = sum(valid ledger transactions)`), and the write
is keyed on the same `Idempotency-Key` as the operation that triggered it — one key, one entry,
enforced by a unique index rather than by application logic.

This is the correct design for this specific case regardless of what the topology allows.

## Consequences

- **Verified, not assumed.** On the local stack: `rs.status()` reports set `rs0`, state `PRIMARY`,
  and a two-collection transaction commits. Checked on 2026-08-20 rather than inferred from the flag.
- `nfr.md`'s "single instance" wording is superseded by this ADR. One *node*, yes — but configured
  as a replica set.
- Development, staging and production behave identically with respect to transactions. There is no
  environment where a transaction silently degrades to a non-atomic write.
- A unique index on `RewardLedgerEntry(idempotencyKey)` is a **correctness** constraint, not a
  performance one. It is the double-spend defence, and it belongs in the collection definition from
  the first migration.
- Earn events carry a separate uniqueness constraint on `(userId, reason, period)` — the double-earn
  half of `T22`, which is a different failure with a different key.
- The PostgreSQL target is unaffected. Postgres has transactions unconditionally, and (b) remains
  the right design there too.

## Alternatives considered

| Option | Why not |
|---|---|
| Standalone node, add a replica set later | The migration is trivial; the *discovery* is not. By the time a flow needs a transaction, the assumption that none is available has usually spread into several call sites |
| Replica set only in production | Guarantees the class of bug that appears exclusively in production. The entire value of (a) is environment parity |
| Transaction only, no atomic ledger design | Leaves the most sensitive write in the product dependent on infrastructure configuration rather than on its own design |
| Atomic ledger design only, no replica set | Correct for the ledger, and leaves every future multi-write flow to rediscover the problem |
| A three-node replica set locally | Real cost in memory and startup time for no additional guarantee at this stage. Transactions require *a* replica set, not a quorum of distinct machines |
