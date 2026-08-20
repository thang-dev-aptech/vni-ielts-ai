# ADR-0001 — Backend on .NET 10 / ASP.NET Core

- **Status:** Accepted
- **Date:** 2026-08-17
- **Deciders:** Product owner, solution architect
- **Related:** Requirements S-1, D-1…D-6 · [`../architecture/backend-architecture.md`](../architecture/backend-architecture.md)

## Context

A central backend API must serve four clients (learner web, Android, iOS, Admin CMS), orchestrate AI evaluation, and handle bulk content ingestion.

Constraints and observations:

- Requirement §17 lists ".NET / ASP.NET Core" and "C#" first among the skills to research, implying an existing team capability.
- The development machine has **.NET SDK 10.0.100** installed.
- **.NET 8 and .NET 9 both reach end of support on 2026-11-10** — roughly three months from this decision. Starting on either would mean migrating almost immediately.
- **.NET 10 is LTS, supported until 2028-11-14.**
- MongoDB now, PostgreSQL later (D-1, D-2) — both need mature drivers.

Sources: [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) · [.NET 8/9 end of support](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)

## Options considered

| Option | For | Against |
|---|---|---|
| **.NET 10 / ASP.NET Core** | LTS to 2028; SDK already installed; official MongoDB driver and Npgsql; mature OIDC libraries; strong typing suits a domain with strict invariants; `csharp-lsp` and `duende-skills` tooling available | Different language from the TypeScript clients |
| .NET 8 or 9 | Familiar, well-documented | **EOL 2026-11-10.** Non-starter |
| Node.js / TypeScript (NestJS) | One language across the whole system; Node 24 installed | Diverges from the skill list in §17; weaker fit for long-running background processing |
| Go | Excellent concurrency, small deployments | No stated team capability; smaller ecosystem for OIDC and document databases |

## Decision

**Build the backend on .NET 10 / ASP.NET Core.**

## Consequences

### Positive
- Three years of LTS support — no forced runtime migration during the build.
- Strong typing serves a domain with hard invariants (band scales, deadlines, entitlements) well; a `BandScore` value type can make invalid values unrepresentable.
- Official drivers for both the current and target databases, which matters directly for [ADR-0003](0003-database-mongodb-first-postgresql-target.md).
- Background worker hosting is a first-class, well-supported pattern.

### Negative
- Two languages in the system (C# and TypeScript). Mitigated by generating the client API package from OpenAPI, so type definitions have a single source.
- Container images are larger than Go equivalents. Not material at this scale.

### Risks accepted
- Team capability with .NET 10 specifically is `[ASSUMPTION]` inferred from the §17 skill list rather than stated. If wrong, this decision should be revisited before Phase 4.

## Notes

The EOL timing made this close to forced. Had .NET 8 remained supported for another two years, the choice between it and .NET 10 would have been about tooling maturity rather than lifecycle.
