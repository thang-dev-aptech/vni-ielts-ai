# FS0.3 · Product/config decisions as versioned data

**Task id:** `FS0.3` — `docs/development/four-skills-functional-core-todolist.md` §7
**Agent:** domain-analyst · **Run:** `fscore-20260829`
**Baseline:** `35bf37ce9b459222036710a6770541ec3d26d829`
**Boundary honoured:** only `contracts/schemas/**` and `docs/domain/**` were edited. No `backend/**`,
no `scripts/**`, no `package.json`, no `apps/**`, no `contracts/openapi/**`.

---

## 1 · Files changed

| File | Change |
|---|---|
| `contracts/schemas/exam.schema.json` | +135 / −3. Two new top-level properties, three new `scoringProfile` siblings, seven new `$defs`, and one tightening of `criterionWeights.writing` |
| `docs/domain/versioned-policy-profiles.md` | **New.** The specification: the six decisions, where each refusal happens, the rules `FS1.4` must implement, fields reserved for `FS1.1`, and the conflicts found |
| `docs/domain/band-scoring.md` | +6. Band-table provenance and the part-scope no-band rule under `H-4`; the both-halves rule under `H-8b` |
| `docs/domain/ielts-exam-structure.md` | +15 / −1. Six rows added to the fixed-vs-configurable table; the `ExamVersion` tree gains `SequenceProfile` and `PolicyProfile` |

Not touched deliberately: `$id`, and the `formatVersion` pattern `^1\.[0-9]+$`. A package declaring
`"1.1"` already validates, and `$id` is a resolution key `ExamPackageReader` loads — moving it is
`FS1.1`'s call, not a side effect of this task.

---

## 2 · The six, and the shape chosen for each

| # | Decision | Pointer | Shape | Absent resolves to |
|---|---|---|---|---|
| 1 | Module sequence | `/sequenceProfile/modules` | ordered array of module enums, `uniqueItems`, 1–4 | The `E-12` order, through **one** resolver |
| 2 | Part-score policy | `/scoringProfile/partScore` | `reporting`: `raw-only` \| `estimated-band`; optional `calibration[]` of `{module, part, rawToBand}` | `raw-only` — raw and accuracy, no band |
| 3 | Partial-credit profile | `/scoringProfile/partialCredit/multiMark` | enum with **one** member: `all-or-nothing` | `all-or-nothing` |
| 4 | Explanation policy | `/policyProfile/explanation/mode` | enum `none` \| `ai-generated` | `none` |
| 5 | Practice-history policy | `/policyProfile/practiceHistory/bandTrend` | enum `excluded` \| `full-test-only` | `excluded` |
| 6 | Writing task weights | `/scoringProfile/criterionWeights/writing` | `task1` **and** `task2`, both required, both `exclusiveMinimum: 0` | **Nothing. No default** |

One supporting field was added because 2 and 5 are unenforceable without it:

| — | Band-table provenance | `/scoringProfile/bandTableProvenance` | `status`: `synthetic` \| `provisional` \| `equated`; `source` required when `equated`; optional `note` | Read as **not equated** |

`exam/Exam1/exam.json` already carries this information as prose the schema then rejected —
`rawToBand.provisional: true` plus a note. It now has a home.

### Two shape decisions worth stating

**Placement.** Scoring-affecting policy sits under `scoringProfile` (`partScore`, `partialCredit`,
`bandTableProvenance`, and the existing `criterionWeights`); what may be *done with a result* sits
under a new `policyProfile` (`explanation`, `practiceHistory`); order sits at top level as
`sequenceProfile`, where `FS1.1` already expects it beside `formatProfile` and `scoringProfileRef`.
Each field is where its failure surfaces. The index mapping decision → pointer is in
`docs/domain/versioned-policy-profiles.md`.

**No `maxRaw` on a part calibration entry.** It is computed from the part's own marks, so there is one
source of truth for it rather than two that can drift — the same reason `Section.AutoScoredMarks` sums
marks instead of trusting a declared count.

**`partScore.reporting` has no plain `band` member.** An IELTS band is defined over a whole module, so
a part-level band is not a thing the scale can express. The format cannot state one because there is
nothing to state.

---

## 3 · What is deliberately null, and why

| Seam | Left null | Why |
|---|---|---|
| `criterionWeights.writing` | **No default, in the schema or anywhere** | `H-8b` has never been settled. `ScoringProfile.WritingTask1Weight`/`WritingTask2Weight` are already nullable and `RequireWritingTaskWeights()` already throws; this task did not fill the seam. A version without weights produces two task bands and no Writing band. `exam/Exam1/exam.json`'s `{task1: 1, task2: 2}` is one authored package declaring its own weighting — **not promoted to a default** |
| `partialCredit.multiMark` | **Enum has exactly one legal member** | `H-12` is open, and the scorer can only do all-or-nothing: `AnswerKey.Accepted` is a set compared with `SetEquals`. Adding `per-slot` now would let a package declare a mode nothing implements, and the failure would be silent wrong marking rather than a refusal. `FS1.1` widens the enum when `ResponseSlot` exists |
| `explanationPolicy.mode` | **No `authored` member** | It would point at an explanation-evidence field `FS1.1` has not added. Declaring a mode with nothing behind it produces a blank review screen, not an error |
| Band for a part with no calibration | **No band at all** — not null, not 0 | Plan decision 7. A 40-mark table read at row 13 produces a number that looks exactly like a band |
| `practiceHistory.bandTrend` | **`excluded` until `H-4` answers** | `full-test-only` requires `bandTableProvenance.status: equated`, which no package in this repository can honestly declare today. The gate holds closed on its own |

### Current defaults did not change

All six resolutions reproduce what the engine does today: `E-12` order, no part bands, all-or-nothing
marking, no explanations, no band trend, and a refusal on Writing weights. The plan's *"defaults must
not change silently"* is met by not changing them **and** by removing the silence — see the
`POLICY_DEFAULTED` warning below.

---

## 4 · Where each refusal happens — three gates, not one

Putting every refusal at import would reject `fixtures/exams/synthetic-full-1.json`, which declares
none of the six and which `DevelopmentExamSeeder` seeds **and publishes** (`ExamVersionStatus.Published`,
`DevelopmentExamSeeder.cs:133`) — the whole e2e suite runs on it. A publish-time gate would do the
same thing one step later. So:

| Gate | Refuses | Owner |
|---|---|---|
| Schema | A value that is not a legal value | this task — committed |
| Import (`ExamPackageReader`) | A *declared* value that contradicts the package it sits in | `FS1.4` |
| Report time | Not the package. **The number** — suppressed, with a named reason, never defaulted | `FS5`, `FS7` |

The third row is the pattern the codebase already uses three times: `BandFor` throws rather than
return band 0 for an uncovered raw score, `RequireWritingTaskWeights` throws rather than reach for
1:2, and the Speaking pipeline returns `AwaitingVoiceProvider` rather than estimate.

---

## 5 · Validation rules `FS1.4` must implement

Findings use the existing `ValidationFinding(Severity, Code, Path, Message)`.

### Errors — package rejected

| Code | Trigger | Path |
|---|---|---|
| `SEQUENCE_MODULE_MISMATCH` | `sequenceProfile.modules` is not exactly the module set in `sections` | `/sequenceProfile/modules` |
| `PART_SCORE_CALIBRATION_MISSING` | `reporting: estimated-band` and some part of some auto-scored module has no calibration entry | `/scoringProfile/partScore/calibration` |
| `PART_SCORE_CALIBRATION_UNKNOWN_PART` | A calibration entry names a `(module, part)` the package does not contain | `/scoringProfile/partScore/calibration/{i}` |
| `PART_SCORE_TABLE_INCOMPLETE` | A part table leaves a raw score between 0 and that part's auto-scored marks uncovered — the same bottom-of-table gap `CoversRange` already catches | `/scoringProfile/partScore/calibration/{i}/rawToBand` |
| `BAND_TREND_NOT_EQUATED` | `bandTrend: full-test-only` while provenance is absent, `synthetic`, or `provisional` | `/policyProfile/practiceHistory/bandTrend` |
| `PARTIAL_CREDIT_UNSUPPORTED` | `multiMark` is a member the scorer cannot perform. Dead while the enum has one member — it must exist **before** `FS1.1` widens it, not after | `/scoringProfile/partialCredit/multiMark` |

### Warnings — accepted, and told

| Code | Trigger |
|---|---|
| `POLICY_DEFAULTED` | One per omitted field among the six, naming the pointer, the resolved value, and `docs/domain/versioned-policy-profiles.md`. **This is the mechanism that makes the defaults non-silent, and it is the only genuinely new behaviour specified here** |
| `MULTI_MARK_WITHOUT_PARTIAL_CREDIT` | The package has a `marks > 1` question and declares no `partialCredit`. Warning, not error — both committed fixtures carry such a question |

### Expected test cases

Positive:

1. Committed `fixtures/exams/synthetic-full-1.json`, unmodified, imports and is accepted.
2. All six declared coherently ⇒ accepted, **zero** `POLICY_DEFAULTED`.
3. None declared ⇒ accepted, **six** `POLICY_DEFAULTED`.
4. Absent `sequenceProfile` ⇒ Reading → Listening → Writing → Speaking; a version with only Reading
   and Writing ⇒ Reading → Writing, not a throw.
5. Declared `["listening","reading","writing","speaking"]` ⇒ `NextModuleAfter(Listening) == Reading`.
   **This is the test that proves the order comes from data and not from `FullTestOrder`.**
6. `partScore` absent ⇒ a part-scope run reports raw and accuracy and **no band field at all** — not
   a null band, not band 0.

Refusals — each shown to go red when the rule is removed:

7. `modules` omits Writing while a Writing section exists ⇒ `SEQUENCE_MODULE_MISMATCH`.
8. `modules` names Speaking while no Speaking section exists ⇒ `SEQUENCE_MODULE_MISMATCH`.
9. `estimated-band` with calibration for Reading part 1 only, over three parts ⇒
   `PART_SCORE_CALIBRATION_MISSING`.
10. A part table whose lowest `minRaw` is 5, over a 13-mark part ⇒ `PART_SCORE_TABLE_INCOMPLETE`
    naming raw **0** — the bottom-of-table gap, not the top.
11. `full-test-only` + `status: provisional` ⇒ `BAND_TREND_NOT_EQUATED`.
12. `full-test-only` + **no** provenance ⇒ `BAND_TREND_NOT_EQUATED`. Absence must fail the same way
    `provisional` does, or the gate opens by omission.
13. `criterionWeights.writing` with `task1` only ⇒ refused at **import**, not later by
    `RequireWritingTaskWeights` in front of a learner.
14. `multiMark: "per-slot"` ⇒ schema refusal today; after `FS1.1` widens the enum this case moves to
    `PARTIAL_CREDIT_UNSUPPORTED` and must not become an acceptance.
15. Writing section, two tasks, no weights ⇒ two task bands, no Writing band, reason stated. **Not** a
    band computed at 1:2.

---

## 6 · `sequenceProfile` — resolving the ambiguity once

Two ordered constants exist and disagree: `ExamVersion.FullTestOrder` is Reading, Listening, Writing,
Speaking; `SittingBand.FourSkills` is **Listening, Reading**, Writing, Speaking. Harmless only while
the second is read as a set.

The design removes the ambiguity **by type**, not by discipline. `FS7.1` implements:

1. `SequenceProfile.Resolve(declared, presentModules)` is the only function that answers "what order".
   `NextModuleAfter` and `FirstModule` read its result, not a constant.
2. `SequenceProfile.CanonicalOrder` (the `E-12` order) becomes `private`/`internal` to that resolver
   and is the only surviving ordered module constant in the solution.
3. **`SittingBand.FourSkills` changes type to `IReadOnlySet<ExamModule>`.** A set cannot disagree about
   order. This is what makes the fix permanent instead of a comment asking the next person not to sort it.
4. `apps/web` `SKILL_ORDER` takes order from the session payload (`FS1.5`); `practiceCatalogue.ts`
   must stop *dropping* an exam whose modules do not match the constant — that filter silently hides
   content.

Resolving an absent `sequenceProfile` to `E-12` is not an invented default: `E-12`/`E-13` are
CONFIRMED with an owner source. `G-11` forbids inventing an answer to an open question, not recording
a closed one — what it forbids is recording it in two places, which is what was happening.

---

## 7 · Fields reserved for `FS1.1` — do not take these names

| Field | Level | For |
|---|---|---|
| `formatProfile` | top | Declared question types, slot numbering style |
| `scoringProfileRef` | top | A scoring profile shared across versions rather than inlined |
| `slots` | `question` | `ResponseSlot[]` — the answer-sheet numbers a question occupies |
| `explanation` | `question` | Authored explanation evidence — unlocks an `authored` member on `explanationPolicy.mode` |
| `timing` | `part` | Per-part timing, currently per-section only |

Three notes for `FS1.1`:

- **`$defs.question` has no `additionalProperties: false`**, unlike every other definition in the file.
  That is what makes `slots` addable without a coordinated bump. Leave it open until `FS1.1` lands.
- `$id` and the `formatVersion` pattern are untouched; `"1.1"` already validates.
- `policyProfile` **is** `additionalProperties: false`, so a fourth policy is a deliberate edit. Intended.

Additivity check: every field added here is optional, and every new `$defs` entry is referenced from
exactly one place, so `FS1.1` can add its own siblings without restructuring any of them.

---

## 8 · Commands and exit codes

Validator: `@redocly/ajv@8.11.2` `dist/2020.js` (draft 2020-12), already present under
`node_modules/.pnpm` as a transitive dependency. Runner scripts live in the session scratchpad —
nothing was added to `scripts/` or `package.json`, which `FS0.2`/`FS0.6` own.

| # | Command | Exit | Result |
|---|---|---:|---|
| 1 | `node <scratch>/validate.mjs contracts/schemas/exam.schema.json fixtures/exams/synthetic-full-1.json backend/.../valid-exam.json exam/Exam1/exam.json` — **before any edit** | 1 | Fixtures PASS; `exam/Exam1/exam.json` FAIL with 11 errors. **Pre-existing** |
| 2 | `node scripts/check-docs.mjs` — before any edit | 0 | 130 files, 683 links, 70 CONFIRMED rows sourced |
| 3 | `node -e "JSON.parse(...exam.schema.json)"` | 0 | Valid JSON after edits |
| 4 | `node <scratch>/validate.mjs contracts/schemas/exam.schema.json fixtures/exams/synthetic-full-1.json backend/.../valid-exam.json` — **after edits** | 0 | **Both fixtures still PASS. No breaking change** |
| 5 | `node <scratch>/probe.mjs` | 0 | **20 probes, 0 disagreed** — 3 positive, 17 refusals, each asserting its expected verdict |
| 6 | `npx prettier --check contracts/schemas/exam.schema.json` | 0 | `contracts/schemas/` is **not** in `.prettierignore`, so this gates `pnpm format:check` |
| 7 | `node scripts/check-docs.mjs` — after edits | 0 | 131 files, 699 links, **73 CONFIRMED rows, 73 sourced** |
| 8 | `git diff --check` | 0 | No whitespace errors. Three CRLF advisories on files this task did not touch |
| 9 | `node scripts/check-docs.mjs` — final re-run, several minutes later | **1** | **Not this task.** See below |

### Command 9 — a red `check-docs` that this task did not cause

Command 7 was green on 131 files and **12** collections. Command 9, run after the report was written,
is red on **13**:

> `migration inventory: docs/database/migration-plan.md does not mention` `content_sources`, `which the application opens.`

Between the two runs, the concurrent `FS0.1` agent added
`_db.GetCollection<Content.ContentSourceDocument>("content_sources")` at
`backend/src/Vni.Ielts.Infrastructure/Persistence/MongoContext.cs:72`, and `check-docs.mjs` check 9
requires every collection the application opens to have a row in the migration runbook.

Both files are outside this task's boundary — `backend/**` and `docs/database/**` — so it was **not
fixed here**. `FS0.1` owes `docs/database/migration-plan.md` a `content_sources` row before the FS0
phase gate; nothing in `contracts/schemas/**` or `docs/domain/**` is implicated. Re-running check-docs
against only this task's changes reproduces command 7's green result.

### The 20 probes (command 5)

Positive: untouched fixture · all seven fields declared coherently · `sequenceProfile` absent.

Refusals: writing weights `task1` alone · `task2` alone · a zero weight · empty object ·
`multiMark: per-slot` · `estimated-band` with no calibration · `reporting: band` · duplicated module ·
a non-IELTS module · empty module list · `equated` with no source · an invented provenance status ·
`mode: authored` · an invented `bandTrend` · an unknown `policyProfile` key · a calibration entry with
no table · a calibration table carrying band `6.3`.

The probe script asserts the *expected verdict* per case and exits non-zero on any disagreement, so a
rule that stops biting is a red run rather than a quietly green one.

**Not run, and cannot be by this agent:** `dotnet test backend` and `pnpm check`. `ExamPackageReader`
is unchanged, and no removed constraint could have broken it, but `ExamPackageReaderTests` (14) should
be run by whoever holds the backend boundary before this phase closes.

---

## 9 · Conflicts found — stated, not relaxed

**1 · `exam/Exam1/exam.json` does not validate against this schema, and did not before this task.**
Verified at the baseline commit before any edit: 11 errors, and the same 11 after. It is an *index*
document, not a package — `sections` is `["listening","reading","writing","speaking"]`, a list of
folder names — plus `sectionOrder`, `sectionOrderNote`, `openQuestions`,
`scoringProfile.overallBand`, and `rawToBand.provisional`/`note`/`readingDeviation`. Nothing in the
backend reads it (survey §5). Two of those keys now have real homes:
`rawToBand.provisional`/`note` → `bandTableProvenance`, and `sectionOrder` → `sequenceProfile.modules`.
**Converting the file is `FS2` import work and should not be done by hand.**

**2 · A "Writing weights are mandatory" import rule would reject the committed fixture.**
`fixtures/exams/synthetic-full-1.json` has a Writing section with two tasks and no `criterionWeights`.
That is `H-8b` showing through content, not a fixture defect. **No such rule was written.** The
specified behaviour is import-accepts / no-Writing-band. Making it an import error would reject the
fixture the e2e suite runs on, to enforce a rule about a number that fixture may not show a learner
anyway.

**3 · A "partial-credit is mandatory when `marks > 1`" error would reject *both* fixtures.**
`synthetic-full-1.json` (Listening, 4 questions / 5 marks) and
`backend/tests/.../Content/valid-exam.json` (Reading, 39 questions / 40 marks) each carry one. Hence
`MULTI_MARK_WITHOUT_PARTIAL_CREDIT` is a **warning**.

**4 · `M-34`/`B-13` say "không thêm trường nào vào `exam.schema.json`" until the Luyện đề / Thi thử
question is answered. That instruction is intact.** None of the seven fields describes a session mode,
a run kind, or a practice/mock distinction. `sequenceProfile` describes the order of a version's own
modules (`E-12`, settled); `policyProfile` describes what may be done with a *result*; `partScore`
describes what a sub-module score may be *called*. `ExamSession.mode` keeps its shape and `B-13` stays
open and unprejudiced. Flagged here so the orchestrator can confirm the reading rather than discover it.

---

## 10 · Risks

| Risk | Severity | Mitigation |
|---|---|---|
| `FS1.4` implements the schema half and skips the import half, leaving `SEQUENCE_MODULE_MISMATCH` and the calibration rules unwritten. The schema alone cannot catch a sequence that disagrees with its own content | High | §5 lists the codes and the test cases; §5 case 7/8 are the ones that must exist |
| `FS7.1` adds the resolver but leaves `SittingBand.FourSkills` an ordered list, and a third order appears | High | §6 point 3 is a **type change**, not a convention. If it lands as a comment instead, the ambiguity survives |
| Someone reads `exam/Exam1`'s `{task1: 1, task2: 2}` as the product answer to `H-8b` | Medium | Stated in three places now — the schema description, the domain doc, §3 here. It remains a live risk because the number is sitting right there |
| `POLICY_DEFAULTED` gets dropped as noise, and the defaults go quiet again | Medium | It is the only new behaviour in this task. Without it the work reduces to relocating constants |
| `packages/api-client` / OpenAPI drift | Low | `contracts/openapi/` is generated from the running API and untouched. No API surface was changed here. `FS1.5` exposes these fields |
| A future author declares `equated` without an equating study, to unlock the band trend | Low | `source` is required and free text. This is an editorial control, not a technical one — `FS2.4` review workflow is where it is actually enforced |

---

## 11 · Next dependency

**`FS1.1` (schema v2 + `ResponseSlot`) is unblocked and must edit this file next, not concurrently.**
It should read §7 first: five names are reserved, `$defs.question` is deliberately left open, and
widening `partialCredit.multiMark` to `per-slot` and `explanationPolicy.mode` to `authored` is
`FS1.1`'s job once the fields those members point at exist.

Then, in order: **`FS1.2`** maps `SequenceProfile` and `PolicyProfile` onto `ExamVersion` with no
persistence types in Domain; **`FS1.4`** implements §5; **`FS7.1`** does §6; **`FS3.2`** reads
`partScore.reporting` for the catalogue's `raw` / `estimated-band` / `band` capability field;
**`FS3.5`** reads `practiceHistory.bandTrend`.

Blocked on the product owner, unchanged by this task: `H-8b`, `H-12`, `H-4`, `B-2`, `B-13`.
