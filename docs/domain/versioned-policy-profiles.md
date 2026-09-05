# Versioned Policy Profiles

Six product decisions that used to be constants — or were never written down at all — expressed as
data carried by an exam version.

This file is the answer to task `FS0.3` in
[`../development/four-skills-functional-core-todolist.md`](../development/four-skills-functional-core-todolist.md)
§7. It specifies **shape and validation only**. It does not implement anything: the domain mapping is
`FS1.2`, the validator is `FS1.4`, and the sequence resolver is `FS7.1`.

---

## Why these six, and why now

The governing test has not changed since [`ielts-exam-structure.md`](ielts-exam-structure.md):

> **Does this value vary across exam versions, or across products VNI might offer? If yes, it is data.**

Each of the six below fails that test as a constant. Five of them are also unanswered questions, which
brings in the second rule — `G-11` in [`../../CLAUDE.md`](../../CLAUDE.md):

> **An unresolved policy becomes a configured seam with a null implementation — never an invented default.**

The two rules pull in the same direction here but for different reasons, and it is worth keeping them
apart. The first says *put the knob on the version*. The second says *do not turn the knob on the
owner's behalf*. A field can satisfy one and violate the other: `criterionWeights.writing` is on the
version and has no default, which is both; a `partialCredit` enum with a `per-slot` member nothing
implements would be the first without the second.

---

## The index

| # | Decision | JSON pointer | Shape | Resolved when absent | Status |
|---|---|---|---|---|---|
| 1 | Module sequence for a sitting | `/sequenceProfile/modules` | ordered array of module enums, unique | The `E-12` order — Reading → Listening → Writing → Speaking — through **one** resolver | `E-12` is settled; the *location* of the constant was the defect |
| 2 | Part-score policy | `/scoringProfile/partScore` | `reporting` enum + optional per-part calibration | `raw-only` — raw and accuracy, no band | Plan decision 7, and current engine behaviour |
| 3 | Partial-credit profile | `/scoringProfile/partialCredit/multiMark` | enum, **one legal member today** | `all-or-nothing` | `[OPEN QUESTION]` `H-12` |
| 4 | Explanation policy | `/policyProfile/explanation/mode` | enum `none` \| `ai-generated` | `none` | Gated by `B-2` and the `FS0.1` rights registry |
| 5 | Practice-history policy | `/policyProfile/practiceHistory/bandTrend` | enum `excluded` \| `full-test-only` | `excluded` | Gated by `H-4` |
| 6 | Writing task weights | `/scoringProfile/criterionWeights/writing` | `task1` and `task2`, both required, both `> 0` | **Nothing. There is no default and there must not be one** | `[OPEN QUESTION]` `H-8b` |

A seventh field, `/scoringProfile/bandTableProvenance`, is not one of the six but is added alongside
them because decisions 2 and 5 are unenforceable without it. → [below](#the-supporting-field-band-table-provenance)

**Every one of the six is optional in the schema, and every resolution above reproduces what the
engine does today.** That is the plan's requirement — *current defaults must not change silently* —
read literally: the values do not change, and the resolution stops being silent. See
[the three gates](#the-three-gates) for where "silent" is fixed.

---

## The three gates

"Refused by validation where the value is mandatory" needs a *where*. Putting every refusal at import
would reject `fixtures/exams/synthetic-full-1.json`, which declares none of the six and is seeded and
published by `DevelopmentExamSeeder` — the whole e2e suite runs on it. Putting every refusal at
publish would do the same thing one step later.

So the refusals are placed where the damage is:

| Gate | Refuses | Owner |
|---|---|---|
| **Schema** — `contracts/schemas/exam.schema.json` | A value that is not a legal value: an invented enum member, a half-declared ratio, a zero weight, a duplicated module, `estimated-band` with no calibration object at all | this task |
| **Import** — `ExamPackageReader` | A *declared* value that contradicts the package it is declared in: a sequence naming modules the content does not have, a calibration table that does not cover its part's raw range, a band trend claimed over tables nobody equated | `FS1.4` |
| **Report time** — scoring and results | Not the package. **The number.** A missing value suppresses the score that depends on it, names the reason, and never substitutes a default | `FS5`, `FS7` |

The third row is the one that carries the weight, and it is not a new invention — it is the pattern
the codebase already uses in three places. `ScoringProfile.BandFor` throws rather than return band 0
for a raw score its table does not cover. `ScoringProfile.RequireWritingTaskWeights` throws rather
than reach for 1:2. The Speaking pipeline returns `AwaitingVoiceProvider` rather than estimate. In
each case a missing policy costs you a number, not a package.

### The part that stops it being silent

`ExamPackageReader` emits a **`warning`-severity finding for every one of the six a package omits**,
on every import, carrying the pointer, the resolved value, and this document. `ExamPackageResult`
already carries findings on an accepted result, so this needs no new shape.

An author who never declares a sequence gets told, every time, that their package is being run in
`E-12` order because it did not say otherwise. That is the difference between a default and a silent
default, and it is the only mechanism here that is genuinely new rather than relocated.

---

## 1 · `sequenceProfile` — the one that had two answers

This is the field the task warned about, and the warning was right. Two ordered constants exist today
and they disagree:

| Site | Order |
|---|---|
| `Domain/Exams/ExamContent.cs` — `ExamVersion.FullTestOrder` | Reading, Listening, Writing, Speaking |
| `Application/Exams/ExamViews.cs` — `SittingBand.FourSkills` | **Listening, Reading, Writing, Speaking** |

`SittingBand.FourSkills` is read only for set membership, so the disagreement is invisible. It stops
being invisible the moment a sequence is read from data, because there will then be a third
order — the declared one — and three orders in a system that needs one is not a bug that announces
itself. It announces itself as a learner reaching the end of a Full Test having never been shown
Writing.

**So the resolution happens once, in one place, and the ambiguity is removed by type rather than by
discipline:**

1. `SequenceProfile.Resolve(declared, presentModules)` is the **only** function anywhere that answers
   "what order do the modules run in". `ExamVersion.NextModuleAfter` and `FirstModule` read its
   result; they do not read a constant.
2. `SequenceProfile.CanonicalOrder` — the `E-12` order — is `private`/`internal` to that resolver and
   is the fallback when a package declares nothing. It is the *only* surviving ordered module
   constant in the solution.
3. `SittingBand.FourSkills` **changes type** to `IReadOnlySet<ExamModule>`. It is used as a set; a set
   cannot disagree about order. This is what makes the fix permanent instead of a comment asking the
   next person not to sort it.
4. `apps/web/src/features/exam/skills.ts` `SKILL_ORDER` stops being the source of order and takes it
   from the session payload. Until `FS1.5` ships that field, it may keep a display constant, but
   `practiceCatalogue.ts` must stop **dropping** an exam whose modules do not match it — that is a
   filter on the order constant, and it silently hides content.

Points 1–3 are `FS7.1`. Point 4 is `FS7.1` plus `FS1.5`. None of them are this task.

**Not configured here:** whether a Full Test auto-advances, and whether Single Skill does. It does and
it does not, and that is `E-11`…`E-13` — confirmed product behaviour, not a per-version choice.

| Requirement | Statement | Status | Source |
|---|---|---|---|
| `E-12` | Full Test runs Reading → Listening → Writing → Speaking in one session | CONFIRMED | Owner brief 2026-08-20, in [`../requirements/confirmed.md`](../requirements/confirmed.md) |
| `E-13` | Single Skill never auto-advances | CONFIRMED | Owner brief 2026-08-20, in [`../requirements/confirmed.md`](../requirements/confirmed.md) |

Because `E-12` is settled, resolving an absent `sequenceProfile` to that order is **not** an invented
default. `G-11` forbids inventing an answer to an open question; it does not forbid recording a closed
one. What it does forbid is recording it in two places, which is what was happening.

---

## 2 · Part-score policy — what thirteen questions may be called

Plan decision 7: *practice at part scope shows raw and accuracy; an estimated band only where the unit
has its own calibration profile.*

The arithmetic reason is the same one that made `Section.AutoScoredMarks` count marks rather than
question objects. A band table is equated against a **forty-mark module**. A Reading part is thirteen
questions. Reading a band off row 13 of a 40-mark table is not an approximation, it is a different
table — and the number it produces looks exactly like a band score.

```jsonc
"partScore": {
  "reporting": "raw-only",          // or "estimated-band"
  "calibration": [                  // required when reporting is "estimated-band"
    { "module": "reading", "part": 1, "rawToBand": [ /* … */ ] }
  ]
}
```

The enum has **no plain `band` member**, and that is a domain statement rather than a scoping decision:
an IELTS band is defined over a whole module, so a part-level band is not a thing the scale can
express. A package cannot declare one because there is nothing for it to declare.

`calibration` carries no `maxRaw`. The maximum is computed from the part's own marks, so there is one
source of truth for it rather than two that can drift. Coverage is checked at import, the same way
`ScoringProfile.CoversRange` already checks the module tables — and for the same reason: an incomplete
table does not fail, it produces a wrong band inside the gap.

---

## 3 · Partial-credit profile — a one-member enum on purpose

`[OPEN QUESTION]` `H-12`. Real IELTS awards one mark per correct letter on a "Choose TWO" question;
`DeterministicScorer` awards all marks or none, because `AnswerKey.Accepted` models an accepted answer
as a set compared with `SetEquals` and a set has no shape for "two of these three".

```jsonc
"partialCredit": { "multiMark": "all-or-nothing" }
```

**The enum has exactly one legal member, and adding the second one now would be the mistake.** A
package could then declare `per-slot`, the reader would accept it, and the scorer would go on marking
all-or-nothing — a wrong mark with no error anywhere. The seam is declared and empty until `FS1.1`
gives each mark its own `ResponseSlot`, its own answer-sheet number and its own key; at that point
`per-slot` becomes implementable and the enum widens additively.

Note which direction the current behaviour points. Awarding partial credit is as much an invented
policy as withholding it — but withholding it cannot silently *inflate* a band, and inflating is the
error nobody reports. → `H-12` in
[`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md)

---

## 4 · Explanation policy — a content property, not a deployment switch

```jsonc
"explanation": { "mode": "none" }   // or "ai-generated"
```

It belongs to the version rather than to `appsettings` because it turns on the *content*. Asking a
model why an answer was wrong means sending the passage, the question and the key to that model. If
the source is cleared only as a fixture, it may not be sent at all; if it may be sent, it is still a
cross-border transfer of whatever the package holds. Two packages on one deployment can legitimately
have different answers, which is the definition of per-version data.

- `none` — resolved when the object is absent, and what happens today.
- `ai-generated` — an LLM may be asked, subject to the `FS0.1` rights registry and the `B-2`
  cross-border gate. Both are checked in code at request time; a package declaring `ai-generated` is a
  permission the package grants, not one it takes.

An `authored` member — explanations written into the package — is deliberately **absent** until
`FS1.1` adds the evidence reference it would point at. Declaring a mode with nothing behind it
produces a blank review screen rather than a refusal.

**No value of this field can change a band.** That is `A-11`, and it lives in code where configuration
cannot reach it.

| Requirement | Statement | Status | Source |
|---|---|---|---|
| `A-11` | Reading and Listening bands come from the answer key; an explanation can never modify a band | CONFIRMED | Owner decision in session 2026-08-20, in [`../requirements/confirmed.md`](../requirements/confirmed.md) |

---

## 5 · Practice-history policy — what may become a trend

```jsonc
"practiceHistory": { "bandTrend": "excluded" }   // or "full-test-only"
```

A trend line is read as progress. A trend built from bands that were never equated is a chart of an
artefact, and nothing on the screen says so. So the conservative reading is what an absent field
resolves to: results are recorded, and excluded from the trend.

`full-test-only` additionally requires `bandTableProvenance.status` to be `equated`. That is the gate
`H-4` has to open, and it is why the provenance field exists.

That practice at part or single-skill scope **never** enters the trend is `FS3.5` — a fixed product
invariant, not a per-version choice — so it is not offered as an option here. The only thing this
field decides is whether *this version's* full sittings count at all.

---

## 6 · Writing task weights — the null seam

`[OPEN QUESTION]` `H-8b`. Task 2 weighs more than Task 1; IELTS does not publish the ratio the way it
publishes the overall-band rule.

```jsonc
"criterionWeights": { "writing": { "task1": 1, "task2": 2 } }
```

**There is no default and this task did not add one.** `ScoringProfile.WritingTask1Weight` and
`WritingTask2Weight` are already nullable and `RequireWritingTaskWeights()` already throws — that is
the seam, and it stays empty. A version without a weighting produces two task bands and no Writing
band, which is a truthful result.

`exam/Exam1/exam.json` carries `{ "task1": 1, "task2": 2 }`. That is **one authored package declaring
its own weighting**, which is exactly what the field is for. It is not a product decision and it was
not promoted to one. `H-8b` is answered by the product owner or it is not answered.

Two things did change, both schema-level, both tightenings:

- **`task1` and `task2` are now both required when the object is present.** One half alone was
  previously legal and produced a half-stated ratio that `RequireWritingTaskWeights` rejected at
  *marking* time — in front of a learner — rather than at *import* time, in front of the author who
  could fix it.
- **Both must be `> 0`.** `minimum: 0` permitted a task weighted zero: a task the learner was told to
  write and then not marked on.

---

## The supporting field: band-table provenance

Not one of the six. Added because decisions 2 and 5 cannot be enforced without it, and because the two
band tables in this repository already say this in prose that the schema then rejected.

```jsonc
"bandTableProvenance": {
  "status": "provisional",
  "note": "Generic tables, not equated to this paper."
}
```

| Status | Meaning |
|---|---|
| `synthetic` | Invented for this repository. Never learner-facing. What `fixtures/exams/synthetic-full-1.json` is, in its own words |
| `provisional` | A real table, but generic — not equated to this paper. What `exam/Exam1/exam.json` carries today, in its own `"provisional": true` note |
| `equated` | Equated to this test version. Requires a stated `source`; the only status that may feed a band trend |

**Absent is read as not-equated** — the conservative reading, never the flattering one.

`H-4` is what moves a real package from `provisional` to `equated`. Until it is answered, no package in
this repository can legitimately declare `equated`, and the practice-history gate therefore holds
closed on its own without anyone remembering to hold it. → `H-4` in
[`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md)

---

## What these fields deliberately do **not** encode

`M-34` and `B-13` both end with *"không thêm trường nào vào `exam.schema.json`"* — do not add a field
until the Luyện đề / Thi thử question is answered. **That instruction is intact.** None of the seven
fields added here describes a session mode, a run kind, or a practice/mock distinction:

- `sequenceProfile` describes the order of a version's own modules, which `E-12` already settled.
- `policyProfile` describes what may be done with a *result*, not how a session is run.
- `partScore` describes what a sub-module score may be *called*, not which sessions may produce one.

`ExamSession.mode` keeps its current shape, and `B-13` stays open and unprejudiced.

Also still fixed in code, and not made configurable by anything here: the four modules, the four
Writing criteria, the four Speaking criteria, the 0–9 half-step band scale, and the overall-band
rounding rule. → [`band-scoring.md`](band-scoring.md)

---

## Validation rules `FS1.4` must implement

The schema half is already committed and proven — 20 probes, in
`_workspace/workflow/agents/domain-config-decisions.md`. What follows is the half the schema cannot
express, because every rule below compares a declared value against the package's own content.

Findings use the existing `ValidationFinding(Severity, Code, Path, Message)` shape.

### Errors — the package is rejected

| Code | Trigger | Path |
|---|---|---|
| `SEQUENCE_MODULE_MISMATCH` | `sequenceProfile.modules` is not exactly the set of modules present in `sections` — a module listed but absent, or present but unlisted | `/sequenceProfile/modules` |
| `PART_SCORE_CALIBRATION_MISSING` | `partScore.reporting` is `estimated-band` and some part of some auto-scored module has no `calibration` entry | `/scoringProfile/partScore/calibration` |
| `PART_SCORE_CALIBRATION_UNKNOWN_PART` | A `calibration` entry names a `(module, part)` the package does not contain | `/scoringProfile/partScore/calibration/{i}` |
| `PART_SCORE_TABLE_INCOMPLETE` | A part calibration table leaves a raw score between 0 and that part's auto-scored marks uncovered — the same bottom-of-table gap `CoversRange` already catches for modules | `/scoringProfile/partScore/calibration/{i}/rawToBand` |
| `BAND_TREND_NOT_EQUATED` | `practiceHistory.bandTrend` is `full-test-only` while `bandTableProvenance.status` is absent, `synthetic`, or `provisional` | `/policyProfile/practiceHistory/bandTrend` |
| `PARTIAL_CREDIT_UNSUPPORTED` | `partialCredit.multiMark` is any member the scorer cannot perform. Dead while the enum has one member; it is the guard that must exist **before** `FS1.1` widens the enum, not after | `/scoringProfile/partialCredit/multiMark` |

### Warnings — the package is accepted, and told

| Code | Trigger |
|---|---|
| `POLICY_DEFAULTED` | Emitted once per omitted field among the six, naming the pointer, the resolved value, and this document |
| `MULTI_MARK_WITHOUT_PARTIAL_CREDIT` | The package contains a question with `marks > 1` and declares no `partialCredit`. The policy is load-bearing in exactly this package and the author should state it knowingly. **Warning, not error** — both committed fixtures would be rejected by an error here, and rejecting a fixture to make a point about a fixture is not a validation rule |

### Test cases `FS1.4` is expected to carry

Positive:

1. The committed `fixtures/exams/synthetic-full-1.json`, unmodified, still imports and is accepted.
2. A package declaring all six coherently is accepted with **no** `POLICY_DEFAULTED` warning.
3. A package declaring none of the six is accepted with **six** `POLICY_DEFAULTED` warnings.
4. An absent `sequenceProfile` resolves to Reading → Listening → Writing → Speaking, and a version
   carrying only Reading and Writing resolves to Reading → Writing rather than throwing.
5. A declared `sequenceProfile` of `["listening", "reading", "writing", "speaking"]` over a package
   containing all four is accepted, and `NextModuleAfter(Listening)` returns Reading. This is the test
   that proves the sequence comes from data and not from `FullTestOrder`.
6. `partScore` absent ⇒ a part-scope run reports raw and accuracy and **no** band field at all — not a
   null band, not band 0.

Refusals — each must be shown to go red when the rule is removed:

7. `sequenceProfile.modules` omits Writing while the package has a Writing section ⇒
   `SEQUENCE_MODULE_MISMATCH`.
8. `sequenceProfile.modules` names Speaking while the package has no Speaking section ⇒
   `SEQUENCE_MODULE_MISMATCH`.
9. `reporting: "estimated-band"` with calibration for Reading part 1 only, over a three-part Reading
   section ⇒ `PART_SCORE_CALIBRATION_MISSING`.
10. A part calibration table whose lowest `minRaw` is 5, over a 13-mark part ⇒
    `PART_SCORE_TABLE_INCOMPLETE` naming raw 0 — the bottom-of-table gap, not the top.
11. `bandTrend: "full-test-only"` with `status: "provisional"` ⇒ `BAND_TREND_NOT_EQUATED`.
12. `bandTrend: "full-test-only"` with no `bandTableProvenance` at all ⇒ `BAND_TREND_NOT_EQUATED`.
    Absence must fail the same way `provisional` does, or the gate is opened by omission.
13. `criterionWeights.writing` with `task1` only ⇒ schema refusal at import, **not** an exception
    later from `RequireWritingTaskWeights`.
14. `partialCredit.multiMark: "per-slot"` ⇒ schema refusal today; after `FS1.1` widens the enum, this
    case moves to `PARTIAL_CREDIT_UNSUPPORTED` and must not become an acceptance.
15. A version with a Writing section, two tasks and no `criterionWeights` produces two task bands and
    no Writing band, and the result says why. It does **not** produce a band computed at 1:2.

---

## Reserved for `FS1.1`

`FS1.1` edits this schema next, additively. These names are held for it and must not be taken:

| Field | Level | For |
|---|---|---|
| `formatProfile` | top | Declared question types, slot numbering style |
| `scoringProfileRef` | top | A scoring profile shared across versions rather than inlined |
| `slots` | `question` | `ResponseSlot[]` — the answer-sheet numbers a question occupies |
| `explanation` | `question` | Authored explanation evidence, which is what unlocks the `authored` member of `explanationPolicy.mode` |
| `timing` | `part` | Per-part timing, currently per-section only |

Three notes for whoever picks it up:

- `$defs.question` has **no** `additionalProperties: false`, unlike every other definition in the file.
  That is what makes `slots` addable without a coordinated bump, and it should probably stay that way
  until `FS1.1` lands rather than being tightened first.
- `$id` and the `formatVersion` pattern `^1\.[0-9]+$` were **not** touched. A package declaring `"1.1"`
  already validates. `FS1.1` owns whatever version bump it needs; changing the `$id` here would have
  moved a resolution key that `ExamPackageReader` loads.
- `policyProfile` is `additionalProperties: false`, so a fourth policy is a deliberate edit. That is
  the intent.

---

## Known conflicts, stated rather than relaxed

**1 · `exam/Exam1/exam.json` does not validate against this schema, and did not before this task
either.** It is an index document, not a package: its `sections` is `["listening", "reading",
"writing", "speaking"]` — a list of folder names — and it carries `sectionOrder`, `sectionOrderNote`,
`openQuestions`, `scoringProfile.overallBand`, and `rawToBand.provisional` / `note` /
`readingDeviation`, none of which the format has ever had. Nothing in the backend reads it. Verified
against the baseline commit before any edit: 11 schema errors then, and the same 11 now.

Two of those keys have real homes as of this task — `rawToBand.provisional`/`note` become
`bandTableProvenance`, and `sectionOrder` becomes `sequenceProfile.modules`. Converting the file is
`FS2`'s import work, not this task's, and it should not be done by hand.

**2 · Both committed fixtures would fail a `criterionWeights`-is-mandatory rule, so no such rule was
written.** `fixtures/exams/synthetic-full-1.json` has a Writing section with two tasks and no
weighting. That is not a defect in the fixture — it is `H-8b` showing through content, exactly as it
should. The correct behaviour is the one specified above: import accepts, and the Writing band does
not exist. Making it an import error would reject the fixture the whole e2e suite runs on, to enforce
a rule about a number that fixture may not show a learner anyway.

**3 · Both committed fixtures carry a `marks > 1` question and declare no `partialCredit`.** Hence
`MULTI_MARK_WITHOUT_PARTIAL_CREDIT` is a warning. An error there would reject both fixtures on day one.

---

## Where the rest of this lives

- Band scale, the rounding rule, and why the tables are data — [`band-scoring.md`](band-scoring.md)
- The fixed-versus-configurable table this file extends — [`ielts-exam-structure.md`](ielts-exam-structure.md)
- Entity model and the `Exam`/`Attempt`/`Result` vocabulary — [`domain-model.md`](domain-model.md)
- `H-4`, `H-8b`, `H-12`, `B-13`, `M-34` — [`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md)
