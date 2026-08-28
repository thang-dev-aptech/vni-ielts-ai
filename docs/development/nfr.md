# Non-Functional Requirements

Requirement §12, with **MVP** and **future scale** deliberately separated. Requirement G-1 — *do not over-engineer* — means MVP targets are modest on purpose.

`[OPEN QUESTION]` M-3 — no user-volume targets were provided. MVP figures below are assumptions and should be replaced once real projections exist.

---

## Scalability

| | MVP | Future scale |
|---|---|---|
| Concurrent exam sessions | `[ASSUMPTION]` low hundreds | Thousands |
| Approach | Stateless API, scale horizontally | Same, plus read replicas |
| Workers | Scale **independently** of the API | Autoscale on queue depth |
| Database | Single MongoDB node, **configured as a replica set (`rs0`)** — see below | Multi-node replica set → PostgreSQL with replicas |
| Storage | Object storage from day one | CDN for exam assets |

**API and worker scale separately.** This is the main reason they are separate processes: a burst of Speaking submissions needs more workers, not more API instances.

> **`H-10` RESOLVED 2026-08-20 → [ADR-0011](../decisions/0011-mongodb-single-node-replica-set.md).** The "single instance" wording above is superseded: one *node*, but configured as a replica set. Original reasoning retained below. MongoDB supports multi-document transactions **only on a replica set**, and token deduction plus session creation must be atomic or a retry debits twice (threat `T22`).
>
> A **single-node replica set** costs essentially nothing — one process, one configuration flag — and makes transactions available. Recommendation: run one from development onward, *and* design token deduction as a single atomic ledger update so it does not depend on the transaction either way. → [`../database/strategy-mongodb-to-postgresql.md`](../database/strategy-mongodb-to-postgresql.md)

---

## Performance

| Operation | MVP target | Notes |
|---|---|---|
| API read (p95) | < 300 ms | |
| Answer autosave (p95) | < 200 ms | Frequent; must feel instant during an exam |
| Session start (p95) | < 1 s | Includes entitlement check and content load |
| Exam content load | < 2 s | Preload the next section during the current one |
| Submission acknowledgement | < 1 s | Returns `202`; evaluation is async |
| Reading/Listening score | Immediate | Deterministic |
| Writing evaluation (median) | < 60 s | |
| **Speaking evaluation (median)** | **< 2 min** | Upload + ASR + LLM. → [`../ai/speaking-pipeline.md`](../ai/speaking-pipeline.md) |
| Audio upload (2 min recording) | < 30 s on 4G | Resumable |

The autosave target matters more than its size suggests: a save that feels slow during a timed exam produces anxiety and duplicate submissions.

---

## Availability

| | MVP | Future scale |
|---|---|---|
| Target | `[ASSUMPTION]` 99.5% | 99.9% |
| Deployment | Rolling, brief downtime tolerated | Blue/green, zero-downtime |
| Degradation | AI outage → results pending, exams continue | Same |
| Backups | Client-side encrypted `mongodump --oplog` **plus continuous PITR via Percona Backup for MongoDB**, both restore-drilled 2026-08-28 | Scheduled runner on the chosen platform |

**The degradation rule is the important one.** An AI provider outage must not prevent learners from taking exams. Reading and Listening score without AI at all; Writing and Speaking submissions queue and drain when service returns. The learner sees an honest "evaluating" state.

**Never deploy during a scheduled exam window** once real usage exists — an in-flight session is stateful in a way a stateless API disguises.

**RPO and RTO are still a `[BUSINESS DECISION]`, but the mechanism now measures far better than the sentence that used to stand here.** The sharp edge was that a daily backup means an incident at 11am loses a 9am sitting — a result a learner spent two hours on. `mongodump --oplog` could not fix that on its own: it captures the oplog only for the *duration of the dump*, so between two daily runs there was nothing.

Continuous PITR closes that gap. Measured 2026-08-28 on the local stack: **recovery point ≤ 1 minute** of writes (PBM `oplogSpanMin=1`), and a point-in-time restore into an isolated target completed in **177 seconds**. What the business *requires* is still theirs to set; what the mechanism *delivers* is no longer a day. → [`backup-and-restore.md`](backup-and-restore.md)

---

## Security

Baseline for MVP, not deferred:

- TLS 1.2+ everywhere; HSTS
- Argon2id password hashing
- Short-lived access tokens; refresh rotation with reuse detection
- RBAC at the use-case boundary
- Rate limiting keyed on identity
- Full ZIP validation pipeline
- AI output schema validation
- Audit logging for admin actions
- Secrets from environment, never committed

→ [`../security/threat-model.md`](../security/threat-model.md)

Future: WAF, automated dependency scanning in CI, periodic penetration testing, key rotation automation.

---

## Observability

| | MVP | Future scale |
|---|---|---|
| Logging | Structured JSON, correlation IDs | Centralised aggregation |
| Metrics | Request rate, error rate, latency, queue depth | Full RED/USE dashboards |
| Tracing | Correlation ID propagation | Distributed tracing |
| Alerting | Error rate, queue backlog, **AI spend** | Full SLO alerting |

### AI-specific telemetry — from day one of Phase 7

Not optional, because these are the metrics that reveal cost regressions and quality regressions, both of which are otherwise silent:

| Metric | Reveals |
|---|---|
| Cost per evaluation, by module | The headline number |
| **Prompt cache hit rate** | The most likely silent cost regression — a prompt change that drops it to zero looks like nothing |
| Input/output token split | Whether feedback length is the problem |
| ASR minutes per evaluation | Untrimmed silence, truncated uploads |
| Evaluation latency by stage | Where the pipeline is slow |
| Validation failure rate | Prompt drift or a provider change |
| Retry and dead-letter rate | Failed evaluations cost full price and produce nothing |
| `modelVersion` distribution | Confirms pinning is working |

→ [`../ai/cost-model.md`](../ai/cost-model.md)

---

## Rate limiting

| Class | MVP |
|---|---|
| Authentication | Strict — credential stuffing |
| Registration | Strict — feeds referral fraud |
| **Submission / evaluation** | Strict — **each request costs real money** |
| In-session content reads | Generous — must not interfere with a timed exam |
| **AI Chat** | **Its own budget, separate from every class above** |
| Admin bulk operations | Separate limits |
| **Token-earning endpoints** | Strict, with a uniqueness constraint per period — feeds reward farming |

> **AI Chat must not inherit the "generous" in-session class**, and rate limiting alone does not bound its cost. A learner sending messages steadily all day stays inside any reasonable rate limit while accumulating unlimited spend — chat has no natural ceiling the way a fixed number of exam submissions does. It needs a **per-conversation and per-user budget**, enforced before the provider call. → threat `T24`, and [`../ai/cost-model.md`](../ai/cost-model.md)

`429` always carries `Retry-After`. **Never rate-limit a learner out of an in-progress exam** — that turns a defensive control into a scoring incident.

---

## API versioning

URL path versioning (`/api/v1/`). Additive changes do not bump the version; breaking changes do. **Clients must tolerate unknown enum values** — mobile apps cannot be force-updated. → [`../api/api-design-principles.md`](../api/api-design-principles.md)

---

## Error handling

Single problem-details envelope with stable machine-readable codes. All validation errors returned at once. `404` rather than `403` for resources not visible to the requester, so existence is not disclosed.

---

## Background jobs

| Property | Requirement |
|---|---|
| Idempotent | Re-running produces the same outcome |
| Retried | Exponential backoff with jitter, capped attempts |
| Dead-lettered | Never silently dropped |
| Observable | Provider, latency, usage, versions recorded |
| Drainable | Deploys wait for in-flight jobs |

Draining matters at cutover: a job started against MongoDB that completes after a Postgres switch writes to the wrong database. → [`../database/migration-plan.md`](../database/migration-plan.md)

---

## Idempotency

Required on every state-changing request. Key stored with the response for `[ASSUMPTION]` 24 hours. Same key + different body → `409`.

Mobile clients on unreliable networks retry aggressively; without this, a retried submission consumes entitlement twice and triggers a second paid evaluation.

---

## Data retention

`[ASSUMPTION]` — requires owner confirmation (M-2), and interacts with PDPL storage-limitation obligations:

| Data | Retention |
|---|---|
| Audio recordings | 90 days |
| Transcripts | 2 years |
| Evaluation records | 2 years |
| Results | Account lifetime |
| Audit logs | 2 years |
| **Chat history** | **UNCONFIRMED** → `B-6d`. Chat has no natural expiry event the way an attempt does, so a schedule has to be chosen rather than derived |
| Deleted-account data | Purged within 30 days |

Retention drives the object-storage layout: buckets and prefixes are organised **by retention class**, so deletion is a policy on a prefix rather than a scan. → [`../architecture/system-architecture.md`](../architecture/system-architecture.md) § Object storage

→ [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)

---

## Privacy

PDPL compliance is a **launch blocker**, not a hardening task:

- Explicit consent for AI processing, versioned and timestamped
- Separate consent for cross-border transfer if one occurs
- Vietnamese-language privacy notice
- Data-subject rights: access, correction, deletion, consent withdrawal
- Deletion must reach object storage and provider copies, not just the database
- CTIA filed within 60 days of first cross-border transfer
- `[OPEN QUESTION]` Parental consent for users under 18 — IELTS candidates are frequently minors

---

## What is deliberately not done at MVP

| Not done | Why |
|---|---|
| Multi-region deployment | No requirement; PDPL may constrain regions anyway |
| Zero-downtime deploys | Brief maintenance windows acceptable at MVP |
| Distributed tracing | Correlation IDs sufficient for a modular monolith |
| Autoscaling | Manual scaling adequate at hundreds of concurrent sessions |
| CDN | Add when asset delivery becomes a measured bottleneck |
| Read replicas | Add when read load is measured, not anticipated |

Each is a deliberate deferral, not an oversight — added when a measurement justifies it.
