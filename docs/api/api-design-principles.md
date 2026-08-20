# API Design Principles

REST over HTTPS, JSON. Serves four first-party clients (learner web, Android, iOS, Admin CMS), so it optimises for evolvability and correctness over generality.

---

## Versioning

**URL path versioning:** `/api/v1/...`

Chosen over header-based negotiation because it is visible in logs, trivially routable at the proxy, and unambiguous when debugging a mobile client whose version you cannot easily inspect.

### The evolution rule

> **Additive changes do not bump the version. Breaking changes do.**

| Change | Breaking? |
|---|---|
| Add an optional request field | No |
| Add a response field | No |
| Add an endpoint | No |
| Add an enum value | **Yes, for clients** — see below |
| Remove or rename a field | Yes |
| Change a field's type or meaning | Yes |
| Make an optional field required | Yes |
| Tighten validation | Yes |

Enum additions deserve care: mobile clients cannot be force-updated. **Clients must tolerate unknown enum values** rather than failing — a documented client requirement, not merely a server concern. This matters concretely here: adding a question type or an evaluation status must not break an app installed six months ago.

Deprecation: mark in OpenAPI, respond with a `Deprecation` header, and support at least one full mobile release cycle before removal.

---

## Resources

```
POST   /api/v1/auth/register
POST   /api/v1/auth/login
POST   /api/v1/auth/social
POST   /api/v1/auth/refresh
POST   /api/v1/auth/logout

GET    /api/v1/exams
GET    /api/v1/exams/{examId}

POST   /api/v1/exams/{examId}/sessions
GET    /api/v1/sessions/{sessionId}
PUT    /api/v1/sessions/{sessionId}/answers/{questionId}
POST   /api/v1/sessions/{sessionId}/recordings
POST   /api/v1/sessions/{sessionId}/submit

GET    /api/v1/results
GET    /api/v1/results/{resultId}
POST   /api/v1/results/{resultId}/appeal

GET    /api/v1/me
GET    /api/v1/me/entitlement
GET    /api/v1/me/referral-code

POST   /api/v1/admin/packages
GET    /api/v1/admin/packages/{packageId}
POST   /api/v1/admin/exams/{examId}/versions/{version}/publish
GET    /api/v1/admin/evaluations
POST   /api/v1/admin/evaluations/{evaluationId}/rerun
```

Sessions are addressed at the root (`/sessions/{id}`) rather than nested under the exam, because a client holding a session ID should not need to remember which exam produced it.

---

## Server time in every response

Every response includes the server's current time:

```http
X-Server-Time: 2026-08-17T09:14:22.481Z
```

This is not a convenience — it is what makes the client timer correctable without a dedicated time endpoint. The client reconciles its display clock against this on every response. → [ADR-0007](../decisions/0007-server-authoritative-exam-timer.md)

---

## Idempotency

**Required on every state-changing request** (`POST`, `PUT`, `PATCH`, `DELETE`):

```http
Idempotency-Key: 9f2b4c1e-...
```

The server stores the key with the response for a retention window (`[ASSUMPTION]` 24 hours) and returns the original response on replay.

This matters more than usual here because mobile clients on unreliable networks retry aggressively. Without it, a retried submission could consume entitlement twice, create a duplicate session, or trigger a second paid AI evaluation. → [T8](../security/threat-model.md)

Replay with the **same key but a different body** is a conflict, not a replay:

```http
409 Conflict — IDEMPOTENCY_KEY_REUSED
```

---

## Errors

A single problem-details envelope for every error:

```jsonc
{
  "type": "https://api.vni-ielts.example/errors/session-expired",
  "title": "Session expired",
  "status": 409,
  "code": "SESSION_EXPIRED",
  "detail": "The submission deadline for this session passed at 2026-08-17T09:00:00Z.",
  "instance": "/api/v1/sessions/abc123/submit",
  "traceId": "0af7651916cd43dd8448eb211c80319c",
  "errors": [
    { "path": "/answers/q-12", "code": "ANSWER_TOO_LONG", "message": "Exceeds the 2-word limit." }
  ]
}
```

| Field | Purpose |
|---|---|
| `code` | **Stable, machine-readable.** Clients branch on this, never on `title` or `detail` |
| `detail` | Human-readable, safe to display |
| `traceId` | Correlates to server logs for support |
| `errors[]` | Per-field detail for validation failures |

**Return all validation errors at once**, not the first. A form that reveals one error per submission is a poor experience and, on a timed exam, an actively harmful one.

### Status codes

| Code | Used for |
|---|---|
| 200 / 201 / 202 | Success. **202 for submission** — AI evaluation is asynchronous |
| 400 | Malformed request |
| 401 | Missing or invalid authentication |
| 403 | Authenticated but not permitted |
| 404 | Not found, **or not visible to this user** — never distinguish the two |
| 409 | State conflict: session expired, idempotency reuse, stale revision |
| 413 | Payload too large |
| 422 | Semantically invalid |
| 429 | Rate limited — always with `Retry-After` |
| 500 | Server error. Never leak internals |

The 404 rule prevents enumeration: a request for another user's session returns 404, not 403, so an attacker learns nothing about existence. → [T19](../security/threat-model.md)

---

## Authentication

`Authorization: Bearer <access token>`. Short-lived access tokens; rotating refresh tokens with reuse detection.

Refresh-token reuse revokes the entire token family — the standard defence against a stolen refresh token, and the reason rotation without reuse detection is not enough.

Mobile clients are **public clients**: OIDC authorization-code flow with PKCE, no client secret. → [`../architecture/key-flows.md`](../architecture/key-flows.md)

---

## Authorisation

RBAC with `resource.action` permissions, checked at the **use-case boundary** rather than in controllers — so the same rule applies whether a use case is invoked by the API or by the worker.

Permissions are seeded data, not an enum, so adding one does not require a deployment. Requirement C-13 states the example keys are not final.

---

## Pagination

Cursor-based, not offset:

```
GET /api/v1/results?limit=20&cursor=eyJpZCI6...
```

```jsonc
{ "items": [ /* … */ ], "nextCursor": "eyJpZCI6…", "hasMore": true }
```

Cursors are stable under concurrent inserts; offsets are not — a learner scrolling their score history while a new result lands would otherwise see a duplicate or skip an entry.

Cap `limit` server-side regardless of what is requested.

---

## Rate limiting

Keyed primarily on **authenticated identity**, with IP as a secondary signal. IP alone is both shared (mobile carrier NAT) and rotatable, so it is a poor primary key.

| Class | Rationale |
|---|---|
| Authentication endpoints | Strictest — credential stuffing |
| Registration | Strict — automated account creation feeds referral fraud |
| **Submission / evaluation** | Strict — **each request costs real AI spend** |
| Content reads during a session | Generous — must not interfere with a timed exam |
| Admin bulk operations | Separate limits |

`429` always carries `Retry-After`. Never rate-limit a learner out of an in-progress exam — that converts a defensive control into a scoring incident.

---

## Long-running operations

Submission returns `202` with partial results; AI evaluation completes asynchronously.

```jsonc
{
  "resultId": "res_123",
  "status": "partial",
  "sections": {
    "reading":   { "status": "scored", "band": 7.0 },
    "listening": { "status": "scored", "band": 6.5 },
    "writing":   { "status": "evaluating" },
    "speaking":  { "status": "queued" }
  }
}
```

Clients poll `GET /results/{id}` or receive a push notification. `[ASSUMPTION]` Polling with backoff for MVP; push added later.

Each section carries its **own** status, so one failed module does not present as a failed result.

---

## File upload

Audio uses resumable, chunked upload to object storage via short-lived signed URLs — never proxied through the API. Completion is confirmed to the API with a checksum.

Signed URLs are scoped to a single object and expire quickly. Object keys are **server-generated**; a client-supplied filename is never used as a storage key. → [`../security/threat-model.md`](../security/threat-model.md)

---

## Documentation

OpenAPI 3.1, generated from the code so it cannot drift from the implementation. The client `packages/api-client` is generated from that spec, which keeps the four clients type-safe against a single source.
