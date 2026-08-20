# AI-Assisted Development

How Claude Code and Cursor are configured for this repository, and how duplication between them is prevented.

---

## The source-of-truth problem

Requirement G-9: *do not create duplicate instructions between Claude and Cursor.*

This matters more than it sounds. When the same architectural rule is written in `CLAUDE.md`, `.cursor/rules/`, and `docs/architecture/`, three things go wrong:

1. They drift. One gets updated; the others silently become wrong.
2. The two tools give contradictory advice, and the developer cannot tell which is authoritative.
3. Nobody knows where to make a change, so changes get made in the most convenient place — usually the wrong one.

### The rule

> **Each surface owns a distinct *kind* of instruction. Nothing is stated twice.**

| Surface | Owns | Must not contain |
|---|---|---|
| **`docs/`** | Architecture, domain rules, decisions, research, specifications | — (this is canonical) |
| **`CLAUDE.md`** | Phase status, the 8 non-negotiables, pointers into `docs/` | Long-form rationale — link instead |
| **`.claude/`** | Agent roles, orchestration, commands, project skills | Architecture rules — link instead |
| **`.cursor/rules/`** | Editor-time coding conventions (naming, layout, lint-adjacent) | Architecture rationale — link instead |

**Test:** if you find the same rule in two places, delete the copy outside `docs/` and replace it with a link.

### Why the non-negotiables *are* in `CLAUDE.md`

They are stated there as one-line invariants with links, not explanations. An AI agent about to write a client-side timer needs the rule at the point of decision, not a reference to a document it may not read. The rationale lives in `docs/`; the invariant lives where it prevents the mistake.

---

## Claude Code

### Agents — 10

Each owns distinct artifacts and a distinct failure mode. Full roster and orchestration: [`agent-orchestration.md`](agent-orchestration.md).

```
product-analyst        domain-analyst         solution-architect
backend-engineer       ai-evaluation-engineer frontend-engineer
mobile-engineer        qa-engineer            security-engineer
devops-engineer
```

**There is deliberately no custom `code-reviewer` agent.** Claude Code ships `/code-review` and `/security-review` skills already. Adding a custom reviewer would create a second, competing review instruction set — exactly the duplication problem this document exists to prevent.

### Commands — 3

| Command | Purpose |
|---|---|
| `/adr` | Scaffold the next-numbered ADR with correct format |
| `/req` | Record a requirement decision, updating the open-questions list |

Each exists because it is a repeated, error-prone, format-sensitive task. `/adr` in particular prevents the numbering collisions and format drift that make an ADR set unusable.

### Project skills — 3

Procedural knowledge worth loading on demand rather than carrying in every context:

| Skill | Loads when |
|---|---|
| `ielts-domain` | Working on exam structure, scoring, or band conversion |
| `exam-package-format` | Working on ZIP import, validation, or the package spec |
| `ai-evaluation-contract` | Working on AI output schemas, validation, or prompts |

These carry *how to do the thing correctly*, including the traps — e.g. that band values must be an `enum`, not a `minimum`/`maximum` range.

### Hooks — 1

| Hook | Purpose |
|---|---|
| PreToolUse guard on `.env*` and credential files | Blocks writes to credential files. Enforces [CLAUDE.md](../../CLAUDE.md) rule 6 mechanically rather than by convention |

**Deliberately minimal.** Hooks that format or lint code (`dotnet format` on `*.cs` edit) are documented as Phase-4 additions — writing them now, against a codebase that does not exist, would produce hooks that fail on every invocation.

### Settings

`.claude/settings.json` allowlists the read-only commands this repository actually uses, reducing permission prompts without granting write access.

---

## Cursor

`.cursor/rules/` holds **editor-time coding conventions only** — five focused `.mdc` files:

| File | Scope |
|---|---|
| `00-project-context.mdc` | Orientation and pointers into `docs/`. Always applied |
| `10-dotnet-backend.mdc` | C# conventions, layering rules, applied to `**/*.cs` |
| `20-react-capacitor.mdc` | TS/React conventions, applied to `**/*.ts`, `**/*.tsx` |
| `30-security.mdc` | Security-sensitive coding patterns |
| `40-testing.mdc` | Test conventions |

Each begins with a line naming `docs/` as canonical, so a developer who reads only the Cursor rule still knows where the real answer lives.

---

## Division of labour

| Task | Tool |
|---|---|
| Research, architecture, ADRs | **Claude Code** — multi-file reasoning, agents, web research |
| Multi-file refactoring | **Claude Code** |
| Domain and specification work | **Claude Code** with the relevant agent |
| Line-level completion while typing | **Cursor** |
| Single-file edits in flow | **Cursor** |
| Code review | **Claude Code** `/code-review` |
| Security review | **Claude Code** `/security-review` |

Not a hard boundary — a rough guide to which tool each task suits.

---

## Rules that apply to both tools

These come from `docs/` and are enforced through whichever surface the tool reads:

1. **No AI provider code or credentials** until the owner decides ([CLAUDE.md](../../CLAUDE.md) rule 6). The `.env*` hook enforces the credential half mechanically.
2. **No application code during Phase 0.** The current phase is research and specification.
3. **Tag uncertainty** rather than resolving it silently — the five-tag legend in [`../README.md`](../README.md).
4. **Cite external claims.** Anything about IELTS, a platform API, or a regulation carries a source link.
5. **Every major decision gets an ADR.**

---

## Verifying the setup stays clean

Periodically, and after any change to instructions:

```bash
# No architecture rules leaked into .cursor
grep -ril "clean architecture\|repository pattern\|modular monolith" .cursor/

# No credential-shaped content anywhere
grep -riE 'api[_-]?key|sk-[a-z]+-|_API_KEY' . --exclude-dir=.git

# Every doc reachable from the index
# (compare docs/README.md links against the file tree)
```

The first check is the one that matters most — architecture rationale migrating into `.cursor/rules/` is the most common way this setup decays.
