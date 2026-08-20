# ADR-0004 — One strict persistence boundary, not full Clean Architecture

- **Status:** Accepted
- **Date:** 2026-08-17
- **Deciders:** Solution architect
- **Related:** Requirements D-3, D-4, D-5, G-1, G-2 · [ADR-0003](0003-database-mongodb-first-postgresql-target.md)

## Context

Two requirements pull in opposite directions:

- **D-3/D-4:** the MongoDB→PostgreSQL migration must be *manageable*; business logic must not be tightly coupled to MongoDB.
- **D-5/G-1:** do **not** prematurely create an overly complex Clean Architecture implementation; do not over-engineer.

Getting this balance wrong is the most likely architectural failure on this project. Too little abstraction makes the migration a rewrite of the whole application. Too much produces a codebase heavier than the migration it was meant to avoid — and every feature pays that cost forever, while the migration is paid once.

## Options considered

| Option | For | Against |
|---|---|---|
| **One strict boundary** | Migration confined to a single project; low ongoing cost; simple to explain and enforce | Requires discipline — a single leaked type undermines it |
| Full Clean Architecture | Textbook separation | Directly contradicts D-5. Ceremony on every feature for a one-time benefit |
| Generic `IRepository<T>` + specifications | Feels reusable | Leaks storage semantics into `Application`; produces awkward queries in *both* databases |
| Database-agnostic query abstraction | Zero-effort migration in theory | Reinventing an ORM. Enormous cost, poor results |
| No abstraction | Simplest now | Migration becomes an application-wide rewrite. Violates D-3 |

## Decision

**Exactly one strict boundary:**

> Repository interfaces live in `Application`. Persistence models and mapping live in `Infrastructure`. Domain entities carry no persistence attributes.

Enforced by an **automated architecture test** that fails the build if `Domain` or `Application` references a persistence or vendor namespace.

Explicitly **not** required: generic repositories, the specification pattern, a shared Unit-of-Work abstraction, one interface per use case, or mandatory mediator indirection.

## Consequences

### Positive
- Migration is confined to `Vni.Ielts.Infrastructure`. `Domain`, `Application`, `Api`, and `Worker` do not change.
- The rule is one sentence, so it can actually be followed and reviewed.
- Ongoing cost is near zero — repository interfaces are needed for testability regardless.
- Domain entities stay expressive: a `BandScore` value object rejecting invalid values is not fighting an ORM.

### Negative
- Hand-written mapping between domain entities and persistence models.
- Repository interfaces must be written per aggregate rather than generically.

### Risks accepted
- **The boundary decays silently.** A single `[BsonId]` on a domain entity, or an `ObjectId` in a method signature, breaks it — and nothing fails at runtime. This is why the architecture test is not optional: an unchecked rule is a rule that erodes.

## Notes

The distinguishing question, applied to any proposed abstraction: **does this reduce migration cost more than it adds to every feature built between now and then?**

Repository interfaces pass — they are needed for testing anyway. A generic repository fails: it adds friction to every query for a migration that still requires rewriting each implementation.

If exactly one thing from this ADR survives review, it should be the architecture test. The rule without enforcement is a comment.
