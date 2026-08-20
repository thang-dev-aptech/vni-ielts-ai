# Database Strategy — MongoDB Now, PostgreSQL Later

## The decision

| Phase | Database | Trigger to move |
|---|---|---|
| Phase 1 — now | **MongoDB 7** | — |
| Phase 2 — target | **PostgreSQL 16+** | Requirement freeze (UI/UX finalised, functional requirements settled) |

Stated by the owner as requirements D-1 and D-2. This document records *why* it is sound and what makes it work.

---

## Why MongoDB first is the right call here

This is not "NoSQL because it scales." It is a deliberate choice to defer schema commitment while the domain is still moving.

Four things are genuinely unsettled, and each would force a schema migration:

| Unsettled | Why it changes the schema |
|---|---|
| Exam structure (E-10, H-1) | Academic vs General Training, module combinations, whether Speaking is one session or three submissions |
| AI evaluation shape | Criterion sets, feature payloads, and provider response shapes are all undecided — the provider itself is undecided |
| Subscription/referral rules (B-4) | No entitlement model exists at all |
| UI/UX | Not designed yet, and UI regularly surfaces missing fields |

Writing a normalised relational schema now means writing migrations for every one of those discoveries. MongoDB's flexible documents absorb that churn without a migration per change.

### The honest trade-off

MongoDB gives up things this product will eventually want:

- Referential integrity between sessions, exams, and evaluations
- Cheap ad-hoc analytical queries (score distributions, cohort trends)
- Schema as documentation
- Straightforward multi-entity transactional guarantees

Those matter more once the model stabilises — which is exactly when the migration happens. The strategy is coherent because the weaknesses bite *later* than the strengths help.

---

## Why PostgreSQL as the target

| Need | Postgres fit |
|---|---|
| Referential integrity across sessions/exams/evaluations | Native foreign keys |
| Reporting and analytics on scores | Mature SQL, window functions |
| Append-only reward ledger with correctness guarantees | Transactional integrity |
| Semi-structured AI output that still resists over-normalising | `jsonb` — keeps the flexibility where it is genuinely needed |
| Operational maturity | Well understood; 16.11 already installed locally |

`jsonb` is what makes the migration comfortable: `Evaluation.rawOutput`, `AiJob.featureSnapshot`, and `ScoringProfile` tables stay JSON columns rather than being forced into relational shape. Only the entities that need integrity get normalised.

---

## What makes the migration tractable

**One rule, enforced by tests:**

> Repository interfaces live in `Application`. Persistence models and mapping live in `Infrastructure`. Domain entities carry no persistence attributes.

The blast radius of switching databases is then a single project:

```
Vni.Ielts.Domain/          unchanged
Vni.Ielts.Application/     unchanged
Vni.Ielts.Api/             unchanged
Vni.Ielts.Worker/          unchanged
Vni.Ielts.Infrastructure/  ← rewritten
```

→ [ADR-0004](../decisions/0004-persistence-abstraction-boundary.md) · [`../architecture/backend-architecture.md`](../architecture/backend-architecture.md)

### What is *not* built to enable this

Requirement D-5 warns against premature Clean Architecture, and it is worth being explicit about what that rules out:

| Rejected | Why |
|---|---|
| Generic `IRepository<T>` | Leaks storage semantics into `Application` and produces awkward queries in both databases |
| Shared Unit-of-Work abstraction | Mongo and Postgres transaction semantics differ enough that a common abstraction leaks. Keep transactions inside `Infrastructure` |
| Database-agnostic query language | Reinventing an ORM. Write the queries twice — once, at migration time |
| Dual-write / dual-read layer | Enormous complexity for a one-time cutover |
| Abstracting *now* for a schema nobody has designed | Requirement D-6 forbids assuming a Postgres schema before requirements stabilise |

The migration is a **rewrite of one project, executed once**. It is not an ongoing abstraction tax paid on every feature.

---

## MongoDB usage rules for Phase 1

Following these keeps the eventual migration mechanical rather than archaeological:

1. **No cross-document joins in application logic.** `$lookup` in a repository is a relational query wearing a disguise — it will not survive the move cleanly.
2. **Reference across modules by ID only.** Never embed a `User` inside an `ExamSession`.
3. **Embed only what is owned and bounded.** `CriterionScore` inside `Evaluation` is fine — there are four, and they never exist alone. `Answer` inside `ExamSession` is **not** — answers are written independently and frequently.
4. **Use explicit typed IDs** (`ExamSessionId`), not raw `ObjectId`, in the domain. `ObjectId` is a Mongo concept and must not leak.
5. **Store money-like and score-like values as decimals**, never as floating point. Band scores are half-steps; float drift is unnecessary risk.
6. **Timestamps are UTC `DateTimeOffset`.** Exam deadlines cross timezones and the server is the authority.
7. **Design documents that could be tables.** If a document would need to be shredded into five tables later, reconsider it now.

---

## Applying those rules — `PROPOSED` document shapes

Rule 3 ("embed only what is owned and bounded") decides most of these. The 2026-08-20 entities are included.

| Entity | Shape | Which rule decides it |
|---|---|---|
| `ExamVersion` + sections, parts, questions, answer keys | **Embed** | Read as one unit at session start, immutable after publish, bounded by exam length. Assets are **references** to object storage, never embedded binary — that is what keeps the document small |
| `ExamSession` + `SectionAttempt` | **Embed** | Bounded at four. They share a lifecycle and are always read together |
| `Answer` | **Separate collection** | Rule 3 names this case explicitly: written independently and frequently, updated by `revision` |
| `Evaluation`, `Result` | **Separate collections** | Different lifecycles and different trust levels — an `Evaluation` can be superseded while its `Result` stands |
| `RewardLedgerEntry` | **Separate collection**, append-only. Index `(userId, occurredAt)` | Unbounded growth per user. Balance is derived, never stored as a counter |
| `ChatMessage` | **Separate collection**. Index `(conversationId, createdAt)` | A long conversation is exactly the unbounded-array shape that breaks. Never embed messages in the conversation |
| `DictationAttempt` | **Separate collection** | Unbounded per learner, same reasoning as `Answer` |
| Idempotency keys | **Separate collection** + TTL index at 24h | Self-expiring, matching the `nfr.md` assumption. A TTL index does this without a cleanup job |

### A contradiction to resolve before Phase 4 — `H-10`

> [`../development/nfr.md`](../development/nfr.md) specifies a **"Single MongoDB instance"** for MVP.
> MongoDB supports **multi-document transactions only on a replica set.**

Most of this product tolerates that fine — a single-document update is atomic, and the queue, answers, and evaluations all work within one document at a time.

**Token deduction does not.** Debiting the ledger and starting a session are two writes that must both happen or neither. Without atomicity, an aggressive mobile retry debits twice — the precise failure `nfr.md` warns about under Idempotency, and threat `T22` in [`../security/threat-model.md`](../security/threat-model.md).

Two remedies, and they are complementary rather than alternatives:

| | Approach | Cost |
|---|---|---|
| **(a)** | Run a **single-node replica set** (`rs0`) from development onward, so transactions are available | Near zero — same process, one configuration flag |
| **(b)** | Design deduction as **one atomic update on a single ledger document**, requiring no transaction at all | Design effort, no infrastructure |

**Recommendation: both.** (b) is the correct design for the specific case and should be built that way regardless. (a) is the safety net for every other multi-write flow that has not been designed yet — and the flows that need it tend to be discovered late.

---

## Migration timing

The move happens **after** requirement freeze and **before** significant production data accumulates. Migrating a large production dataset is a different, much harder project than migrating a small one.

If launch precedes the freeze, the migration cost rises sharply — this is a real scheduling risk worth naming. → [`../requirements/risks-and-dependencies.md`](../requirements/risks-and-dependencies.md#r9)

Execution detail: [`migration-plan.md`](migration-plan.md)
