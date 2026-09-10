# Answer-key cross-check — what the code does

Verified against the tree on **2026-09-10**. This page describes implementation, not a proposal.

The design is [`../superpowers/specs/2026-09-10-import-time-exam-preparation-design.md`](../superpowers/specs/2026-09-10-import-time-exam-preparation-design.md) § A2–A5. This page covers only Part A of that design — the six commits that landed the paper/key layout split and the deterministic checking layers. Part B (Writing marking notes, model answers), the CMS review panel, the background-job shape for `POST /api/v1/admin/import/packages`, and Layer 5's own finding code are a later plan and are not described here.

---

## Why this exists

Measured on 2026-09-02: a model shown a Reading paper with no answer key produced forty answers, five of them wrong, and all forty passed schema validation. Before this work, `ExamPackageImportPipeline` concatenated every file under every skill folder — paper and key alike — into one text blob and sent it to `IExamSourceParser`. An answer key dropped into `reading/` was read by the model along with the paper.

**The fix is not "ask the model more carefully." It is: stop sending the key to the model, read it with code, and check the two documents against each other.**

---

## The layout gains a role

Inside `reading/` and `listening/`, a second folder level says paper or key: `de` · `paper` · `questions` for the paper, `dap-an` · `dapan` · `key` · `answers` for the key — case-insensitive, unaccented spellings only (a ZIP stores entry names as CP437 or UTF-8 depending on a per-entry flag many Windows tools set wrongly, so an accented folder name arrives mangled unpredictably). A file with no role folder is still a paper, so packages imported before this change keep working.

The role is read from `verdict.Path` in `ExamPackageArchiveInspector` — the canonicalised path the existing security checks have already cleared of traversal, absolute prefixes, symlinks and non-regular entries. The role read is a security-boundary addition placed *after* those checks, never before them; a role is never derived from a raw, attacker-controlled entry name. `PackageLayout` carries the split as `EntriesBySkill: ExamModule → SkillEntries(Paper, Key, Audio)`; `PresentSkills` and `AcceptedEntries` keep their pre-existing meaning.

**Only paper-role files are concatenated and sent to `IExamSourceParser`.** Key files are extracted separately and parsed by `AnswerKeyDocument.Parse` — the model never sees them. This is `ExamPackageImportPipeline.ImportFromSandboxAsync`, which now walks `layout.For(skill).Paper` rather than every accepted entry.

**The key route is decided per skill, never per package.** Reading and Listening both number their questions 1 to 40, so a package-wide "a key was supplied" flag would apply Reading's key to Listening's questions, or silently skip Listening's fabrication guard because Reading carried a key. `ExamPackageImportPipeline.ApplyKeysAndGuardAsync` loops `layout.PresentSkills` and asks, for each one, whether *that skill's* `SkillEntries.Key` is non-empty — a fact read from the layout, never inferred from the package. A skill with a key folder gets its model-written answers stripped (`StripModelAnswers`) and the real key applied (`AnswerKeyInjection.Apply(json, entries, skill)`, scoped with the `ExamModule` parameter so it only touches that skill's questions). A skill with no key folder meets `FabricatedAnswerKeyGuard.Inspect(packageJson, sourceIncludesAnswerKey: false)` instead — every finding becomes an unresolved review warning (`P-19` shape), not a silent rejection.

---

## After keying, the draft is re-validated

Once a skill's package JSON changes, `ExamPackageImportPipeline.Revalidate` re-runs `IExamPackageValidator` against the updated JSON and, on success, replaces `draft.Version` with the freshly materialised `ExamVersion`. This keeps `PackageJson` (the source of truth for what was actually written) and `Version` (what `AdminImportEndpoints` reads today, and what a future consumer might) from disagreeing about an answer.

When re-validation fails, the draft is **not** left holding a stale, now-wrong `Version`. It records finding code `ANSWER_KEY_REVALIDATION_FAILED` (error) and leaves `Version` untouched at its previous value, with the finding telling a reader to read the package JSON rather than trust the version until the problem is fixed. This is the deliberate outcome of an unreadable key folder too: `StripModelAnswers` removes the model's guesses and nothing valid replaces them, so the schema legitimately refuses — no answer is judged safer than an invented one.

---

## The checking layers

There is no single check. Six layers are named in the design; four are deterministic code and ship in this plan. Layer 5 (semantic verification via the explanation pipeline) predates this plan and is unchanged by it — its refusal is still filed under the generic `TranscriptAndEvidence` review category rather than its own finding code, which is explicitly left for the next plan. Layer 6 is the person — see [Residual cases](#three-residual-cases-no-layer-covers) below.

| Layer | What it checks | Finding code(s) | Severity | Question types reached | Where |
|---|---|---|---|---|---|
| 1 — Counting | A numbered key matches by number; a bare positional key advances a counter by each entry's own width (`"24-26. A, B, D"` consumes three positions). Catches a short key, a long key, a heading misread as an answer | `ANSWER_KEY_COVERAGE` | error | Every question type — this is alignment, not content | `AnswerKeyInjection.Apply` |
| 2 — Shape legality | The question's own type constrains the legal answer set: True/False/Not Given accepts three values, multiple-choice only the option keys printed on that question, matching only labels in the group's bank | `ANSWER_KEY_TYPE_MISMATCH` (error); `ANSWER_KEY_TYPE_RETYPED`, `ANSWER_KEY_OPTION_ADDED`, `ANSWER_KEY_BANK_LABEL_ALTERNATIVES`, `ANSWER_KEY_FOLDED_CHOICE` (warning) | mixed | True/False/Not Given, Yes/No/Not Given, multiple-choice, matching, multiple-select, and any question printing options regardless of its declared type | `AnswerKeyInjection.Apply` |
| 3 — The paper's own stated rules | Three contradictions the paper states about itself: `question.marks` vs. the key's answer count; `question.constraints.maxWords` vs. the key's word count; the group's `eachLetterOnce` flag vs. a repeated label | `KEY_MARK_COUNT_MISMATCH`, `KEY_EXCEEDS_WORD_LIMIT`, `KEY_LABEL_REUSED` | error | Any question that declares `marks > 1`, a `maxWords` constraint, or belongs to a group with `eachLetterOnce` | `PaperKeyConsistency.Inspect` |
| 4 — Passage anchoring | 4a: the answer must appear in the passage (Reading) or transcript (Listening), normalised with the same rules `AnswerMatcher` uses to mark a learner. 4b: within a group, answers must appear in passage order — reported via a greedy earliest-legal-occurrence walk, so a repeated word does not false-alarm. 4c: a whole group with zero anchored answers is reported as a probable *paper* defect (wrong or truncated passage), distinct from a few missing answers, which point at the key | `KEY_ANSWER_NOT_IN_PASSAGE`, `PASSAGE_DOES_NOT_MATCH_QUESTIONS` (error); `KEY_ANSWERS_OUT_OF_PASSAGE_ORDER` (warning) | mixed | `completion`, `short-answer`, `labelling` only — roughly half a Reading paper and most of a Listening paper. Skipped entirely: True/False/Not Given, Yes/No/Not Given, multiple-choice, matching | `PassageAnchorCheck.Inspect` |
| 5 — Semantic verification | `CanonicalExplanationWorkflow` sends the model the question, the passage, and the answer code read from the key, and requires an explanation naming that answer with verbatim evidence. `ExplanationOutputValidator` refuses when the model names a different answer | `EXPLANATION_ANSWER_MISMATCH`, filed under review category `TranscriptAndEvidence` (not its own finding code yet) | warning | True/False/Not Given, Yes/No/Not Given, multiple-choice, matching — the types layers 1–4 cannot speak to, since their answers are not passage strings | `Explanations/ExplanationOutputValidator.cs` (pre-existing; unchanged by this plan) |
| 6 — The person | The CMS review panel, per-question anchors, the coverage statement | — | — | Everything the automated layers could not resolve | Not built by this plan — see A5 in the design |

Layer 5's own limit, stated in the design and worth repeating here: the validator checks that a quoted passage span is *in* the passage, not that it *supports* the answer. A model can agree and cite something real but irrelevant. It raises the floor; it does not make a key certain.

### What blocks, and what a person can clear

| Layer | Severity | Clearable? |
|---|---|---|
| 1 Counting | error | No |
| 2 Shape (`ANSWER_KEY_TYPE_MISMATCH`) | error | No |
| 3 Paper's own rules | error | No |
| 4a Answer absent from passage / whole-group mismatch | error | No |
| 4b Order goes backwards | warning | Yes, with a recorded reason |
| 5 Model disputes the answer | warning | Yes, with a recorded reason |

`ImportReviewWorkflow.ApproveAsync` refuses with `IMPORT_FINDINGS_BLOCKING` when `draft.Findings` contains any entry with `Severity == "error"` — checked before the existing unresolved-warning and checklist gates, and with **no override**: there is no equivalent of `ResolveWarningAsync` for a finding. An error here means two documents in the same package contradict each other; a key answer the passage does not contain, or an answer over the paper's own stated word limit is not a judgement call a reviewer's authority can settle — the fix is a corrected source file, re-uploaded. Warnings keep the `P-19` shape already in place: `ResolveWarningAsync` records who cleared it and why, audited as `WarningOverridden`.

Two further finding codes sit outside the six-layer table because they fire before or around it rather than as a layer of the cross-check itself: `ANSWER_KEY_UNREADABLE` (error — a key folder was supplied but `AnswerKeyDocument.Parse` found nothing it recognises; the model's answers are stripped regardless, since a key folder existing means they were never meant to stand) and `LAYOUT_UNKNOWN_ENTRY` (warning — a top-level or role folder name matches neither list; unchanged, pre-existing behaviour).

---

## Three residual cases no layer covers

Taken from `docs/superpowers/specs/2026-09-10-import-time-exam-preparation-design.md` § A5. These are **named, not eliminated** — no layer above closes them, and none is coming in this plan:

1. **A run of unanchorable questions with no anchored question after it.** Layer 4 can only place a question relative to passage positions found for other questions in its group. A trailing run of True/False/Not Given or multiple-choice questions with nothing anchorable after them has nothing to be checked against.
2. **A passage containing no anchorable question at all.** If every question in a part is True/False/Not Given or multiple-choice, layer 4 never runs against that passage — it has nothing to search for.
3. **A single non-propagating error, such as a typo in the official key.** The layers above exist for the failure mode that propagates — a shift that corrupts everything after it. An isolated, one-off wrong answer in an otherwise correctly aligned key produces no count mismatch, no shape violation, no rule contradiction, and (if the wrong answer still happens to appear in the passage) no anchor failure either.

The review screen's coverage statement (A5, not yet built) is designed to name these residual rows explicitly rather than imply full coverage with a green tick.

---

## The harder limit

Every layer in this document checks **consistency between two files** — the parsed paper and the supplied key — never **truth in the world**. If the official key document itself is wrong, every layer here confirms it: a correctly-transcribed wrong answer passes layer 1 (it's counted right), layer 2 (it's a legal value for the question's type), layer 3 (it doesn't violate the paper's own stated rules), and layer 4 (a wrong-but-plausible short answer can still appear in the passage). Nothing here is a check against ground truth. The only defences against a wrong official key are a second source for the same paper, or a person who knows the answer — cross-source key comparison is left as a seam and is not built.

---

## What this plan did not touch

- `docs/ai/writing-marking.md` — Writing marking is unrelated to this work; Part B of the design (marking notes, model answers) is a later plan.
- `docs/requirements/assumptions-and-open-questions.md` — nothing here closes an open question.
- Layer 5's own finding code, the CMS review panel (per-question anchors, the coverage statement), explanations generated at import (`IP-05`), and import as a background job (`202` + polling) — all named in the design as later work, not silently dropped.
