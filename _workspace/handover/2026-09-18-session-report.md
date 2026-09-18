# Session handover — 2026-09-18

**For the next agent picking this repository up.** Written at the end of a
session that took `main` from `d9ec890` to `d68382a`: 20 commits, 123 files,
+5691 / −875.

Read `CLAUDE.md` first; it is still the canonical pointer and was updated three
times today. This file exists for the things a pointer file cannot hold: what
was wrong, why, and what is left.

---

## 1 · Start here if you do nothing else

**One item is diagnosed and unfixed.** `Foundation verification` is the last red
workflow, and the cause is precise:

`scripts/failure-drills.mjs`, drill `production-config-live` (line ~234), runs

```
docker compose -f infra/docker/compose.production.yaml run --rm --build --no-deps \
  -e Email__ClientBaseUrl=http://insecure.smoke.invalid api
```

and expects the startup gate to **refuse** — non-zero exit, output naming
`ClientBaseUrl`. **All mail infrastructure was removed on 2026-09-08**, so no
`Email` configuration is read by anything any more. The API therefore starts
normally, `docker compose run` holds the container in the foreground, and the
drill hangs. Observed: 05:46:07 → 06:34:26, forty-eight minutes, killed by the
job's `timeout-minutes: 60` in `.github/workflows/verify.yml:50`.

Two separate defects, and the second matters more:

1. **The drill asserts a gate that no longer exists.** It can never pass. Either
   retarget it at a refusal that is still real — `Jwt__SigningKey`,
   `Sso__ClientBaseUrl` or whatever `StartupConfiguration` still rejects for a
   plain-`http` external URL — or delete it and say why in the commit.
2. **No drill has its own timeout.** An inverted drill whose command never
   terminates consumes the whole job budget instead of failing. A per-drill
   deadline turns "the pipeline was cancelled after an hour" into "this drill
   did not exit", which is a message somebody can act on.

Fix (2) whichever way you resolve (1). It is the reason this took a session to
find rather than a minute.

---

## 2 · CI board

| Workflow | Start of session | Now |
|---|---|---|
| Backend | dead at "Start MinIO" | ✅ |
| Browser | 6 tests red | ✅ |
| Frontend | red | ✅ |
| Security | red | ✅ |
| Documentation checks | ✅ | ✅ |
| Images | ✅ | ✅ |
| Foundation verification | red | ❌ Windows ✅, Linux cancelled at 60 min — § 1 |

Local, on the tip: backend **1443/1443**, web **361/361**, admin **101/101**,
packages **58/58**, e2e **34/34** (desktop and mobile), typecheck / drift /
check-docs / format:check green.

---

## 3 · Production defects found, and what each one actually was

Everything in this section began as a red test that looked like a test problem.
Only one of them was.

### 3.1 A learner could be stranded mid-exam

`AdvanceSection` writes the section transition, saves it, **then** marked the
section being left — and `MarkSection` called the AI provider *inside the
request*. `Ai:*` HttpClient timeouts floor at 180 seconds. So pressing
"Tiếp theo" after Writing held the response while an essay was marked; a proxy,
a tab switch or a flaky connection turned that into `TaskCanceledException`, a
500, and a sitting the server had already advanced while the screen had not.
Pressing the button again could not help: the transition it would repeat was
done.

`MarkSection` now marks inline **only when nothing was enqueued**.
`MarkingWork.EnqueueAsync` returns `bool` so the caller can tell "queued" from
"no rubric, so nobody owes this". Reading and Listening still run inline — they
get no job, and that is how `AwaitingEvaluator` reaches the screen at all.
Nothing waits longer for a band: `WritingResults` already polls 8s × 40.

→ `8d0f22e`, tests in `ExamLifecycleTests`.

### 3.2 The e2e suite tested an interface no learner sees

The app resolves its locale from `navigator.languages`; Playwright's Chromium
says `en-US`. Every copy assertion in the suite was written as a
`/Kết quả|Results/` alternation to cope — which means **one half of each was
never taken**. That is how `exam.resultHeading` shipped as `'{skill} result'`
in English while a test waited for `Results` and nobody noticed.

`playwright.config.ts` now pins `locale: 'vi-VN'`. If you add an assertion on
copy, assert the Vietnamese.

### 3.3 `MediaBucket` was not pinned in a test factory

`ObjectStorageAppFactory` pins the service URL, both keys, two buckets, three
prefixes, the region and the path style — and not `MediaBucket`, which the CMS
media library added with a default. The readiness probe walks it, so the result
depended on whether `vni-media` happened to exist in whatever MinIO was
running: red on a clean stack, intermittent on a developer's. **That is why it
read as flakiness.**

The same gap in CI created two of eight buckets. Both fixed; a new test asserts
the probe walks media at all.

> **Operational hazard worth carrying forward:** any environment that deploys
> the media library without creating `vni-media` boots fine and then answers
> `ServiceUnavailable` for ever, because the startup gate creates no buckets.
> Create the bucket before deploying.

→ `101e3e7`, `cf3967e`.

### 3.4 The skip gate could not match an exemption written by hand

`check-test-skips.mjs` compared TRX `testName` attributes raw. A theory's
arguments arrive XML-escaped — `fixture: &quot;cam16-test-3.json&quot;` — so an
exemption written the obvious way never matched and the gate answered "skipped
test with no exemption" for an entry sitting right there. Correct about the
rule, misleading about the cause.

Decoded in the parser, because the allowlist is read by people and the TRX is
read by the script. → `7ceca8c`.

### 3.5 Two CI faults that were nothing to do with this repository

- **`minio/minio` and `minio/mc` stopped serving anonymous pulls on Docker
  Hub.** A cached image hid it completely on developer machines. Moved to
  `quay.io/minio/*`, MinIO's own registry. → `68d04ef`
- **PBM runs one operation at a time.** `pbm config --file` starts a storage
  resync and the `--set` calls that followed hit "another operation in
  progress". Retried on that exact message, bounded to 60s. → `7a26448`

---

## 4 · Owner decisions of 2026-09-18 — `Q-01`…`Q-04`

Recorded in `docs/requirements/confirmed.md`. All four are implemented.

| ID | Decision | Where it lives |
|---|---|---|
| `Q-01` | A three-skill mock **shows its overall band normally**, with a note naming the skills it covers | `ExamHandlers.OverallBand`, `SessionResultsView.OverallBandModules` |
| `Q-02` | **Admin resets a learner's password and that stays the only recovery path.** No SMS OTP in the MVP | `UserIdentity.MustChangePassword`, `RequireAuth`, `ForcedPasswordChangePage` |
| `Q-03` | **The papers already in use are published; the exposure is accepted.** Narrows `P-21` | `ContentRights:AllowPublicationWithoutProvenRights` |
| `Q-04` | **Keep the reseller key in use**; direct OpenAI pricing is not affordable. No code change | `api.vietapi.tech` stays the base URL |

**Three things about `Q-03` that must not be softened.** It is an *accepted
risk under `M-53`, not a licence*. The rule is untouched — `ContentRightsPolicy`
still refuses, the seed still throws if a source file tries to grant, and
`ContentRightsPublishTests` pins all of it with the switch explicitly off. Every
override is audited and announced at startup. **And there is still no way to
record a genuine grant**: nothing anywhere writes a `RightsProof`, so a real
licence today would have to be inserted by hand. The seam defers that work; it
does not do it.

`Q-01` did not weaken product law `L3`. The old gate conflated "a partial mean
moves under the learner who read it" (a statement about pending work) with "all
four skills" (a statement about IELTS). The rule is now *nothing is still
owed*: a skill is settled when it has a band **or** its marking status carries a
terminal code. Nothing names Speaking — when an ASR provider is chosen it stops
carrying one, starts producing a band, and a four-skill mock averages four with
no edit.

---

## 5 · Features built

| What | Notes |
|---|---|
| **CMS media library merged** | Came from the outside collaborator on a base 27 commits stale; merged, not rebased. Permissions 31 → 34 |
| **Listening transcript rendered** | The data had arrived since 2026-09-11 with nothing reading it. Also unbroke `pnpm typecheck`, red on `main` since that date |
| **Dictation attempts persisted** | `dictation_attempts`, append-only like the usage ledger. **What the learner typed is deliberately not stored** — a personal-data class with no retention decision (`G-11`) |
| **Overall band + forced password change** | `Q-01`, `Q-02` above |
| **WebP sniffing** | `media.ts` tested `RIFF` before `WEBP`; a WebP *is* a RIFF file, so every image uploaded came back `audio/wav` |

### Two configuration values with opposite fates, and the difference matters

- **Removed:** `ObjectStorage:SpeakingRecordingRetentionDays: 90`. `git log -S`
  puts it inside the S1–S8 slice commit whose message never mentions retention
  — a passenger, not a decision — and `SpeakingRecordingUpload` stamps it onto
  every recording as `retentionUntil`, which makes it a claim written into
  data. Removed under `G-11`. `Recordings:SweepEnabled` stays true and is
  unrelated: the sweeper reconciles orphaned uploads by age.
- **Added:** `ContentRights:AllowPublicationWithoutProvenRights: true`. An
  owner decision with a date and a source, defaulting to `false`, announced at
  startup, audited per use.

If you are about to add a configuration value, the test is which of these two
it resembles.

---

## 6 · Formatting

`pnpm format:check` was red over 122 files. **87 of them are not source.**
`fixtures/exams/*.json` and the assembled packages under `_workspace/` are
written by `scripts/assemble-*.py` with `json.dumps(indent=2)`, which always
expands a short array where Prettier always collapses one — formatting
`exam-1.json` alone rewrites 323 lines and the next assemble run puts them
back. `fixtures/ai/` is excluded for a different reason: those are verbatim
provider responses replayed by the evaluation tests, and a recording that has
been reformatted is no longer what came off the wire.

Both are now in `.prettierignore` with the reasoning. The 35 files that really
are source are formatted. → `727f4b1`

---

## 7 · Known gaps, in the order they will hurt

1. **Foundation drill hang** — § 1. Diagnosed, unfixed.
2. **No door to record a content-rights grant.** `VerifyContentSource` checks
   file integrity and is registered in DI with no endpoint calling it. Nothing
   writes a `RightsProof`.
3. **`/me` and the dictation endpoints declare no response schema in
   `contracts/openapi`.** Their shapes never reach `packages/api-client`, so
   both clients type them by hand. Pre-existing; it bit twice today.
4. **`cam16-test-3.json` and `cam19-test-4.json` were never assembled.** Absent
   from the repository, disk and `.gitignore`. The `InlineData` rows are kept
   with exemptions expiring **2026-11-30** so somebody decides.
5. **History list and results screen disagree about the overall band.**
   `SittingSummaryView.overallBand` still uses `SittingBand.Overall`, which
   demands four sections, and the history query loads no markings or jobs.
   Reconciling needs both in that query — watch for an N+1.
6. **Dictation catalogue is still `FixtureDictationCatalogue`.** Attempts
   persist; there is no authoring surface. These were always two problems.
7. **Five design-hook findings** in `apps/web/src/styles/{practice,dashboard,exam}.css`
   — side-tab borders and layout-property transitions. All predate this
   session. **Left untouched and unsuppressed**; the owner has not ruled.
8. **`secrets.develop.json` holds an OpenAI-shaped key.** Not in git. As
   `check-docs` says every run: excluding a key is not revoking it.

---

## 8 · Repository mechanics you will get wrong once

- **The collaborator mirror diverges silently.** `vni-ielts-ai-collab` sat 27
  commits behind while an outside developer built against it for a week;
  nothing warned anyone, because its `upstream/main` ref was stale.
  `sync-collab.sh` is fast-forward only and refuses a divergence by design. The
  recovery that worked: merge `local/main` into the clone's `main`, verify
  there, then fast-forward the source repo *from the clone* — the merge makes
  the old tip an ancestor, so the port back is conflict-free. **Sync whenever
  `main` moves, not when the collaborator asks.**
- **Two GitHub remotes, one history.** Pushing `main` is not syncing the
  mirror. Run `sync-collab.sh` after every push.
- **`backend.yml` now triggers on `ci/**` and `scripts/**`** because the job
  runs four scripts and reads `ci/test-skip-allowlist.json`. It did not, so a
  fix aimed at that job never ran it.
- **Local dev points at Cloudflare R2**, one bucket with prefixes (ADR-0016),
  configured in `secrets.develop.json` — not `appsettings.Development.json`. A
  new `ObjectStorage:*Bucket` default that nobody sets there breaks readiness
  locally while CI, which has no `ServiceUrl`, stays green. To run e2e locally:
  `ObjectStorage__MediaBucket=vni-ielts-ai-dev ObjectStorage__MediaPrefix=media/`.
- **`PHT1232`'s branches.** `feat/cms-admin-operations` and
  `feat/cms-lifecycle-rbac-audit` carry commits that are in **neither** `main`
  nor `integrate/cms-admin-with-main`, and they exist **only on the collab
  mirror** — they are gone from the main GitHub repo. Owner instruction
  2026-09-18: leave both alone. Do not delete the mirror's copies.

---

## 9 · How work was closed

Every fix here carries a test that was **watched failing first**, and for the
narrow ones the fix was removed again to confirm the test went red for the
stated reason. Where a test could not be made to fail — the two Application
tests that already passed against a throwing evaluator — that is said in the
commit rather than papered over, and the assertion was changed to one that
could fail.

Two things were deliberately *not* done, and both are the point:

- The e2e leak gate (`pre-submit … carry no keys, explanations or transcripts`)
  was **not** loosened to a value check. `PartView.Transcript` is omitted when
  null instead, and `ExamRunContractTests` was *strengthened* to match: absent,
  not present-and-null.
- `ContentRightsPolicy` was **not** edited to let borrowed papers through. The
  rule still refuses; a configured seam records that somebody accepted the
  refusal.
