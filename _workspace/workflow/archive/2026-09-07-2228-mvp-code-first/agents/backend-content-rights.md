# FS0.1 — Content rights registry

Run `fscore-20260829` · agent: backend-engineer · baseline `35bf37ce9b459222036710a6770541ec3d26d829`
Branch `feat/foundation-and-learner-auth`. **Nothing committed, nothing pushed.**

---

## 1. What was built

A per-source rights record, and a gate on the publish endpoint that refuses an exam whose
source does not hold a `learner-production` right.

**The registry's shape** — `ContentSource` in `Vni.Ielts.Domain/Content/ContentRights.cs`:

| Field | Notes |
|---|---|
| `Id` (`ContentSourceId`) | A lowercase slug a human chose. Refuses anything path-shaped — the VOL 9 folder carries a Google Drive export stamp, and an id derived from it would move on re-export |
| `Title`, `Owner` | `Owner` is `null` when nobody has established one. `null` is not shorthand for VNI |
| `Proof` (`RightsProof?`) | Licence reference + reviewer + review date. `null` = nothing recorded |
| `AllowedEnvironments` | `fixture` · `internal-review` · `learner-production` |
| `ExpiresAt` | Evaluated against the server clock at decision time, never cached at import |
| `RootPath`, `Files` | Repo-relative paths + optional SHA-256 + size. Paths and hashes, never bytes |
| `BoundExamVersionIds`, `BoundExamDefinitionIds` | How a publish request finds its rights |

**The rule is one pure function**, `ContentRightsPolicy.Evaluate(ContentSource?, environment, now)`.
It takes `null` as a valid input and answers "no" — so the empty state is a parameter rather than an
early return every caller has to remember.

Four denial reasons: `NoRegistryEntry`, `EnvironmentNotGranted`, `RightExpired`, `ProofMissing`.

**Two independent fail-closed guards on the unproven grant.** `ContentSource.Register` throws
`UnprovenPublishRightException` if `learner-production` is granted with no `Proof`; `Evaluate` refuses
such a grant even when it arrives from storage (a hand edit, an older writer). `Rehydrate` deliberately
does *not* throw — a corrupt row costs one refusal rather than taking the whole listing down.

**Binding is by definition id as well as version id.** `DevelopmentExamSeeder` derives an exam
*version* id from a content fingerprint, so correcting a typo mints a new one; a registry keyed only on
version ids would silently lose track of its own material on any edit.

### Seeded entries — 21, every one `fixture` only

Cambridge IELTS 16–21 (6) · VOL 9 tests 1–8 (8) · Writing criteria PDFs (3) · Speaking criteria PDFs (2)
· `exam1` · `synthetic-full-1`.

- **Nothing holds `learner-production`.** `M-53` is open, so the seam is wired, tested and empty.
- **No entry carries a `Proof`** — no licence has been established for anything.
- VOL 9 filenames are recorded **exactly as they are on disk**, misspellings included:
  `KET TEST 2-R.docx`, the directory `KEY - EXPLAINATION`, and `TEST 1 -R.docx` with its stray space.
  Tidying them would produce permanent false "missing" reports.
- `exam1` carries the **six SHA-256 hashes transcribed from `exam/Exam1/manifest.json`** — the only
  per-file content hashes anywhere in this project. The manifest lives inside the gitignored folder,
  so without this copy they vanish from a clean checkout.
- `synthetic-full-1` is registered because it is the only exam a clean checkout has; leaving the
  project's own fixture unregistered would make the refusal look like a bug.

### How publish refusal is enforced

`AdminEndpoints.PublishEndpoint` calls `ContentPublishGuard.MayPublishToLearnersAsync(version, ct)`
**before** the already-published check — "you may not publish this at all" outranks "you already did",
and a version published before the registry existed must not hide its rights problem behind a status
message.

Refusal is `409` with `code = CONTENT_RIGHT_MISSING`, plus `reason`
(`no-registry-entry` / `environment-not-granted` / `right-expired` / `proof-missing`) and `sourceId`
extensions. One stable code, because branching on the distinction would be branching on a policy
`M-53` has not settled.

A read surface was added — `GET /api/v1/admin/content-sources`, behind `package.read` — because a
refusal an operator cannot investigate reads as a bug. It carries a derived `mayReachLearners` (false
for everything) and a stated note that a source with no entry holds no rights.

### Storage

New Mongo collection `content_sources`, two multikey indexes (`ix_content_sources_exam_version`,
`ix_content_sources_exam_definition`). Seeding is **insert-if-absent, never upsert**: a grant is a
reviewer's act, and a restart that rewrote records would silently revoke one the day `M-53` is
answered. An unseeded registry is safe on its own — no record means no rights.

Environments are stored **by name**. A bare enum serialises as its ordinal, and inserting a member
above `LearnerProduction` would reinterpret every stored fixture-only record as a publication right
with no error at any point.

---

## 2. Files changed

**New — Domain**
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Domain\Content\ContentRights.cs`

**New — Application**
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Application\Content\Ports.cs`
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Application\Content\ContentPublishGuard.cs`
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Application\Content\VerifyContentSource.cs`
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Application\Content\ContentErrorCodes.cs`

**New — Infrastructure**
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Infrastructure\Content\ContentRightsSeed.cs`
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Infrastructure\Content\FileSystemContentProbe.cs`
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Infrastructure\Content\ContentRightsRegistration.cs`
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Infrastructure\Persistence\Content\Documents.cs`
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Infrastructure\Persistence\Content\Mappers.cs`
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Infrastructure\Persistence\Content\MongoContentRightsRegistry.cs`

**Modified**
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Infrastructure\Persistence\MongoContext.cs` — `ContentSources` collection + 2 indexes
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Api\Endpoints\AdminEndpoints.cs` — publish gate, `GET /content-sources`, two `Describe` helpers
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\src\Vni.Ielts.Api\Program.cs` — `AddContentRights(...)`, `SeedContentRightsAsync()`
- `c:\Users\ADMIN\Documents\vni-ielts-ai\contracts\openapi\v1.json` — **regenerated**, +25 lines, one new path
- `c:\Users\ADMIN\Documents\vni-ielts-ai\docs\database\migration-plan.md` — `content_sources` row + "twelve" → "thirteen" (boundary widened by the coordinator mid-task)

**New — tests**
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\tests\Vni.Ielts.Domain.Tests\Content\ContentRightsTests.cs`
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\tests\Vni.Ielts.Application.Tests\Content\ContentPublishGuardTests.cs`
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\tests\Vni.Ielts.Infrastructure.Tests\Content\ContentRightsSeedTests.cs`
- `c:\Users\ADMIN\Documents\vni-ielts-ai\backend\tests\Vni.Ielts.Integration.Tests\ContentRightsPublishTests.cs`

Nothing outside the (widened) boundary was touched. `contracts/schemas/**`, `scripts/**`,
`package.json`, `StartupConfiguration.cs` and every `appsettings*.json` are untouched by me — the
modifications visible on `AiOptions.cs`, `ObjectStorage.cs`, `StartupConfiguration.cs`, `scripts/`,
`package.json` and `contracts/schemas/exam.schema.json` are concurrent agents' work.

---

## 3. Commands and exit codes

All run from `c:\Users\ADMIN\Documents\vni-ielts-ai\backend` unless stated.

| Command | Exit | Result |
|---|---|---|
| `dotnet build -v q --nologo` | 0 | 0 warnings, 0 errors |
| `VNI_REQUIRE_MONGO=1 dotnet test --nologo` (whole solution) | 0 | see counts below |
| `node scripts/check-docs.mjs` (from repo root) | **0** | `13 collections, all described in migration-plan.md` · `All documentation checks passed.` |
| `node scripts/check-generated-drift.mjs --mode=all` (repo root) | 0 | `api-client` reproduces byte-identically; no drift |
| `git diff --check` (repo root) | 0 | only pre-existing CRLF advisories |

Per the coordinator's warning I did **not** rely on `pnpm check`. Mongo was live
(`vni-mongo`, `127.0.0.1:27018`, healthy) and `VNI_REQUIRE_MONGO=1` was set, which turns an absent
replica set into a failed run rather than a skipped suite.

### Test counts — measured, not copied

| Project | Passed | Failed | Skipped |
|---|---|---|---|
| `Vni.Ielts.Domain.Tests` | 189 | 0 | 0 |
| `Vni.Ielts.Application.Tests` | 178 | 0 | 0 |
| `Vni.Ielts.Architecture.Tests` | 10 | 0 | 0 |
| `Vni.Ielts.Infrastructure.Tests` | 108 | 0 | 0 |
| `Vni.Ielts.Worker.Tests` | 13 | 0 | 0 |
| `Vni.Ielts.Integration.Tests` | 188 | 0 | 0 |

**Added by FS0.1 — 61 cases**, counted by namespace filter:

- Domain `…Domain.Tests.Content` → 32
- Application `…Application.Tests.Content` → 8
- Infrastructure `…Infrastructure.Tests.Content` → 29 total, of which **15 are new**
  (14 are the pre-existing `ExamPackageReaderTests`, same namespace)
- Integration `ContentRightsPublishTests` → 6

**Zero skips, including the conditional one.** `RecordedContentIntegrityTests` hashes the real
`exam/Exam1` assets and ran here because this working tree has the material —
`Passed … The_recorded_Exam1_hashes_match_the_files_that_are_actually_there [242 ms]`. That is
independent confirmation that the six hashes were transcribed from the manifest correctly.

On a clean checkout it will skip, and **loudly**: it prints why, and `VNI_REQUIRE_CONTENT=1` turns the
skip into an `Assert.Fail` — the same pattern `VNI_REQUIRE_MONGO` uses. It is the only conditional
skip added.

---

## 4. Negative proofs — red, then green

### 4.1 Publish refusal (the required proof)

Removed the guard block from `AdminEndpoints.PublishEndpoint` (replaced with `_ = rights;`) and re-ran
`VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Integration.Tests --filter "…ContentRightsPublishTests"` — exit **1**:

```
  Failed …ContentRightsPublishTests.An_exam_whose_source_has_no_registry_entry_is_refused_at_publish [6 s]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: Conflict
Actual:   OK
  Failed …ContentRightsPublishTests.An_expired_grant_stops_publishing_again [342 ms]
   Assert.Equal() Failure: Values differ  Expected: Conflict  Actual: OK
  Failed …ContentRightsPublishTests.An_imported_fixture_only_source_is_refused_at_publish [349 ms]
   Assert.Equal() Failure: Values differ  Expected: Conflict  Actual: OK
```

`A_source_that_holds_the_right_publishes` stayed green throughout, which is the point: it shows the
gate is a gate and not a constant refusal.

Restored from backup; re-ran → `Passed! - Failed: 0, Passed: 6, Skipped: 0`, exit **0**.

### 4.2 A source with no registry entry is refused, not allowed

Patched `ContentRightsPolicy.Evaluate` so `source is null` returned `Permit`. Exit **1**:

```
  Failed …Domain.Tests.Content.ContentRightsTests.An_unregistered_source_is_refused_for_every_environment_including_fixture
  Failed …Domain.Tests.Content.ContentRightsTests.A_source_with_no_registry_entry_has_no_rights_at_all
  Failed …Application.Tests.Content.ContentPublishGuardTests.An_exam_no_registry_entry_covers_is_refused
```

Restored; 189 / 178 green.

### 4.3 A changed content hash is detected

Patched `ContentIntegrity.Compare` so the SHA-256 comparison always yielded `Matches`. Exit **1**:

```
  Failed …Domain.Tests.Content.ContentRightsTests.A_file_whose_content_hash_changed_is_reported_as_changed [17 ms]
  Failed …Infrastructure.Tests.Content.FileSystemContentProbeTests
            .A_file_whose_bytes_changed_is_detected_against_its_recorded_hash [25 ms]
```

The Infrastructure case is end-to-end over a **real file** — write, hash, verify clean, overwrite,
re-hash, detect — in a temp directory, so it runs identically in CI where the gitignored material is
absent. Restored; green.

The working tree was restored after every proof and the full solution re-run green afterwards.

---

## 5. Artifacts

- `contracts/openapi/v1.json` — regenerated by `OpenApiContractTests` (run → `git diff` → re-run).
  Diff is exactly one added path, `/api/v1/admin/content-sources`, with 200/401/429 and bearer
  security. `OpenApiContractTests` now passes 3/3; `check-generated-drift --mode=all` is clean.
- `docs/database/migration-plan.md` — `content_sources` row added, describing the array→`text[]`/join-table
  decision and why environments must stay strings rather than ordinals. `check-docs.mjs` exit 0.

---

## 6. Requested changes outside my boundary

1. **`backend/src/Vni.Ielts.Application/Common/ErrorCodes.cs`** — move
   `ContentErrorCodes.ContentRightMissing` (`"CONTENT_RIGHT_MISSING"`) in beside every other code and
   delete `Application/Content/ContentErrorCodes.cs`. Two lists of error codes will drift. The string
   value must not change; clients branch on it.
2. **`backend/src/Vni.Ielts.Infrastructure/DependencyInjection.cs`** — move the body of
   `ContentRightsRegistration.AddContentRights` into `AddInfrastructure`. Splitting DI across two entry
   points is how a service ends up registered in the API and missing in the Worker. Today the Worker
   does not resolve `IContentRightsRegistry` and does not need to; that will stop being true.
3. **`appsettings*.json`** — optional key `Content:RootPath`, default absent. It points the file probe
   at a checkout that actually holds the gitignored material. Absent → working directory → every file
   reports `Missing`, which is correct and honest for a container.
4. **A `content.rights.grant` permission key** (`Domain/Identity/PermissionKeys.cs`) once `M-53` is
   answered and grants are made through the CMS rather than through the registry port. The read route
   currently borrows `package.read`, which is a reasonable fit for provenance but not for granting.

---

## 7. Risks

- **`GET /api/v1/admin/content-sources` returns `rootPath` and file paths.** That is provenance, not
  learner data, and it is behind `package.read` — but it does disclose where third-party material sits
  on a server. No file contents, no hashes of learner work, nothing logged.
- **The seed's paths are transcribed by hand.** Only `exam1`'s six hashes are verified against real
  files today. Every other path will report `Missing` until FS0.2's inventory confirms it, and a wrong
  path is indistinguishable from an absent file. FS0.2 should cross-check the seed's paths against what
  it actually finds.
- **The VOL 9 root path embeds a Google Drive export stamp** (`-20260819T082203Z-1-001`). The
  `sourceId`s do not, so a re-export moves the paths and not the rights records — but the paths will
  need updating by hand.
- **Publish now refuses everything** that is not explicitly granted, including exams published before
  this change if someone unpublishes and re-publishes them. That is the intended behaviour and it is a
  behaviour change to a live endpoint. The `reason` extension and the read route exist so an operator
  can see why.
- **No CMS screen grants a right yet.** The only ways to grant one are the registry port or a direct
  database write. Acceptable while `M-53` is open — a grant should be hard — but it is a gap the moment
  the answer arrives.
- `Vni.Ielts.Worker` does not register the registry. Nothing in the worker publishes, so this is not a
  hole today.

---

## 8. Next dependency

- **FS0.2 (content inventory script)** — should reconcile its findings against
  `ContentRightsSeed.Sources`: same paths, same anomalies, and it can fill in the `sha256` values the
  seed leaves `null`. `ContentFileRef` already refuses a malformed hash and reports an unhashed file as
  `NotHashed` rather than as a pass.
- **FS2 (import pipeline)** — an importer must call `RegisterIfAbsentAsync` (or bind an
  `examDefinitionId` onto an existing source) for anything it creates, or the resulting exam is
  unpublishable by construction. That is the safe default, but it will read as a bug if the importer
  does not say so.
- **`M-53`** stays open and unresolved by this task. Nothing here invented a default.
