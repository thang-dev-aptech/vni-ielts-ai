# Survey — exam engine current state (read-only)

Run: `fscore-20260829` · Phase FS0 · Produced by a read-only Explore agent, commissioned by the
orchestrator so that FS1/FS3/FS4 briefs extend working code instead of rebuilding it.

No files were modified. Items marked **[inferred]** were not read directly.

---

## 1. The headline: the plan overstates the FS1 gap

**`Question.Marks` already exists and works end to end.** It is live in the domain record
(`Question(..., int Marks = 1)`), in `Section.AutoScoredMarks` (`Questions.Where(IsAutoScored).Sum(q => q.Marks)`),
in `DeterministicScorer` (`available += question.Marks`), in `QuestionDocument.Marks`, in the Mongo
mappers, and in `contracts/schemas/exam.schema.json` line 300 with a written rationale.

So the plan's framing — _"một question có thể có nhiều slot/mark"_ — is **half-built already**. The
multi-**mark** half is done. What genuinely does not exist:

> **No concept of _which answer-sheet numbers_ a question occupies.** `Marks` is a scalar count.
> A 2-mark question is one `Question.Id`, one sheet key, one input box, all-or-nothing scoring.

The XML doc on `Marks` says part-marking _"needs an answer-key shape that does not exist"_ and tags it
`[OPEN QUESTION]`. That is precisely the `ResponseSlot` gap, and it is the whole of FS1.1/FS1.2.

**Consequence for the brief:** FS1 must be scoped as _add a slot keyspace beside the existing mark
count_, not _introduce multi-mark_. Rebuilding `Marks` would regress tested behaviour.

---

## 2. The answer-sheet protocol is load-bearing — extend it, never replace it

Answers do **not** live on `ExamSession`. They live behind `IAnswerSheetStore`, one sheet per
`(sessionId, module)`:

```csharp
AnswerSheet(IReadOnlyDictionary<string,string?> Answers,   // questionId -> value|null
            int Revision,
            IReadOnlyDictionary<string,long>? Sequences)   // questionId -> Lamport token
```

Already implemented and tested, and all of it is the FS4.7 "autosave/offline" checklist item:

- Writes are **patches keyed by question id**, never whole-sheet.
- `Sequences` is a per-question monotonic client counter; a stale entry is **ignored, not refused** —
  this is the out-of-order-save guarantee FS4's phase gate asks for.
- `Revision` is per-sheet and advisory; `PatchedSheet.PreviousRevision` tells a caller it was behind.
- `CloseAsync` freezes the sheet atomically **before** the session CAS; afterwards `PatchAsync` throws
  `SectionSheetClosedException`.
- Client side: `useAnswerSheet.ts` (1052 lines) with a 1200 ms debounce, exponential backoff, and
  `patchJournal.ts` (223 lines) — an IndexedDB journal (`vni.exam`/`patches`, keyed
  `sessionId|module|questionId`) that restores entries whose sequence beats the server's and re-arms
  the debounce.
- Covered by `AnswerSheetStoreTests` (~24), `patchJournal.test.ts` (8), and e2e `offline.spec.ts`,
  `races.spec.ts`.

**Every key in the sheet is a `Question.Id`.** Introducing slot numbers therefore forces a deliberate
choice — a second keyspace, or a value-shape change — and that choice is the riskiest decision in FS1.
It must be made explicitly by the domain owner, with the migration in FS1.3, and it must preserve the
Lamport/close semantics above.

`SaveAnswers` validates every incoming key against `section.Questions.Select(q => q.Id)` and rejects
unknown ids (`UnknownQuestionException`); `MaxAnswerCharacters = 60_000`.

Multi-value answers already ride in one string field: `AcceptedAnswer.All` +
`AnswerMatcher.MatchesSet` (splits on `'|' , ';' ' '`, `HashSet.SetEquals`) + the client's `"A|D"`
join. Contract-tested by `AnswerContractTests` (7).

---

## 3. Hard-coded Reading→Listening order — the FS7.1 target list

FS7.1 says the sequence must come from a versioned profile. These are the sites that hard-code it:

| Site                                                     | Constant                                                          |
| -------------------------------------------------------- | ----------------------------------------------------------------- |
| `Domain/Exams/ExamContent.cs:114`                         | `ExamVersion.FullTestOrder = [Reading, Listening, Writing, Speaking]` — drives `NextModuleAfter()`/`FirstModule()` |
| `Application/Exams/ExamViews.cs:330`                      | `SittingBand.FourSkills = [Listening, Reading, Writing, Speaking]` — **a different order**; harmless today because it is used as a set, but it will bite if ever read as a sequence |
| `apps/web/src/features/exam/skills.ts:~93`                | `SKILL_ORDER = ['reading','listening','writing','speaking']`      |
| `practiceCatalogue.ts` `toFullItems`                      | drops an exam unless every `SKILL_ORDER` module is present; orders parts by it |
| `ExamHandlers` / `SessionProjection.ToResults`            | uses `FullTestOrder.Count` (= 4) as the "all skills marked" test — this is also the FS7.4 overall-band gate |
| `e2e/tests/smoke.spec.ts`                                 | asserts _"a Full Test opens on Reading"_ — will need updating with FS7.1 |

---

## 4. What already exists — do not rebuild

1. **Multi-mark scoring** (§1).
2. **The full answer-sheet concurrency protocol**, client and server (§2).
3. **Question groups** — `QuestionGroup` repeated per question and grouped by `Id`; rendered by
   `QuestionList.chunk`, `SummaryGaps` (parses `___N___` markers and inlines inputs by order), and
   `takenBy` for matching banks (**shown, not enforced**).
4. **Question renderers**, all in `QuestionInput.tsx`: `true-false-notgiven`, `yes-no-notgiven`,
   `multiple-choice`, `matching`/`labelling`, `multiple-select`, `essay-task`, and a default text input
   covering completion/short-answer. `speaking-response` is handled by `SpeakingRecorder.tsx` (368).
   **FS4.6's real gap is the drag/drop + keyboard fallback, not the renderers themselves.**
5. **Package validation through a single gate** — `Infrastructure/Content/ExamPackageReader.cs` (321)
   with `ExamPackageResult(Accepted, Version, Findings)` and `ValidationFinding(Severity, Code, Path, Message)`.
6. **Both runners work.** `ExamRunnerPage` (947) is complete for mock: server-clock reconciliation,
   part navigation, autosave chip, `advance()` with an idempotency key, submit, expiry, results.
   `PracticeRunnerPage` (632) works for single-skill with stopwatch pause/resume and target time.
   **FS4.1's "runner shell" largely exists**; the real gaps are the slot-numbered footer (FS4.3) and
   drag/drop a11y.
7. **A client-side practice catalogue** — `PracticeItem` in `practiceCatalogue.ts` (330) is the closest
   thing to `PracticeUnit`, including a declared-but-unimplemented `FACET_SEAM = ['band','questionType','topic','difficulty']`.

## 5. Genuine gaps

- **No `ResponseSlot`** anywhere. `QuestionView.Order` is the only number the client has, and
  `refusedNumbers()` and the footer both render it as "the number on the paper".
- **No `PracticeUnit` server type, id, or projection** — the catalogue is derived in the browser from
  `GET /exams`, keyed `"${examVersionId}:${module|full}"`. FS3.1–FS3.3 are genuinely greenfield.
- **No package-driven sequence** — see §3.
- **No ZIP or `manifest.json` ingestion in C#.** `ExamPackageReader` consumes a single JSON document;
  `exam/Exam1/manifest.json` is parsed by nothing in the backend **[inferred]**. There is **no admin
  import/upload endpoint** — `AdminEndpoints` has publish/unpublish but no import. FS2.1/FS2.2 are
  greenfield.
- **No `ITranscriptSource` implementation** — the interface exists, unimplemented, which is the
  `AwaitingVoiceProvider` seam FS8.7 needs.

---

## 6. Contract mechanics FS1.5 must respect

`contracts/openapi/v1.json` is **generated, never hand-edited**. `OpenApiContractTests` compares the
running app's document to the committed file and, on mismatch, **writes the new document into the
working tree and then fails** — so the fix loop is "run, `git diff`, commit".

`packages/api-client` runs `openapi-typescript 7.9.1` into `src/generated/` (untracked).
The drift gate is `scripts/check-generated-drift.mjs` (`pnpm check:drift --mode=all`), run in
`.github/workflows/verify.yml:356`, guarding three links: running API ⇄ `v1.json` ⇄ `@vni/api-client` ⇄
the hand-written client types (via `contractParity.test.ts` + `pnpm typecheck`).

A schema transformer in `Api/Program.cs` exists solely to emit `string | null` inside
`SaveAnswersRequest.changes` (the erase-an-answer case) — **any slot-keyed patch DTO will need the same
treatment.**

---

## 7. Existing test coverage to protect (approximate counts)

Domain: `DeterministicScorerTests` 12, `AnswerMatcherTests` 12, `BandScoreTests` 11,
`AnswerContractTests` 7, `TimingProfileTests` 5, `StopwatchTests` 12, `ExamSessionTests` 11,
`CriterionMarkingTests` 15.

Application: `ExamLifecycleTests` 30, `SectionMarkingRunnerTests` 12, `SittingBandTests` 6,
`RecordingReconciliationTests` 4.

Integration (**mostly `[SkippableFact]`, needing Mongo/testcontainers**): `ExamRunContractTests` 26,
`AnswerSheetStoreTests` 24, `IdempotencyContractTests` 12, `SessionsTests` 11, `MarkingOutboxTests` 8,
`QueueBacklogTests` 6, `PublishedExamImmutabilityTests` 4, `ExamSessionTransitionTests` 4,
`FullSittingJourneyTests` 3, `OpenApiContractTests` 3.

Infrastructure: `ExamPackageReaderTests` 14, `AnswerKeyShapeTests` 3.
Architecture: `PersistenceRepresentationTests` 6, `PersistenceBoundaryTests` 4 — these enforce
CLAUDE.md rule 7 and will fail loudly if a slot type leaks a driver attribute into Domain.

Web (vitest): `exam-flow` 29, `practice-runner` 15, `practice-four-skills` 13,
`exam-speaking-contract` 6, `answer-integrity` 5, `patchJournal` 8, `contractParity` 3 (type-level).

E2E (Playwright): `smoke` 1, `offline` 2, `races` 2, `resilience` 2.
