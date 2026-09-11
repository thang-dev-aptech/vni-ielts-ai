# Writing marking — what the code does

Verified against the tree on **2026-09-09**. This page describes implementation, not a proposal.

The design that produced the v2 artifact is [`../superpowers/specs/2026-09-08-writing-marking-rubric-v2-design.md`](../superpowers/specs/2026-09-08-writing-marking-rubric-v2-design.md). The JSON Schema is [`../../contracts/schemas/writing-evaluation.schema.json`](../../contracts/schemas/writing-evaluation.schema.json). Combined-band arithmetic stays in [`../domain/band-scoring.md`](../domain/band-scoring.md) (`P-12`).

---

## Operator pin vs what the pipeline can load

Two artifacts sit on disk. **Which one runs is configuration**, not a compile-time switch.

| Artifact | Version | Task 1 criterion | Task 2 criterion | Hash pin |
|---|---|---|---|---|
| `fixtures/assessment/writing-rubric-v1.json` | `ielts-writing-synthetic-v1` | `taskResponse` (same list as Task 2) | `taskResponse` | Pinned in `secrets.example.json` |
| `fixtures/assessment/writing-rubric-v2.json` | `vni-writing-v2` | `taskAchievement` (Academic / GT split) | `taskResponse` | `contentHash` is still `sha256:pending` — do not hash-pin until the operator computes one |

`secrets.example.json` and the develop secrets still point at **v1**. Pointing them at v2 is an operator choice; this document does not do it. Until that happens, `ConfiguredRubricSource` stamps a single Writing rubric whose criteria are `CriterionKeys.Writing` (`taskResponse` + CC · LR · GRA) for **both** tasks.

`WritingRubricLoader` walks relative paths from the working directory, the binary folder, then `fixtures/` — a `dotnet run --project` from a nested cwd still finds the file. The Docker image copies `backend/src/` only, so a production deploy that enables marking must ship or mount the artifact; otherwise load fails on the first essay.

---

## Three layers

`W-1`: the model judges; code holds the rails.

### 1. Admission — before any provider call

`WritingAdmission.Assess` in Domain. Empty Writing is **not** `NothingSubmitted` (a dash). Speaking empty still is.

| Remaining words after discounting copied prompt | Band | Advisory |
|---|---|---|
| 0 | 0 | `empty-or-not-english` |
| Not English (script / Vietnamese-diacritic heuristic) | 0 | `empty-or-not-english` |
| ≤ 20 | 1 | `under-20-words` |
| otherwise | proceed to the evaluator | — |

Copied prompt text is stripped before the count. Feedback language follows `Assessment:Writing:FeedbackLanguage` (default `vi`).

### 2. Evaluator — GPT or Gemini, never trusted as a result

`WritingSectionEvaluator` implements `ISectionEvaluator`. Application never names a vendor. Infrastructure clients: `OpenAiWritingEvaluationClient`, `GeminiWritingEvaluationClient`, selected by `WritingEvaluationRouter`.

- **Reseller / non-vendor `baseURL`:** `POST …/v1/chat/completions`. Measured live 2026-09-09 against `apithat.dev`.
- **Vendor OpenAI endpoint:** Responses API + JSON Schema. The live schema's `oneOf` (Task Achievement xor Task Response) is a known risk on that path — the reseller path does not send the schema to the provider.
- Prompt: `WritingEvaluationPromptBuilder`. Default voice is a Vietnamese examiner; criterion acronyms stay English (TA, TR, CC, LR, GRA); **evidence stays verbatim English** from the essay. Seam: `Assessment:Writing:FeedbackLanguage` (`vi` / `en`).
- Word count in the user turn is computed in code, not by the model.
- Per-call bound is the **named HttpClient Timeout**, not a CTS around the whole router (that used to cancel retries mid-flight as `TaskCanceledException`). Floor **180 s**, clamp **10…300**. `TimeoutSeconds` in a copied 120 s secret is therefore raised, not honoured as 120. → [`../development/nfr.md`](../development/nfr.md) FS9.3
- Router: `MaxAttempts` **3 per provider**, then optional `FallbackProvider`. Worker: **5** job attempts with backoff, then `Failed`.

`IsConfigured` is false unless `Assessment:WritingMarking:Enabled`, a primary provider with model + key, rubric version + descriptor source, and `AiEgress` allows the payload. Default `Enabled` is **false** in options; develop secrets turn it on.

### 3. Adjudication — after the model returns

1. JSON Schema (`WritingEvaluationValidator.EnsureSchemaValid`).
2. Exactly one of `taskAchievement` or `taskResponse`, plus CC · LR · GRA.
3. `CriterionMarking.Mark` against **the rubric for that task**: every key present, none extra, bands in the closed enum, `sectionBand` recomputed in code, evidence spans grounded in the submission (flag, not reject).
4. `WritingLimiters.Apply` — caps from **boolean flags**, never from English parsed out of the rationale. Notes-like bullets (`LooksLikeNotes`) and very short scripts (`wordCount < 50`) are set in code as `formatNotProse` / `insufficientSentenceControl`. Memorised-script → 0 is absent (`W-Q5`).
5. Provenance stamped: prompt version, provider section, model requested vs reported, mismatch flag, request id. A mismatch **flags**; it does not drop the band.

Advisories and provenance travel on `SectionMarking` → Mongo → `SectionMarkingView` (`advisories`, `provenance`). Additive; older rows without them still load.

---

## Output contract

The canonical schema is the file in `contracts/schemas/`. A Writing response is **either** Task Achievement **or** Task Response, never both:

```jsonc
"criteria": {
  "oneOf": [
    { "required": ["taskAchievement", "coherenceAndCohesion",
                   "lexicalResource", "grammaticalRangeAndAccuracy"],
      "not": { "required": ["taskResponse"] } },
    { "required": ["taskResponse", "coherenceAndCohesion",
                   "lexicalResource", "grammaticalRangeAndAccuracy"],
      "not": { "required": ["taskAchievement"] } }
  ]
}
```

Optional `limiters` booleans: `whollyUnrelated`, `entirelyOffTopic`, `simpleSentencesPredominate`, `gtBulletsOrTone`, `task2ParagraphingInadequate`, `academicTask1NoData`.

Bands are `enum`, never `minimum`/`maximum`. Never clamp. → [`output-contracts.md`](output-contracts.md)

`Assessment:Writing:CriterionGranularity` is `whole` (v2 default) or `half-step` (v1). Whole-band criterion scores are a seam because official descriptors exist only at whole bands (`G-11`).

---

## Combined Writing band

Server `writingBand` via `WritingTaskWeightPolicy` (`Assessment:Writing:TaskWeights` 1:2, `P-12`). The learner app **never** averages the two task cards. Missing a task → `writingBand: null`, reason `awaiting-tasks`. Unconfigured weights → `weighting-not-configured`. Every number on the Writing results screen carries **AI · tham khảo** (`P-13`).

---

## Learner results UI

`apps/web/src/features/exam/result/WritingResults.tsx` — Writing does not reuse the Reading 40-question chrome.

| Behaviour | Code |
|---|---|
| Poll while the job is `pending` / `running` / `retryable` | `MARKING_POLL_MS = 8000`, `MARKING_POLL_MAX = 40` |
| Failed stops polling | `isMarkingInFlight`; **Kiểm tra lại** only `load()`s results — it does **not** reopen a Failed outbox job |
| Criterion labels | `criterionLabels.ts` — `taskAchievement` / `taskResponse` are distinct; unknown keys fall through to the raw key |
| Paper review | prompt, image, submitted text, word count vs 150 / 250 |
| Single Skill | never auto-advances (`E-11`…`E-13`) |

---

## Known mismatch while v1 is pinned

Observed 2026-09-09 on a live sitting: both tasks returned HTTP 200 from the reseller; Task 2 (`taskResponse`) persisted; Task 1 was rejected because the model emitted `taskAchievement` and the loaded rubric still required `taskResponse`. The Vietnamese system prompt names both TA and TR even when the artifact is v1. Schema `oneOf` accepts either key; `CriterionMarking` then demands an exact match to the rubric.

That is current behaviour, not a silent remap. Switching the operator pin to a hash-verified v2 artifact is what makes Task 1's construct `taskAchievement` in Domain as well as in the prompt.

Cloudflare **HTTP 524** (~125 s) on the reseller is a provider/origin timeout, not the client 180 s floor. Worker retries; five exhausted attempts become `Failed`.

---

## Configuration seams

| Key | Default in code | Role |
|---|---|---|
| `Assessment:WritingMarking:Enabled` | `false` | Feature flag. A key in the environment does not turn marking on by itself |
| `Assessment:WritingMarking:PrimaryProvider` | none | `OpenAi` or `Gemini` |
| `Assessment:WritingMarking:FallbackProvider` | `null` | Optional |
| `Assessment:WritingMarking:TimeoutSeconds` | 180 | Clamped 10…300; HttpClient **floors at 180** |
| `Assessment:WritingMarking:MaxAttempts` | 3 | Per provider |
| `Assessment:WritingMarking:RubricArtifactPath` / `RubricContentHash` | v1 in the example secrets | Which scale the prompt and Domain share |
| `Assessment:Writing:TaskWeights` | 1 : 2 in `appsettings.json` | Combined band (`P-12`) |
| `Assessment:Writing:FeedbackLanguage` | `vi` | Learner-facing feedback language |
| `Assessment:Writing:CriterionGranularity` | `whole` | Whole-band vs half-step at criterion level |
| `Ai:AllowCrossBorderTransfer` | `false` | Egress gate (`B-2`) |
