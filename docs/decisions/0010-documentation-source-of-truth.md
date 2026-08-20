# ADR-0010 — `docs/` is canonical; AI tool configs point, never duplicate

- **Status:** Accepted
- **Date:** 2026-08-17
- **Deciders:** Solution architect
- **Related:** Requirements G-5, G-9, §20 · [`../development/ai-assisted-development.md`](../development/ai-assisted-development.md)

## Context

Requirement G-9: *do not create duplicate instructions between Claude and Cursor.* Requirement §20 asks for a clear documentation structure with a single source of truth.

Four surfaces can carry instructions: `docs/`, `CLAUDE.md`, `.claude/`, `.cursor/rules/`. Without a rule about which owns what, the same architectural principle ends up stated in three of them — and then:

1. They drift. One is updated; the others become silently wrong.
2. Claude and Cursor give contradictory advice, and the developer cannot tell which is authoritative.
3. Nobody knows where to make a change, so it lands in the most convenient place rather than the correct one.

## Options considered

| Option | For | Against |
|---|---|---|
| **Ownership by kind of instruction** | Single source per fact; drift structurally prevented | Requires discipline; a rule must be enforced |
| Duplicate into every surface | Each tool self-contained | Guaranteed drift. Directly violates G-9 |
| Generate tool configs from `docs/` | No manual duplication | Build tooling for a problem a rule solves; generated instructions read poorly |
| Put everything in `CLAUDE.md` | One file | Unusable at this documentation volume; Cursor does not read it |

## Decision

**Each surface owns a distinct *kind* of instruction. Nothing is stated twice.**

| Surface | Owns | Must not contain |
|---|---|---|
| **`docs/`** | Architecture, domain rules, decisions, research, specifications — **canonical** | — |
| **`CLAUDE.md`** | Phase status, the 8 non-negotiables as one-line invariants, pointers | Long-form rationale — link instead |
| **`.claude/`** | Agent roles, orchestration, commands, project skills | Architecture rules — link instead |
| **`.cursor/rules/`** | Editor-time coding conventions (naming, layout, lint-adjacent) | Architecture rationale — link instead |

Supporting rules:

- Architecture decisions are recorded as **ADRs**, not as prose in an architecture document.
- Every `.cursor` rule file opens with a line naming `docs/` as canonical.
- **Test:** if a rule appears in two places, delete the copy outside `docs/` and replace it with a link.

## Consequences

### Positive
- One place to change any given fact.
- Both AI tools converge on the same answer because both ultimately point at `docs/`.
- New contributors have one obvious place to look.
- ADRs preserve *why*, which prose architecture documents reliably lose.

### Negative
- Requires following links rather than reading one self-contained file.
- Discipline is needed at review time — this rule is easy to violate accidentally by "just adding a note".

### Risks accepted
- **Decay is the real risk.** The most likely failure is architecture rationale gradually migrating into `.cursor/rules/` because that is where someone was working. Detectable with:
  ```bash
  grep -ril "clean architecture\|repository pattern\|modular monolith" .cursor/
  ```
  Run it periodically and after any instruction change.

## Notes

The eight non-negotiables **are** stated in `CLAUDE.md` rather than only linked. That is deliberate and not a violation: they appear as one-line invariants with links, not explanations. An agent about to write a client-side timer needs the rule at the point of decision, not a reference to a document it may not open. The rationale lives in `docs/`; the invariant lives where it prevents the mistake.

The distinction is **statement vs. explanation**. Stating an invariant in two places is acceptable when one is a pointer. Explaining it twice is not.
