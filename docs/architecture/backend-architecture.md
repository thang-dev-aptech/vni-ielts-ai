# Backend Architecture

**.NET 10 / ASP.NET Core** ([ADR-0001](../decisions/0001-backend-dotnet10-aspnetcore.md)). Modular monolith, pragmatic layering.

## The calibration this document exists to make

Requirement D-3 asks that the MongoDB→PostgreSQL migration be *manageable*. Requirement D-5 warns against *prematurely creating an overly complex Clean Architecture implementation*. These pull in opposite directions, and getting the balance wrong is the most likely architectural failure on this project.

The resolution: **one strict boundary, not five.**

> Repository interfaces live in `Application`. Persistence models and mapping live in `Infrastructure`. Domain entities carry no persistence attributes.

That single rule is what makes the migration tractable. Everything else — MediatR, generic repositories, specification patterns, a full CQRS split, one interface per use case — is optional and should be skipped unless a concrete problem demands it.

---

## Project layout

```
src/
├── Vni.Ielts.Domain/          entities, value objects, IELTS rules. No dependencies.
├── Vni.Ielts.Application/     use cases, port interfaces, DTOs. Depends on Domain.
├── Vni.Ielts.Infrastructure/  Mongo repos, AI adapters, storage, identity. Depends on Application.
├── Vni.Ielts.Api/             controllers, auth, DI wiring. Depends on Application + Infrastructure.
└── Vni.Ielts.Worker/          background job host. Depends on Application + Infrastructure.

tests/
├── Vni.Ielts.Domain.Tests/          fast, no I/O
├── Vni.Ielts.Application.Tests/     use cases with fake ports
└── Vni.Ielts.Integration.Tests/     real Mongo via Testcontainers
```

Dependencies point inward only. `Domain` references nothing.

### Enforcing it

Add an architecture test (NetArchTest or similar) that fails the build when `Domain` or `Application` references a persistence or vendor namespace. A rule nobody checks is a rule that decays — and this is the one rule the migration depends on.

---

## Module slicing

Within each project, organise by **feature**, not by technical type:

```
Vni.Ielts.Application/
├── Identity/
├── Exams/
├── Sessions/
├── Evaluation/
├── Cms/
└── Rewards/
```

Not `Services/`, `Handlers/`, `Interfaces/`. Feature folders keep related code together and make a future extraction a directory move rather than an archaeology exercise.

---

## The persistence boundary in practice

```csharp
// Domain — no persistence attributes, no driver types
public sealed class ExamSession
{
    public ExamSessionId Id { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset DeadlineAt { get; private set; }

    public bool IsWithinDeadline(DateTimeOffset serverNow) => serverNow <= DeadlineAt;
}

// Application — the port
public interface IExamSessionRepository
{
    Task<ExamSession?> GetAsync(ExamSessionId id, CancellationToken ct);
    Task SaveAsync(ExamSession session, CancellationToken ct);
}

// Infrastructure — persistence model + mapping, isolated here
internal sealed class ExamSessionDocument
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("startedAt")] public DateTime StartedAt { get; set; }
}
```

Replacing MongoDB with PostgreSQL means rewriting `Infrastructure/Persistence/`. `Domain`, `Application`, `Api`, and `Worker` do not change. That is the whole point.

### What is explicitly *not* required

| Pattern | Verdict |
|---|---|
| Generic `IRepository<T>` | **No.** Leaks storage semantics and produces awkward query methods |
| Specification pattern | **No.** Not needed at this scale |
| Unit of Work abstraction | **No.** Mongo transactions and Postgres transactions differ enough that a shared abstraction leaks. Handle transactions inside `Infrastructure` |
| MediatR for every call | **Optional.** Use it where it earns its place; do not mandate it |
| One interface per use case | **No.** Group by feature |

---

## Cross-cutting

### Authorisation

RBAC with `resource.action` permission keys, checked at the **use-case boundary** rather than in controllers — so the same rule applies whether a use case is invoked by the API or the worker.

```
exam.read  exam.create  exam.update  exam.delete  exam.publish
user.read  user.suspend  role.assign
evaluation.read  evaluation.rerun  evaluation.override
package.upload  package.import
```

Requirement C-13 states these examples are **not final**. Permissions are seeded data, not an enum, so adding one does not require a deployment.

### AI ports

Domain and Application know only ports. Vendor SDKs appear solely in `Infrastructure/Ai/`, and none exists yet.

```csharp
public interface ISpeechRecognizer
{
    Task<TranscriptResult> TranscribeAsync(AudioReference audio, CancellationToken ct);
}

public interface IWritingEvaluator
{
    Task<EvaluationOutput> EvaluateAsync(WritingEvaluationRequest request, CancellationToken ct);
}
```

`TranscriptResult` must expose **word-level timings**, because the deterministic fluency features depend on them. A provider without word timestamps is not a viable choice. → [`../ai/speaking-pipeline.md`](../ai/speaking-pipeline.md)

### Library choices — `PROPOSED`, 2026-08-20

The layering above is settled. The concrete libraries were not named anywhere, which is a real gap: "short-lived tokens with rotating refresh and Argon2id hashing" describes a requirement, not an implementation.

| Concern | Proposal | Why this, not something else |
|---|---|---|
| **Authentication** | ASP.NET Core's built-in JWT bearer handler + the OIDC handlers for Google and Facebook | Every client is first-party. The product consumes identity providers; it does not *become* one |
| | **Defer Duende IdentityServer** | It solves being an identity provider for third parties — a problem this product does not have. It adds an instance to operate, and has a commercial licence threshold that would need checking first (`V-9`) |
| **Request validation** | FluentValidation | The error envelope requires **all** validation errors returned at once. DataAnnotations expresses that awkwardly, and the envelope is already specified in [`../api/api-design-principles.md`](../api/api-design-principles.md) |
| **Logging** | Serilog, structured JSON | [`../development/nfr.md`](../development/nfr.md) requires structured logs with correlation IDs from MVP |
| **Metrics and tracing** | OpenTelemetry | Vendor-neutral, so the monitoring backend can be chosen later without rewriting instrumentation — which matters because `B-11` may constrain where telemetry can be sent |
| **Media probe** | Out-of-process, sandboxed | Media parsing is a classic memory-safety surface, and the input is untrusted by definition (`I-13`). `[NEEDS VALIDATION]` whether a managed alternative covers the needed formats |

None of these is implemented. They are recommendations awaiting the requirement freeze.

### Configuration and secrets

Environment-based with strongly-typed options. **No AI credentials may be added until the owner selects a provider** ([CLAUDE.md](../../CLAUDE.md) rule 6) — and a PreToolUse hook blocks writes to `.env*` files to enforce this mechanically.

### Background jobs

Worker host, separate process. Every job is:

- **Idempotent** — re-running produces the same outcome
- **Retried with exponential backoff and jitter**, with a capped attempt count
- **Dead-lettered** on final failure, never silently dropped
- **Observable** — records provider, latency, usage, `modelVersion`, `rubricVersion`

#### Queue technology — `PROPOSED`: MongoDB-backed at MVP

`[OPEN QUESTION]` **H-9.** Previously recorded only as "undecided"; here is the reasoning that should inform the decision.

At the MVP target of a few hundred concurrent sessions ([`../development/nfr.md`](../development/nfr.md)), the job volume is **tens per minute** — Writing evaluations, plus Speaking if `M-26` keeps it. That does not justify operating a separate broker with its own availability, backup, and monitoring story.

A MongoDB-backed queue satisfies every property required above: an atomic find-and-modify gives at-least-once delivery, the attempt count and dead-letter state live on the job document, and the queue is backed up with everything else.

**Upgrade trigger, stated so it is not a judgement call later:** when *measured* queue depth or job latency exceeds the `nfr.md` targets. Not when someone predicts it will.

**Deferred, not rejected:** Kafka · RabbitMQ · Redis Streams. Each is a reasonable choice at a volume this product has not reached.

> One caveat that interacts with `H-10`: a MongoDB-backed queue is comfortable with a single instance, but **token deduction is not**. Multi-document transactions require a replica set. → [`../database/strategy-mongodb-to-postgresql.md`](../database/strategy-mongodb-to-postgresql.md)

---

## Testing strategy

| Layer | Approach |
|---|---|
| Domain | Pure unit tests. **The band-rounding rule gets its own table-driven test** covering the `.25` and `.75` cases explicitly — naive rounding gets `.75` wrong |
| Application | Use cases against in-memory fake ports |
| Infrastructure | Integration tests against real MongoDB via Testcontainers |
| API | Endpoint tests via `WebApplicationFactory` |
| Architecture | Automated dependency-direction rules |

**Never assert on live AI provider output.** AI adapters are tested against recorded fixtures; the *validation* layer is tested against deliberately malformed and adversarial payloads — including prompt-injection attempts that try to return an out-of-scale band. → [`../security/ai-security.md`](../security/ai-security.md)

---

## Why .NET 10

| Factor | Detail |
|---|---|
| Support | LTS until **2028-11-14**. .NET 8 (LTS) and .NET 9 (STS) both reach EOL **2026-11-10** — starting on either would mean migrating within months |
| Already present | SDK 10.0.100 installed on the development machine |
| Ecosystem | Official MongoDB driver, Npgsql for the Postgres target, mature OIDC libraries for Google/Facebook |
| Tooling | `csharp-lsp` plugin available; `duende-skills` covers OAuth/OIDC on ASP.NET Core |

Source: [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) · [.NET 8/9 end of support](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)
