---
description: Scaffold the next-numbered Architecture Decision Record
argument-hint: <short decision title>
---

Create a new Architecture Decision Record for: **$ARGUMENTS**

## Steps

1. **Determine the next number.** List `docs/decisions/` and find the highest existing `NNNN-` prefix. The new ADR is that number + 1, zero-padded to four digits. Do not reuse or skip numbers.

2. **Read the template** at `docs/decisions/ADR-template.md` and follow its structure exactly.

3. **Create** `docs/decisions/NNNN-<kebab-case-title>.md`.

4. **Fill it in properly:**
   - **Status:** `Proposed` unless the decision is already made and agreed. Do not mark something `Accepted` that has not been.
   - **Date:** today's date.
   - **Context:** the forces and constraints that make a decision necessary. Facts, not the decision. Cite external claims; tag uncertain ones.
   - **Options considered:** at least two, including "do nothing" where that is a real option. *An ADR listing one option is not a decision record* — if you only have one option, you are documenting a fact, not a decision.
   - **Decision:** stated plainly and unambiguously.
   - **Consequences:** positive, negative, **and risks accepted**. The negative section being empty is a sign the analysis is incomplete — every real decision costs something.
   - **Notes:** what would make this decision wrong later. This is the section a future reader will value most.

5. **Link it from anywhere relevant** — the architecture document it affects, `CLAUDE.md` if it establishes an invariant, and the risk register if it accepts a risk.

## Rules

- If this decision supersedes an existing ADR, mark the old one `Superseded by [ADR-NNNN]` and link both ways.
- Never edit an `Accepted` ADR to change its decision. Write a new one that supersedes it — the record of what was decided and why is the point.
- Keep it short. An ADR is a decision record, not a design document. Link to `docs/` for detail.
