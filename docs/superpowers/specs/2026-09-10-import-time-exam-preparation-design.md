# Import-time exam preparation — the CMS door, the answer-key cross-check, and prepared teaching content

**Status:** design, approved by the product owner 2026-09-10. Not implemented.
**Date:** 2026-09-10
**Owner decisions recorded here:** `IP-01`…`IP-06` (below). Obeys `P-10`, `P-13`, `P-18`, `P-19`, `P-20`, `P-21`, `G-11`.
**Implementation order:** Part A (Reading and Listening) is one plan and lands first. Part B (Writing) is a second plan. Speaking is out, per `P-02`.

---

## 1. The one sentence this design exists to enforce

> **Everything a learner reads after submitting is prepared at import time, reviewed by a person before publication, and merely displayed at results time.**

Nothing is generated while a learner waits. That is the owner's stated dissatisfaction on 2026-09-10:

> *"làm bài xong hiển thị kết quả và phần giải thích mình chỉ việc cho hiển thị lại chứ không phải là lúc đó user ấn giải thích rồi mới cho AI sinh text."*

---

## 2. Owner decisions, 2026-09-10

| Id | Decision |
|---|---|
| `IP-01` | **The marking criteria for Writing stay the fixed IELTS four.** AI does not invent a criterion set per task; it produces per-task *marking notes* that say how those four apply to this particular prompt |
| `IP-02` | **The upload is one ZIP.** Four skill folders; inside `reading/` and `listening/`, two further folders separate the paper from the answer key. The CMS offers a button that downloads the empty skeleton |
| `IP-03` | **A paper that disagrees with its answer key is reported on screen and cannot be approved.** The draft is still saved so an administrator can see what happened |
| `IP-04` | **Reading and Listening are finished end to end first.** Writing follows |
| `IP-05` | **Answer explanations are generated at import**, stored on the exam version, reviewed in the CMS, and displayed without a learner action |
| `IP-06` | **A model answer is prepared per Writing task** and shown to the learner after submission, alongside their own essay |

### What this does not change

`IP-01` is the one place where the design departs from the owner's literal words, and it was accepted after the reason was put: IELTS Writing criteria are fixed and public (Task 1 uses Task Achievement, Task 2 uses Task Response, both plus Coherence and Cohesion, Lexical Resource, Grammatical Range and Accuracy). A criterion set that changes per prompt makes two learners' band 6.5 incomparable, which is the only property a band has. It would also reverse `P-13` and `H-8a`, already settled.

---

## 3. What already exists, and must be reused rather than rewritten

The engine is largely built and has already been paid for in incidents. This design adds a door and two checking layers; it rewrites nothing in the list below.

| Component | What it already does |
|---|---|
| `ExamPackageArchiveInspector` | ZIP safety: magic bytes, entry caps, compression ratio, path canonicalisation, per-skill layout by top-level folder name |
| `ExamPackageImportPipeline` | Inspect, extract to sandbox, structured-or-parsed, validate, save a draft |
| `ExamImportWorkflow` | The single validation boundary for both ingestion routes |
| `AnswerKeyDocument.Parse` | Reads a supplier key document into `AnswerKeyEntry(First, Last, Raw)`; handles the numbered and the bare-positional formats found in VOL 9 |
| `AnswerKeyInjection.Apply` | Writes the key onto a parsed package by question number and refuses on coverage gaps and type mismatches |
| `FabricatedAnswerKeyGuard` | Refuses or strips answer keys the source could not have contained |
| `CanonicalExplanationWorkflow` | Generates explanations at import, caches them, writes them into the package, marks the draft for review |
| `ExplanationOutputValidator` | Rejects an explanation whose `correctAnswer` is not the key's answer, or whose evidence is not in the passage |
| `ImportReviewWorkflow` | Edit, resolve warning, checklist, approve, with `ImportReviewActor(CanEdit, CanReview, CanPublish)` |
| `ContentRightsPolicy` | Refuses `LearnerProduction` without `RightsProof` (`P-21`) |

**Two of these run only from the command line today.** `ProviderNeutralExamSourceParser`, backed by `OpenAiStructuredExamClient`, is wired in `backend/tools/Vni.Ielts.ExamImporter`. The HTTP path resolves `IExamSourceParser` to `UnconfiguredExamSourceParser`, which throws and becomes `AI_PARSER_UNAVAILABLE`. And `ImportReviewWorkflow.EnrichCanonicalExplanationsAsync` has no caller in the API at all.

---

# Part A — Reading and Listening

## A1. The package layout gains a role, and the CMS hands out the skeleton

### A1.1 Layout

```
package.zip
├── reading/
│   ├── de/              paper: passages and questions
│   └── dap-an/          answer key
├── listening/
│   ├── de/              paper: questions (and transcript, if supplied)
│   ├── dap-an/          answer key
│   └── audio/           recordings
├── writing/             task prompts (Part B)
└── speaking/            cue cards (stored, not marked — P-02)
```

Accepted folder names, case-insensitive: `de` · `paper` · `questions` for the paper, and `dap-an` · `dapan` · `key` · `answers` for the key.

**Unaccented spellings only — `de`, never `đề`.** A ZIP stores entry names either as CP437 or as UTF-8 depending on a per-entry flag that many Windows tools set wrongly, so an accented folder name arrives mangled often enough that matching on it would fail unpredictably. The skeleton in A1.3 ships the correct names, so an administrator never has to type one. A folder whose name matches neither list is reported as `LAYOUT_UNKNOWN_ENTRY`, which is a warning today and stays one.

**A file sitting directly under `reading/` with no role folder keeps today's meaning: it is paper.** Packages imported before this change do not break, and the single-`exam.json` structured route is untouched.

### A1.2 Where the classification happens

`ExamPackageArchiveInspector` reads `segments[0]` for the skill at line 191. The role reads `segments[1]` **in the same place, on `verdict.Path`** — which is the canonicalised path that `Examine` has already cleared of traversal, absolute prefixes, symlinks and non-regular entries.

> This is a security boundary. The role layer adds a *read* after the existing checks and changes none of them. A role must never be derived from a raw entry name, because a raw entry name is attacker-controlled and `../` is a legal substring of a folder label.

`PackageLayout` grows from `ExamModule → paths` to `ExamModule → SkillEntries(Paper, Key, Audio, Unassigned)`. `AcceptedEntries` and `PresentSkills` keep their meaning so nothing downstream needs to know about roles it does not care about.

### A1.3 The template button

`GET /api/v1/admin/import/template` returns a ZIP skeleton, permission `exam.submit`. Every folder holds a short `HUONG-DAN.txt` naming what goes in it and what formats are read, because a ZIP cannot carry a meaningful empty directory and an administrator opening the file needs to know what `dap-an/` expects.

---

## A2. The answer key never reaches the model

**This closes a hole that exists in the code today.** `ExamPackageImportPipeline` lines 113–125 concatenate *every* file under *every* skill folder into one text blob and send it to the parser. An answer key dropped into `reading/` is read by the model. That is precisely the configuration that failed on 2026-09-02, where a model given a paper produced forty answers for a paper containing none, five of them wrong, all forty passing schema validation.

New sequence, per skill:

1. **Paper files only** are extracted and concatenated under per-file headings, and sent to `IExamSourceParser`.
2. **Key files** are extracted separately and parsed by `AnswerKeyDocument.Parse`. The model never sees them.
3. If a key folder is present: `FabricatedAnswerKeyGuard.Strip` removes every answer the model wrote, then `AnswerKeyInjection.Apply` writes the real key on by question number.
4. If no key folder is present: today's behaviour stands. `FabricatedAnswerKeyGuard.Inspect` runs with `sourceIncludesAnswerKey: false` and its findings become warnings.

**What the layers in A3 do when there is no key folder.** They still run, against whatever answers the model wrote, and this is deliberate rather than incidental. A fabricated answer that contradicts its own question type, exceeds the paper's stated word limit, or cannot be found in the passage is caught by exactly the same checks — and the 2026-09-02 measurement says fabricated answers are the case most in need of catching, because every one of those forty passed schema validation. A package with no key therefore arrives carrying both the fabrication warning and whatever contradictions the layers found, which together are a far better description of its state than the fabrication warning alone.

The hard-coded `sourceIncludesAnswerKey: false` at line 167 becomes a read of whether the layout carried a key folder. **It is derived from the layout, never from the package** — inferring it from the presence of answer keys would make the check vacuous, which the guard's own remarks already say.

---

## A3. Cross-checking the paper against the key

There is no single check. There are six layers, four of which need no AI. The threat they exist for is **not** "one answer is wrong at random" — it is **"the key is misaligned from question N onward"**, because misalignment is contiguous and propagates through everything after it.

### Layer 1 — Counting *(built)*

A numbered key matches by number. A bare positional key advances a counter by each entry's own width, so `24-26. A, B, D` consumes three positions rather than one. Catches a short key, a long key, a heading misread as an answer, a folded range read as a single. **This is where alignment failures originate, and it is already fixed.**

### Layer 2 — Shape legality *(built)*

The paper declares each question's type; the type declares the legal answer set. True/False/Not Given accepts three values. A multiple-choice question accepts only option keys printed on that question. A matching question accepts only labels in its group's bank.

### Layer 3 — The paper's own stated rules *(new, deterministic)*

Three contradictions the paper states about itself and nothing currently checks:

| Check | Source of truth | Example failure |
|---|---|---|
| Mark count | `question.marks` | "Choose TWO letters" is worth 2; a key giving one letter contradicts it |
| Word limit | `question.constraints.maxWords` | "NO MORE THAN TWO WORDS" against a four-word key answer |
| Label reuse | the group's instruction | A no-reuse matching group whose key uses one label twice |

`AnswerMatcher.ExceedsWordLimit` already applies the word limit **to fail a learner's answer**. It has never been applied to the answer key. The system is currently stricter with learners than with the answers it marks them against.

### Layer 4 — Passage anchoring *(new, deterministic, the highest-value addition)*

For `completion`, `short-answer` and `labelling` — roughly half a Reading paper and most of a Listening paper — IELTS requires the answer to be words taken from the passage or recording. So:

**4a. Containment.** The answer must appear in the passage. Normalised with the same rules `AnswerMatcher` uses to mark a learner (case, punctuation, number forms), so a legitimate spelling variation is not reported as a defect. An answer that appears nowhere is one no learner could produce.

**4b. Order.** Within a `questionGroup`, answers appear in the passage in question order. A key shifted by one line makes the sequence of found positions go **backwards at the shift point**, which names the first bad question. When an answer string occurs several times in the passage, the check looks for *any* monotone assignment across occurrences and only reports when none exists; a common word therefore weakens this signal rather than producing a false alarm.

**4c. It distinguishes two different faults.** A few misses mean the key is wrong. A whole group missing means the *paper parse* is wrong — a truncated passage, the wrong passage, a bad OCR crop. The administrator's next action is completely different, and the report must say which.

### Layer 5 — Semantic verification *(mostly built, needs reporting)*

Only True/False/Not Given, Yes/No/Not Given, multiple-choice and matching remain. Their answers are not strings from the passage, so layers 1 to 4 cannot speak.

`CanonicalExplanationWorkflow` already sends the model the question, the passage and **the answer code read from the key**, and requires an explanation naming that answer with verbatim evidence. `ExplanationOutputValidator` refuses on `EXPLANATION_ANSWER_MISMATCH` when the model names a different answer. That refusal is a key-verification signal and is currently filed as a generic warning under `TranscriptAndEvidence`.

It gets its own finding code and its own line in the report.

> **The direction of writing is what keeps this safe.** The model may *dispute* an answer; it may never *write* one. A dispute is a flag for a person, never an edit.

**Its limit, stated so nobody over-trusts it:** the validator checks that a quote is *in* the passage, not that the quote *supports* the answer. A model can agree and cite something real but irrelevant. This raises the floor; it does not make a key certain.

### Layer 6 — The person, where cross-checking actually ends

See A5.

### What blocks, and what a person can clear

| Layer | Severity | Clearable? |
|---|---|---|
| 1 Counting | error | No |
| 2 Shape | error | No |
| 3 Paper's own rules | error | No |
| 4a Answer absent from passage | error | No |
| 4b Order goes backwards | warning | Yes, with a recorded reason |
| 5 Model disputes the answer | warning | Yes, with a recorded reason |

Layers 1 to 4a are contradictions found by code between two documents; there is no judgement to exercise, so there is no override. Layers 4b and 5 rest on a convention with rare exceptions and on a model's opinion; making either unclearable means one false alarm locks a paper forever and the only escape becomes weakening the check.

Warnings follow the `P-19` shape already in place: `ImportReviewWorkflow.ResolveWarningAsync` records who and why, audited as `WarningOverridden`.

**Errors need a new gate.** `ApproveAsync` today checks unresolved warnings and the checklist; it does not look at `draft.Findings` at all. It gains a refusal, `IMPORT_FINDINGS_BLOCKING`, when any finding carries severity `error`. This is `IP-03`: the draft is saved and visible, and it cannot be approved.

---

## A4. Explanations are generated at import

`ExamPackageImportPipeline` calls `EnrichCanonicalExplanationsAsync` after a successful key injection. Explanations land in `question.explanation` inside the package JSON, travel through review, and reach the learner as `canonicalExplanation` on `QuestionResultView` — which `AnswerReviewList` already prefers over an on-demand fetch.

The generation gate is unchanged: `policyProfile.explanation.mode` must be `ai-generated`, which the parse template sets. `mode: none` on a package means no explanations and no AI call, which is the correct behaviour for content whose rights are not cleared.

**The per-learner on-demand path stays** as a fallback for versions imported before this change. It is not removed; it simply stops being the normal case. Removing it would break every exam already published.

**Cost and time make this a background job.** A Cambridge Reading paper parse takes minutes and costs real money; forty explanations are forty more calls. Running that inside a POST times out and loses what was paid for.

- `POST /api/v1/admin/import/packages` returns `202` with a draft id and a job id.
- `GET /api/v1/admin/import/packages/{draftId}` reports stage and progress.
- The job shape mirrors the Writing marking job, which has run in production since 2026-09-03.

## A5. The review screen states its own coverage

The screen shows one row per question: number, type, the answer from the key, and **its anchor** — the highlighted position found in the passage for a completion question, the model's evidence quote for an inference question. A reviewer scanning forty anchored rows takes a few minutes rather than re-reading the paper.

Rows with no anchor are highlighted, because they are exactly the questions nothing has proved.

The panel must print a coverage statement, not a green tick:

```
34 câu có neo tự động, khớp.
 6 câu (14–19, True/False/Not Given) không có trạm kiểm soát nào phía sau
   trong Passage 2. Chưa có gì chứng minh. Đối chiếu tay với key gốc.
```

**Why this sentence is part of the design rather than a nicety.** Three residual cases survive every layer above: a run of unanchorable questions with no anchored question after it, a passage containing no anchorable question at all, and a single non-propagating error such as a typo in the official key. None can be eliminated. All three can be *named*, and a named list of six is work a person will actually do.

**And one limit no code reaches:** if the official key document is itself wrong, every layer here confirms it, because all of them check consistency between two files rather than truth in the world. Defences are a second source for the same paper or a person who knows the answer. Cross-source key comparison is left as a seam and is not built now.

## A6. What the learner sees

Unchanged in shape, changed in timing. `P-10` already puts the correct answer on screen after submission. The explanation now arrives with the results payload instead of being fetched when a row is opened. No button, no spinner, no per-learner AI call.

---

# Part B — Writing

Second plan. The design is settled here so it is not re-decided later.

## B1. Criteria are fixed; their application is not

The rubric artifact stays what `2026-09-08-writing-marking-rubric-v2-design.md` produced: four criteria per task, Task 1 under Task Achievement with an Academic and General Training split, Task 2 under Task Response. `DescriptorSource` remains VNI's own text. Every band keeps the "AI · tham khảo" label (`P-13`).

## B2. Per-task marking notes *(new)*

At import, for each Writing task, the model produces notes on how the four criteria apply to **this** prompt: the question type (discuss both views, to what extent, problem and solution), what a position must commit to, which chart features count as an overview, which comparisons a competent response must make, and the errors this prompt invites.

They are stored on the exam version, reviewed before publication, and injected into `WritingEvaluationPromptBuilder.SystemPrompt` as task context. They **cannot** add, rename or remove a criterion; `CriterionMarking.Mark` already rejects a key that is absent or extra, and that check is what keeps `IP-01` true in code rather than in prose.

## B3. Model answer *(new)*

A band 8–9 response per task, generated at import and shown to the learner after submission beside their own essay.

Two rules that are not optional:

1. **It is reviewed before publication like any other teaching material.** A model answer a learner studies teaches English to everyone who reads it. The same argument as the answer-key gate applies, and the `Draft → InReview → Approved → Published` lifecycle already exists to carry it.
2. **It is never sent to the marker.** A marker comparing an essay to one sample essay penalises every valid different approach. The model answer is a learner-facing artefact only.

## B4. What the learner sees

Their own essay, the task prompt, the four criterion bands with evidence, and the model answer. `SectionContentView.submissions` already carries the essay text; the prompt is already in `content`.

---

## 4. Schema changes — `contracts/schemas/exam.schema.json`

| Path | Change |
|---|---|
| `part.sampleAnswer` | New. Object: `text`, `targetBand`, `provenance`, `reviewedBy`. Writing only |
| `part.markingNotes` | New. Object keyed by criterion, values are strings. Writing only. Additional properties refused, so a criterion cannot be smuggled in |
| `part.rubricRef` | **Removed.** It exists in the schema and is read by nothing. Wiring it would let an uploaded package choose the standard it is marked against, which is `IP-01` defeated by a field rather than by a prompt. The rubric artifact stays operator-pinned through `Assessment:Writing:RubricArtifactPath` with a content hash. A field that looks live and is dead misleads the next reader, so it goes rather than lingering |
| `question.explanation` | Unchanged. Already the right shape |

`sampleAnswer` and `markingNotes` are additive and optional, so every existing package stays valid.

## 5. API changes

| Endpoint | Change |
|---|---|
| `GET /api/v1/admin/import/template` | New. Returns the skeleton ZIP. `exam.submit` |
| `POST /api/v1/admin/import/packages` | Returns `202` with draft id and job id instead of running inline |
| `GET /api/v1/admin/import/packages/{draftId}` | Reports job stage, progress, findings and per-question anchors |
| `POST …/{draftId}/approve` | New refusal `IMPORT_FINDINGS_BLOCKING` |

The OpenAPI contract is regenerated and `packages/api-client` with it; both are checked by the existing drift gate.

## 6. Deliberately not built

- **Speaking marking.** `P-02`. Recording, storage and playback only; `NoTranscriptSource` stays
- **Cross-source answer-key comparison.** A seam, not code
- **RAR, folder upload, loose files.** Unchanged from `S6`
- **Checkpointing inside the HTTP import.** `ImportBatchRunner` has one; wiring it here is a follow-up. A parse that fails at question 30 currently loses the whole call
- **Removing the on-demand explanation path.** It stays for versions already published

## 7. Risks

| Risk | Handling |
|---|---|
| `ExamPackageArchiveInspector` is a security boundary with tests against ZIP bombs and path escape | Role classification is a read placed after the existing checks, on the already-canonicalised path. No existing check is modified |
| Key document formats vary | `AnswerKeyDocument` reads the two formats met in real material. An unrecognised file is refused, never guessed |
| A model confabulates agreement in layer 5 | Reported as a floor, not a proof. The coverage statement in A5 never counts layer 5 as evidence |
| A paid parse fails mid-run | Accepted for now, named in section 6 |
| Layer 4b false alarms on repeated words | Monotone assignment across all occurrences; reported only when no assignment exists |

## 8. Definition of done

Every item closes with a test **verified to go red when the fix is removed**. A green suite is not evidence.

**Part A**

1. A ZIP with `reading/de/` and `reading/dap-an/` produces a draft whose questions carry the supplied key, and the parse request provably contains no key text
2. A key short by one line is refused, naming the first uncovered question
3. A key answer of four words against a two-word limit is refused
4. A completion answer absent from the passage is refused
5. A key shifted by one within a completion group produces a backwards-order warning naming the shift point
6. A draft carrying any blocking finding cannot be approved, and no override clears it
7. An order or dispute warning is cleared only with a recorded reason, audited
8. A published exam serves explanations in the results payload with no learner action and no AI call at results time
9. The review panel prints the coverage statement, and its unanchored count matches the rows it highlights
10. A zip bomb and a path-escape package are still refused, with the role layer in place

**Part B**

11. Marking notes reach the prompt and cannot alter the criterion set: a notes object naming a fifth criterion is refused at import
12. A model answer cannot be published without review
13. The marker's request provably contains no model-answer text
