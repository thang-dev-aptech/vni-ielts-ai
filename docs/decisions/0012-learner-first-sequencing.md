# ADR-0012 — Build the learner surface before the CMS, seeding content through the package schema

- **Status:** Accepted
- **Date:** 2026-08-20
- **Supersedes:** the `[CHANGED]` note in [`../development/roadmap.md`](../development/roadmap.md) Phase 5 that moved CMS ahead of the exam engine
- **Related:** [ADR-0008](0008-exam-package-format-v1.md) · `F-5` and `H-2` in [`../requirements/confirmed.md`](../requirements/confirmed.md) · `B-8`

## Context

The roadmap moved CMS ahead of the exam engine, and gave exactly one reason:

> *"the exam engine is developed against real imported content rather than hand-written fixtures — which exercises the package format early, when changing it is still cheap."*

That reasoning was sound **when it was written**. Two things have since changed it.

### 1 · The premise changed on 2026-08-20

The roadmap was written while `M-16` was open, so [`../ux/cms-spec.md`](../ux/cms-spec.md) specified the
CMS as a **read-only** surface — 29 screens, essentially no authoring. "CMS first" then meant *a small
body of work that de-risks the format early*.

`F-5` closed `M-16` toward in-place authoring. The CMS became the **largest and least-specified item
in the project**, and the authoring screen group has no UX specification at all. Keeping the original
order now means putting the biggest unknown in front of the product's core — the opposite of what the
reversal was for.

### 2 · CMS-first has no feedback loop

Upload → validate → publish, and then what? The result is verified by reading JSON out of the
database.

That is weak evidence. **The strongest proof that an exam imported correctly is sitting it.** Schema
validation confirms an answer key is well-formed; it can never confirm it is *right*. A wrong answer
key is indistinguishable from a hard question until a person answers it — which is the same argument
`T23` makes for the human review gate, applied one step earlier.

This is the point the original reversal missed, and it is decisive.

## Decision

**Build the learner surface first. Defer the CMS.**

**And seed exam content through the package-format schema, never through hand-written object graphs.**

The second half is not optional — it is the entire mitigation for the risk the original ordering
existed to address. Concretely:

- `contracts/schemas/exam.schema.json` is authored **now**, from
  [`../architecture/exam-package-format.md`](../architecture/exam-package-format.md).
- A development seeder loads exam content **by validating it against that schema**, exactly as the
  importer will.
- When the CMS arrives, it is the **third producer** of a draft `ExamVersion` through the **same**
  validator — seeder, ZIP import, in-place authoring.

## Consequences

- **The format is still exercised early**, which was the original goal. It is exercised by the seeder
  instead of by the importer, at a fraction of the cost.
- **There is one definition of a valid exam from day one**, rather than a fixture shape that later has
  to be reconciled with a schema. This closes trap `T13` rather than accepting it.
- **A wrong answer key becomes discoverable**, because the content can be sat.
- The `ExamVersion` write model must be built before the seeder is useful — which is work the exam
  engine needed anyway.
- **CMS work is deferred, not cancelled.** Nothing about this decision reduces its scope; `F-5` still
  stands, and the authoring screen group still needs specifying before it starts.

### What this does *not* unblock

**`B-8` still gates every exam-taking screen** — Reading, Listening, Writing, Speaking and Results —
and so does the missing screen inventory (`T2`). Choosing learner-first does not change that.

What it does change is the shape of the wait: the **exam engine backend** is blocked by neither.
`B-8` is a question about screen structure, so session lifecycle, server-authoritative timing, answer
persistence, submission, and deterministic Reading/Listening scoring can all be built and verified
through the API while the interface waits.

> `H-1`'s Speaking sub-question is narrower than first recorded. `SectionAttempt` accommodates **both**
> answers — one attempt carrying internal part timings, or three attempts with three deadlines. What
> the answer changes is the Full Test **chaining semantics**, not the entity.

## Alternatives considered

| Option | Why not |
|---|---|
| Keep CMS first, as the roadmap says | Its justification rested on a premise `F-5` invalidated, and it leaves the product unobservable for the whole of its largest phase |
| Learner first with **ad-hoc** demo JSON | This is the version that fails. Fixtures shaped for whatever renders conveniently drift from the format, and the drift surfaces at the first real import, after the engine has hardened around the wrong shape — trap `T13`, exactly as predicted |
| Build both thinly in parallel | Splits a small team across the two least-specified areas at once, and the CMS half would still be building against an unfrozen question-type taxonomy |
| Wait for `B-8` before doing anything | Idles the team on a decision that does not block the backend at all |
