---
description: Record a resolved requirement decision and update the open-questions list
argument-hint: <item ID (e.g. B-3) or short description>
---

Record a requirement decision for: **$ARGUMENTS**

## Steps

1. **Find the item** in `docs/requirements/assumptions-and-open-questions.md`. Items carry IDs like `B-1`, `H-3`, `M-2`, `V-1`. If the argument does not match an existing item, this may be a *new* decision — confirm whether to add it.

2. **Ask for the decision** if it was not supplied in the argument. Do not infer what the owner decided. Do not resolve an owner decision on their behalf.

3. **Edit the item in place.** Do **not** delete it — the record of what was once uncertain is valuable during the freeze review.
   - Replace the question with the decision, stated plainly.
   - Change the tag: `[BUSINESS DECISION]` / `[OPEN QUESTION]` → `RESOLVED` with the date.
   - Keep the original context so a reader understands what was being asked.

4. **Propagate the consequences.** A resolved decision usually invalidates an assumption elsewhere:
   - Search `docs/` for the item ID and for any `[ASSUMPTION]` that was made "meanwhile" pending this decision.
   - Update those documents to reflect the actual decision.
   - If an assumption turned out to be wrong, say so explicitly rather than quietly editing it.

5. **Write an ADR if the decision is architectural.** Requirement decisions that change system shape — provider selection, database timing, reward mechanism — need an ADR. Use `/adr`.

6. **Update `docs/requirements/confirmed.md`** if the decision adds a confirmed requirement.

## Rules

- **Never invent the decision.** If the owner has not decided, the item stays open. Recording a guess as a decision is worse than leaving it open, because it stops being visible.
- Record *what* was decided and *when*, not a reconstruction of the reasoning unless the owner gave it.
- If the decision unblocks a phase, note that in `docs/development/roadmap.md`.
- If the decision conflicts with an existing ADR, supersede that ADR rather than editing it.
