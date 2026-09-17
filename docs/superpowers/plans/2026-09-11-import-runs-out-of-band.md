# Import runs out of band Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An operator uploads a raw exam ZIP through the CMS, the import runs in a background job that survives a restart, and what comes back is a draft whose paper is parsed, whose key is applied by code, whose Listening audio has a transcript, and whose Reading and Listening explanations are already written.

**Architecture:** The HTTP door stops doing the work. It stores the upload privately, enqueues a job, and returns `202`. A worker claims the job under a lease and runs the existing pipeline stage by stage, recording where it got to. Three stages are new: an AI parser wired behind a configuration gate, audio transcription that defers to any transcript the package already carries, and the explanation pass that already exists in code but has never had a caller.

**Tech Stack:** .NET 10, MongoDB, xUnit. The job follows `MarkingOutbox`, which already solves this exact shape — paid AI work, in another process, that must survive a restart.

**Spec:** [`../specs/2026-09-10-import-time-exam-preparation-design.md`](../specs/2026-09-10-import-time-exam-preparation-design.md) — Part A, sections A1.3, A2, A4, plus the audio-transcription addition the owner made on 2026-09-11 (below).

**Predecessor:** [`2026-09-10-answer-key-cross-check.md`](2026-09-10-answer-key-cross-check.md), merged to `main` as `cfccf58`. It built the checking layers this plan feeds.

## Global Constraints

- **The answer key never reaches a model.** Only paper-role files are concatenated into the parser's input; the key is read by `AnswerKeyDocument.Parse`. This is already true — do not undo it. → the 2026-09-02 measurement, where a model given a keyless paper produced forty answers, five wrong, all schema-valid.
- **A transcript is exam content.** It must not reach a client before or during a sitting. Only after `status != inprogress`. → `ExamRunContractTests`
- **Reading and Listening bands come from the answer key, never from a model.** A transcript may explain an answer; it can never change one. → CLAUDE.md rule 9
- **No AI provider type in the Domain layer.** Vendor SDKs live in Infrastructure adapters behind a port. → CLAUDE.md rule 5, ADR-0005
- **AI credentials never enter this repository.** Configuration only; a PreToolUse hook blocks writes to `.env*`. → CLAUDE.md rule 6
- **Nothing logs or echoes an answer key, a passage, a transcript, or learner text.**
- **Severity strings are the lowercase literals** `"error"` and `"warning"`.
- **Every item closes with a test verified to go red when the fix is removed.** A green suite is not evidence.
- Run `dotnet test backend/Vni.Ielts.sln --nologo`. One Integration test fails on `main` and is not yours: `SecretContractTests.The_speaking_retention_seams_are_unset` asserts the retention seams are unset while `appsettings.json` has pinned 90 since before this work. Two more need a running MinIO. If the Docker-backed health tests fail with `bind: address already in use`, clear orphaned containers with `docker rm $(docker ps -aq --filter name=vni-fault-mongo)` — that is an environment collision, not a regression.

## Owner decision added 2026-09-11

> *"phần audio sẽ được AI convert thành text rồi từ text mới so đáp án bổ sung thêm phần này trong quá trình parse đề"*

Recorded as `IP-07`…`IP-09` in `docs/requirements/confirmed.md` by Task 5:

| Id | Decision |
|---|---|
| `IP-07` | **Listening audio is transcribed during import**, and the answer key is anchored against that transcript |
| `IP-08` | **A transcript already in the package wins; AI is called only when one is missing.** Same principle as the answer key: where an authoritative source exists, a model does not guess |
| `IP-09` | **The transcript is shown to the learner after submission**, alongside the correct answer and the explanation |

**Why this is not blocked by `P-02`.** `P-02` defers Speaking marking because no provider is selected for **learner speech** — personal data under PDPL, needing word-level timings for pronunciation. This is published exam audio: not personal data, and a readable transcript is all it needs. OpenAI and Gemini are both already-approved providers and both transcribe, so no new provider decision is required. `NoTranscriptSource` and the Speaking null evaluator are untouched.

**Measured, and the reason this matters:** 0 of 24 Listening parts in the repository's Cambridge packages carry a transcript. `PassageAnchorCheck` therefore skips every Listening part, so the strongest deterministic check does not protect Listening at all today.

**The risk, and why it degrades safely.** An AI transcript will be wrong in places, and Listening answers are numbers, names and terms — the tokens transcription misses most. A wrong transcript makes the anchor check report a correct answer as absent. Yesterday's ruling F-6 already dropped `KEY_ANSWER_NOT_IN_PASSAGE` from a hard error to a blocking-but-clearable warning, so this surfaces as something a reviewer clears rather than a wall.

---

### Task 1: The import job, and what it is allowed to forget

**Files:**
- Create: `backend/src/Vni.Ielts.Application/Importing/ImportOutbox.cs`
- Test: `backend/tests/Vni.Ielts.Application.Tests/Importing/ImportJobTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ImportJobStage`, `ImportJobState`, `ImportJob`, `IImportOutbox`. Tasks 2, 3, 4, 5 and 6 all use them.

**Why a job and not a background task.** A Cambridge parse costs real money and takes minutes; transcription adds more; forty explanations add more again. If the API restarts mid-import — and this machine's own notes record that it does — an in-process task loses the work and the money with it, and the draft is stranded with no way to tell. `MarkingOutbox` already solved this shape and its remarks record why each part is there. Follow it; do not invent a second pattern.

- [ ] **Step 1: Write the failing test**

```csharp
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Importing;

public sealed class ImportJobTests
{
    /// <summary>
    /// Re-uploading the same bytes for the same version must not buy a second
    /// parse. The id is derived from what the work IS, never generated, so a
    /// retried upload collides with the job already running.
    /// </summary>
    [Fact]
    public void The_operation_id_is_derived_from_the_work_not_generated()
    {
        var a = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "abc123");
        var b = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "abc123");

        Assert.Equal(a, b);
    }

    [Fact]
    public void A_different_upload_for_the_same_version_is_a_different_job()
    {
        var a = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "abc123");
        var b = ImportJob.OperationIdFor(new ExamDefinitionId("cam-16"), 1, "def456");

        Assert.NotEqual(a, b);
    }

    /// <summary>
    /// A stage is how far the money went. Restarting a job that already paid
    /// for a parse must not pay again, so the stage has to be ordered and
    /// recorded, not inferred from what happens to be on the draft.
    /// </summary>
    [Fact]
    public void Stages_advance_in_one_direction()
    {
        Assert.True(ImportJobStage.Extracting < ImportJobStage.Parsing);
        Assert.True(ImportJobStage.Parsing < ImportJobStage.Transcribing);
        Assert.True(ImportJobStage.Transcribing < ImportJobStage.Keying);
        Assert.True(ImportJobStage.Keying < ImportJobStage.Checking);
        Assert.True(ImportJobStage.Checking < ImportJobStage.Explaining);
        Assert.True(ImportJobStage.Explaining < ImportJobStage.Done);
    }

    [Fact]
    public void A_job_out_of_attempts_is_failed_rather_than_retried_forever()
    {
        var job = ImportJob.New(
            new ExamDefinitionId("cam-16"), 1, "abc123", "uploads/abc123.zip", DateTimeOffset.UtcNow);

        var exhausted = job with { Attempts = ImportJob.MaxAttempts };

        Assert.False(exhausted.MayRetry);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/Vni.Ielts.Application.Tests --nologo --filter "FullyQualifiedName~ImportJobTests"`
Expected: FAIL to compile — `ImportJob` does not exist.

- [ ] **Step 3: Write the job**

```csharp
using System.Security.Cryptography;
using System.Text;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// How far an import got, in the order the money is spent.
///
/// <b>Recorded rather than inferred.</b> A worker resuming a job needs to know
/// whether a paid parse already happened; reading that off the draft would
/// guess, and guessing wrong buys a second parse of a Cambridge paper.
/// </summary>
public enum ImportJobStage
{
    Extracting = 0,
    Parsing = 1,
    Transcribing = 2,
    Keying = 3,
    Checking = 4,
    Explaining = 5,
    Done = 6,
}

/// <summary>Mirrors <see cref="Vni.Ielts.Application.Assessment.MarkingJobState"/> deliberately.</summary>
public enum ImportJobState { Pending, Running, Retryable, Failed, Completed }

public sealed record ImportJob(
    string OperationId,
    ExamDefinitionId DefinitionId,
    int VersionNumber,
    string SourceSha256,
    /// <summary>Where the uploaded archive was put, so another process can read it.</summary>
    string ArchiveKey,
    Guid? DraftId,
    ImportJobStage Stage,
    ImportJobState State,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? LeaseUntil,
    string? LeaseToken,
    string? LastError,
    DateTimeOffset? CompletedAt,
    string? Traceparent)
{
    public const int MaxAttempts = 3;

    public bool MayRetry => Attempts < MaxAttempts;

    /// <summary>
    /// <c>{definitionId}:{version}:{sourceHash}</c>. The hash is in it so a
    /// corrected package for the same version is a different job, and a
    /// retried upload of identical bytes is the same one. A unique index on
    /// this field is what makes the enqueue idempotent in the database rather
    /// than in a caller's memory.
    /// </summary>
    public static string OperationIdFor(
        ExamDefinitionId definitionId, int versionNumber, string sourceSha256) =>
        $"{definitionId.Value}:{versionNumber}:{sourceSha256.ToLowerInvariant()}";

    public static ImportJob New(
        ExamDefinitionId definitionId, int versionNumber, string sourceSha256,
        string archiveKey, DateTimeOffset now, string? traceparent = null) =>
        new(OperationIdFor(definitionId, versionNumber, sourceSha256),
            definitionId, versionNumber, sourceSha256, archiveKey, null,
            ImportJobStage.Extracting, ImportJobState.Pending, 0, now,
            now, null, null, null, null, traceparent);
}

public interface IImportOutbox
{
    /// <summary>Idempotent on the operation id. False means the job was already there.</summary>
    Task<bool> EnqueueAsync(ImportJob job, CancellationToken ct);

    /// <summary>Takes one due job atomically, setting a lease. Null when none is due.</summary>
    Task<ImportJob?> ClaimAsync(string leaseToken, TimeSpan lease, CancellationToken ct);

    Task RenewAsync(string operationId, string leaseToken, TimeSpan lease, CancellationToken ct);

    Task AdvanceAsync(
        string operationId, string leaseToken, ImportJobStage stage, Guid? draftId,
        CancellationToken ct);

    Task CompleteAsync(string operationId, string leaseToken, CancellationToken ct);

    /// <summary>Releases the lease and schedules a retry, or fails the job when out of attempts.</summary>
    Task FailAsync(string operationId, string leaseToken, string error, CancellationToken ct);

    Task<ImportJob?> FindAsync(string operationId, CancellationToken ct);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/tests/Vni.Ielts.Application.Tests --nologo --filter "FullyQualifiedName~ImportJobTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Verify the idempotency test goes red when the fix is removed**

Temporarily change `OperationIdFor` to return `Guid.NewGuid().ToString()`, re-run, confirm `The_operation_id_is_derived_from_the_work_not_generated` fails. Restore. Record the message.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Vni.Ielts.Application/Importing/ImportOutbox.cs \
        backend/tests/Vni.Ielts.Application.Tests/Importing/ImportJobTests.cs
git commit -m "feat(import): an import is a job that survives a restart"
```

---

### Task 2: The job's store

**Files:**
- Create: `backend/src/Vni.Ielts.Infrastructure/Persistence/Importing/MongoImportOutbox.cs`
- Modify: `backend/src/Vni.Ielts.Infrastructure/Persistence/MongoContext.cs` (collection + index)
- Modify: `backend/src/Vni.Ielts.Infrastructure/DependencyInjection.cs`
- Modify: `docs/database/migration-plan.md` (the docs gate counts collections)
- Test: `backend/tests/Vni.Ielts.Infrastructure.Tests/Persistence/MongoImportOutboxTests.cs`

**Interfaces:**
- Consumes: `IImportOutbox` and `ImportJob` from Task 1.
- Produces: a registered `IImportOutbox`. Task 3 injects it.

**Copy the claim from `MongoMarkingOutbox`.** Read it first. `ClaimAsync` must be ONE `FindOneAndUpdate` — finding a due job and then updating it is two statements and two workers will take the same job between them. The lease bounds *death*, not duration, which is why `RenewAsync` exists.

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>Two workers racing for one job: exactly one may win.</summary>
[Fact]
public async Task Only_one_worker_can_claim_a_job()
{
    var job = ImportJob.New(new ExamDefinitionId("cam-16"), 1, "abc", "k", Now);
    await outbox.EnqueueAsync(job, default);

    var first = await outbox.ClaimAsync("worker-a", TimeSpan.FromMinutes(5), default);
    var second = await outbox.ClaimAsync("worker-b", TimeSpan.FromMinutes(5), default);

    Assert.NotNull(first);
    Assert.Null(second);
}

/// <summary>The unique index, not the caller, is what makes a retried upload harmless.</summary>
[Fact]
public async Task Enqueuing_the_same_upload_twice_creates_one_job()
{
    var job = ImportJob.New(new ExamDefinitionId("cam-16"), 1, "abc", "k", Now);

    Assert.True(await outbox.EnqueueAsync(job, default));
    Assert.False(await outbox.EnqueueAsync(job, default));
}

/// <summary>A worker that died mid-import must not hold the job forever.</summary>
[Fact]
public async Task An_expired_lease_lets_another_worker_take_over()
{
    var job = ImportJob.New(new ExamDefinitionId("cam-16"), 1, "abc", "k", Now);
    await outbox.EnqueueAsync(job, default);
    await outbox.ClaimAsync("dead-worker", TimeSpan.FromMilliseconds(1), default);

    await Task.Delay(50);

    Assert.NotNull(await outbox.ClaimAsync("live-worker", TimeSpan.FromMinutes(5), default));
}

/// <summary>
/// A stage already reached is money already spent. A resumed job must not
/// walk backwards into a paid stage.
/// </summary>
[Fact]
public async Task A_recorded_stage_survives_a_retry()
{
    var job = ImportJob.New(new ExamDefinitionId("cam-16"), 1, "abc", "k", Now);
    await outbox.EnqueueAsync(job, default);
    var claimed = await outbox.ClaimAsync("w", TimeSpan.FromMinutes(5), default);
    await outbox.AdvanceAsync(claimed!.OperationId, "w", ImportJobStage.Keying, Guid.NewGuid(), default);
    await outbox.FailAsync(claimed.OperationId, "w", "transient", default);

    var again = await outbox.FindAsync(claimed.OperationId, default);

    Assert.Equal(ImportJobStage.Keying, again!.Stage);
}
```

Follow the existing Mongo test fixture in `MongoMarkingOutboxTests` for `outbox` and `Now`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test backend/tests/Vni.Ielts.Infrastructure.Tests --nologo --filter "FullyQualifiedName~MongoImportOutboxTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Write the store**

Mirror `MongoMarkingOutbox`: a document with `[BsonElement]` mappings, a unique index on `operationId` created in `MongoContext`, and `ClaimAsync` as a single `FindOneAndUpdateAsync` filtering on `state in (Pending, Retryable)` and `nextAttemptAt <= now` and (`leaseUntil == null` or `leaseUntil < now`), sorted by `nextAttemptAt`, setting `state = Running`, `leaseToken`, `leaseUntil` and `$inc: attempts`.

`FailAsync` sets `Retryable` with a backoff when `MayRetry`, `Failed` otherwise, and clears the lease in both cases. It must NOT reset `stage`.

- [ ] **Step 4: Register it, and record the collection**

Add to `DependencyInjection`. Add the new collection to `docs/database/migration-plan.md` — `scripts/check-docs.mjs` counts collections and fails on an undescribed one.

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test backend/tests/Vni.Ielts.Infrastructure.Tests --nologo --filter "FullyQualifiedName~MongoImportOutbox"`
Expected: PASS, 4 tests.

- [ ] **Step 6: Verify the claim test goes red when the fix is removed**

Temporarily split `ClaimAsync` into a `Find` followed by an `UpdateOne`, re-run, confirm `Only_one_worker_can_claim_a_job` fails with both claims succeeding. Restore. This is the proof that matters in this task.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Vni.Ielts.Infrastructure/Persistence/Importing/MongoImportOutbox.cs \
        backend/src/Vni.Ielts.Infrastructure/Persistence/MongoContext.cs \
        backend/src/Vni.Ielts.Infrastructure/DependencyInjection.cs \
        docs/database/migration-plan.md \
        backend/tests/Vni.Ielts.Infrastructure.Tests/Persistence/MongoImportOutboxTests.cs
git commit -m "feat(import): a job store whose claim is one statement"
```

---

### Task 3: The door stops doing the work

**Files:**
- Modify: `backend/src/Vni.Ielts.Api/Endpoints/AdminImportEndpoints.cs`
- Create: `backend/src/Vni.Ielts.Worker/ImportWorker.cs`
- Modify: `backend/src/Vni.Ielts.Worker/Program.cs`
- Modify: `backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs` (stage callbacks)
- Test: `backend/tests/Vni.Ielts.Integration.Tests/AdminImportEndpointsTests.cs`, `backend/tests/Vni.Ielts.Worker.Tests/ImportWorkerTests.cs`

**Interfaces:**
- Consumes: `IImportOutbox` from Tasks 1–2; `ExamPackageImportPipeline` as it stands.
- Produces: `IImportArchiveStore` (new, see below); `POST /api/v1/admin/import/packages` returning `202` with `{ operationId }`; `GET /api/v1/admin/import/jobs/{operationId}` returning stage, state, attempts, `draftId` and `lastError`.

**The thing the spec did not say, and it is load-bearing.** The API and the Worker are different processes. The uploaded ZIP exists only in the API's request; the worker cannot see it. So the API must persist the archive before enqueuing, and the job carries its key.

**Do NOT use `IPrivateImportAssetStore` for this, however much its name invites it.** That port is write-only — `PutPrivateAsync` and nothing else — so a worker could never read the archive back. Worse, the implementation registered by default is `DiscardedImportAssetStore`, which keeps nothing and returns the string `"discarded:{key}"`. Building on it would produce a system where every upload is accepted, enqueued, and then lost, with the failure appearing minutes later in a worker. It exists so `SafeSourceDocumentExtractor` has somewhere to put embedded media it does not want; that is a different job.

**Write a dedicated port instead.** `IImportArchiveStore` in `Vni.Ielts.Application.Importing`:

```csharp
/// <summary>
/// The uploaded archive, parked where another process can fetch it.
///
/// <b>Separate from `IPrivateImportAssetStore` on purpose.</b> That port is
/// write-only and its default implementation discards what it is given — it
/// exists so the document extractor has somewhere to drop embedded media.
/// An import archive has to survive the request that carried it and be read
/// back by a worker minutes later, which is the same shape as `IRecordingStore`
/// and is backed by the same object storage.
/// </summary>
public interface IImportArchiveStore
{
    Task<string> SaveAsync(string sourceSha256, Stream archive, CancellationToken ct);

    /// <summary>Null when the object is gone — a job whose archive expired must fail, not throw.</summary>
    Task<Stream?> OpenAsync(string archiveKey, CancellationToken ct);

    Task DeleteAsync(string archiveKey, CancellationToken ct);
}
```

Back it with the S3-compatible storage that already serves `IRecordingStore`, under its own key prefix. Object storage here is one bucket with prefixes (owner decision 2026-09-04, ADR-0016), so add a prefix rather than a bucket.

**Delete the archive when the job reaches `Done` or `Failed`.** An uploaded exam package is third-party copyrighted material; keeping every upload forever is a rights exposure nobody chose.

- [ ] **Step 1: Write the failing tests**

```csharp
/// <summary>
/// A Cambridge parse takes minutes and costs money. Doing it inside the POST
/// times out and loses what was paid for.
/// </summary>
[Fact]
public async Task Uploading_a_package_returns_202_and_an_operation_id()
{
    var response = await client.PostAsync("/api/v1/admin/import/packages", MultipartOf(ValidPackage()));

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<ImportAcceptedView>();
    Assert.False(string.IsNullOrWhiteSpace(body!.OperationId));
}

/// <summary>
/// The upload must survive the request that carried it, and be readable by
/// another process. Asserting it can be OPENED is the point — a store that
/// accepts bytes and keeps none would pass a weaker assertion.
/// </summary>
[Fact]
public async Task The_archive_is_stored_readably_before_the_job_is_enqueued()
{
    var bytes = ValidPackage();
    var accepted = await Upload(bytes);

    var job = await outbox.FindAsync(accepted.OperationId, default);
    Assert.False(string.IsNullOrWhiteSpace(job!.ArchiveKey));

    await using var read = await archives.OpenAsync(job.ArchiveKey, default);
    Assert.NotNull(read);
    Assert.Equal(bytes.Length, ReadAll(read!).Length);
}

[Fact]
public async Task Re_uploading_identical_bytes_does_not_start_a_second_job()
{
    var bytes = ValidPackage();
    var first = await Upload(bytes);
    var second = await Upload(bytes);

    Assert.Equal(first.OperationId, second.OperationId);
}

[Fact]
public async Task The_job_endpoint_reports_the_stage_it_reached()
{
    var accepted = await Upload(ValidPackage());
    await worker.RunOnceAsync(default);

    var status = await client.GetFromJsonAsync<ImportJobView>(
        $"/api/v1/admin/import/jobs/{accepted.OperationId}");

    Assert.Equal("Done", status!.Stage);
    Assert.NotNull(status.DraftId);
}
```

And in `ImportWorkerTests`:

```csharp
/// <summary>
/// The whole reason this is a job. A worker that dies mid-import must leave
/// the work owed, not lost.
/// </summary>
[Fact]
public async Task A_job_abandoned_mid_import_is_picked_up_again()
{
    await outbox.EnqueueAsync(JobFor(ArchiveThatParses()), default);
    await outbox.ClaimAsync("dead", TimeSpan.FromMilliseconds(1), default);
    await Task.Delay(50);

    await worker.RunOnceAsync(default);

    var job = await outbox.FindAsync(TheOnlyJob(), default);
    Assert.Equal(ImportJobState.Completed, job!.State);
}

/// <summary>An import that fails for a reason no retry fixes must not spin.</summary>
[Fact]
public async Task A_job_out_of_attempts_lands_in_failed_with_its_reason()
{
    await outbox.EnqueueAsync(JobFor(ArchiveThatCannotParse()), default);

    for (var i = 0; i < ImportJob.MaxAttempts; i++) await worker.RunOnceAsync(default);

    var job = await outbox.FindAsync(TheOnlyJob(), default);
    Assert.Equal(ImportJobState.Failed, job!.State);
    Assert.False(string.IsNullOrWhiteSpace(job.LastError));
}
```

- [ ] **Step 2: Run to verify they fail**

Expected: FAIL — the endpoint returns `200` with a draft today, and neither the worker nor the job endpoint exists.

- [ ] **Step 3: Change the endpoint**

`UploadPackageEndpoint` keeps its multipart handling, its body-size raise and its permission check. Then instead of calling the pipeline:

1. Hash the archive bytes (`SHA256`) — that hash is the job's identity.
2. `IImportArchiveStore.SaveAsync` the bytes, keyed by that hash.
3. `EnqueueAsync` an `ImportJob`, carrying the current `traceparent` so the worker's span joins the upload's trace.
4. Return `202` with `{ operationId }` and a `Location` header pointing at the job endpoint.

Add `GET /api/v1/admin/import/jobs/{operationId}`, permission `exam.submit`, returning stage, state, attempts, `draftId` and `lastError`.

- [ ] **Step 4: Write the worker**

`ImportWorker : BackgroundService`, modelled on `MarkingWorker`. A loop that claims, renews its lease while working, opens the archive from private storage, runs `ExamPackageImportPipeline.ImportAsync`, advances the stage as the pipeline reports it, and completes. On exception: `FailAsync` with a message that names no key, passage or learner text.

Give the pipeline an optional `IProgress<ImportJobStage>` so the worker can record where it is. Default null, so every existing caller is unaffected.

Register with `AddHostedService<ImportWorker>()`.

- [ ] **Step 5: Delete the archive when the job settles**

On `Done` or `Failed`, remove the stored archive. A failure to delete is logged, never fatal — a stray object is a hygiene problem, a lost import is not.

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test backend/tests/Vni.Ielts.Integration.Tests --nologo --filter "FullyQualifiedName~AdminImportEndpointsTests"` and `dotnet test backend/tests/Vni.Ielts.Worker.Tests --nologo`
Expected: PASS.

- [ ] **Step 7: Verify the survival test goes red when the fix is removed**

Temporarily have the worker run the pipeline without claiming a lease, re-run `A_job_abandoned_mid_import_is_picked_up_again`, confirm it fails. Restore. Record the message.

- [ ] **Step 8: Regenerate the contract and commit**

```bash
pnpm run generate:api-client && node scripts/check-generated-drift.mjs --mode=all
git add backend/src/Vni.Ielts.Api backend/src/Vni.Ielts.Worker \
        backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs \
        contracts/openapi/v1.json backend/tests
git commit -m "feat(import): the door enqueues and a worker imports"
```

---

### Task 4: An AI parser, behind a gate

**Files:**
- Modify: `backend/src/Vni.Ielts.Infrastructure/DependencyInjection.cs:375-384`
- Create: `backend/src/Vni.Ielts.Infrastructure/Ai/Importing/ExamParserOptions.cs` (configuration binding)
- Modify: `backend/src/Vni.Ielts.Api/secrets.example.json`
- Test: `backend/tests/Vni.Ielts.Integration.Tests/SecretContractTests.cs`, `backend/tests/Vni.Ielts.Infrastructure.Tests/Content/Import/ExamSourceParserWiringTests.cs`

**Interfaces:**
- Consumes: `IStructuredExamAiClient`, `ProviderNeutralExamSourceParser`, `ExamParserOptions` — all built and proven by the operator CLI.
- Produces: a configured `IExamSourceParser`. Nothing downstream changes shape.

**Reuse, do not rewrite.** `backend/tools/Vni.Ielts.ExamImporter/Program.cs:76-83,187` already wires `OpenAiStructuredExamClient` and `ProviderNeutralExamSourceParser`. Copy that wiring into the API's DI; write no new parser.

**The gate is the point.** With no provider configured, `UnconfiguredExamSourceParser` stays and a raw upload fails with `AI_PARSER_UNAVAILABLE` — today's behaviour, which is correct for a deployment nobody has given keys to. Configuration is what turns it on, never a code default.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void With_no_parser_configured_the_unconfigured_parser_stays()
{
    var services = Build(config: new Dictionary<string, string?>());

    Assert.IsType<UnconfiguredExamSourceParser>(services.GetRequiredService<IExamSourceParser>());
}

[Fact]
public void A_configured_provider_and_model_wires_the_real_parser()
{
    var services = Build(new Dictionary<string, string?>
    {
        ["Import:Parser:Provider"] = "OpenAi",
        ["Import:Parser:Model"] = "gpt-5",
        ["Import:Parser:ApiKey"] = "test-key",
    });

    Assert.IsType<ProviderNeutralExamSourceParser>(services.GetRequiredService<IExamSourceParser>());
}

/// <summary>
/// Half-configured is worse than unconfigured: it looks enabled and fails at
/// the first upload, after the operator has already put a package in.
/// </summary>
[Fact]
public void A_provider_without_a_key_is_refused_at_startup_not_at_first_use()
{
    var config = new Dictionary<string, string?>
    {
        ["Import:Parser:Provider"] = "OpenAi",
        ["Import:Parser:Model"] = "gpt-5",
    };

    var refusal = Assert.Throws<InvalidOperationException>(() => Validate(Build(config)));
    Assert.Contains("Import:Parser", refusal.Message, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run to verify they fail**

Expected: FAIL — the second test gets `UnconfiguredExamSourceParser`, the third throws nothing.

- [ ] **Step 3: Bind the options and wire the parser**

Bind `Import:Parser` to `Provider`, `Model`, `ApiKey`, `BaseUrl?`, `MaxAttempts` (default 2, range 1–3), `PromptVersion` (default `ExamSourceParsePrompt.Version`). Register the HttpClient and `IStructuredExamAiClient` the way the CLI does. Choose the parser by whether the options are complete.

Add the keys to `secrets.example.json` with empty values and a comment saying the door stays shut until they are filled. **Do not put a key in any file in this repository.**

- [ ] **Step 4: Add the startup gate**

Extend the existing startup configuration validation so a partly-filled `Import:Parser` section refuses to boot, naming the section.

- [ ] **Step 5: Run to verify they pass, then verify red-when-removed**

Remove the completeness check so any `Provider` value wires the real parser; confirm `A_provider_without_a_key_is_refused_at_startup_not_at_first_use` fails. Restore. Record the message.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Vni.Ielts.Infrastructure backend/src/Vni.Ielts.Api/secrets.example.json backend/tests
git commit -m "feat(import): wire the AI parser behind a configuration gate"
```

---

### Task 5: Listening audio becomes text, unless text was supplied

**Files:**
- Create: `backend/src/Vni.Ielts.Application/Importing/TranscriptionPorts.cs`
- Create: `backend/src/Vni.Ielts.Application/Importing/AudioTranscriptionStage.cs`
- Create: `backend/src/Vni.Ielts.Infrastructure/Ai/Importing/OpenAiAudioTranscriber.cs`
- Create: `backend/src/Vni.Ielts.Infrastructure/Ai/Importing/UnconfiguredAudioTranscriber.cs`
- Modify: `backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs`
- Modify: `docs/requirements/confirmed.md` (`IP-07`…`IP-09`)
- Test: `backend/tests/Vni.Ielts.Application.Tests/Importing/AudioTranscriptionStageTests.cs`

**Interfaces:**
- Consumes: `PackageLayout.For(module).Audio` — the Audio role already exists. The archive sandbox path from Task 3's worker.
- Produces: `IAudioTranscriber`, and `part.transcript` populated on the package before Keying. Task 6's explanation pass and the existing `PassageAnchorCheck` both read it.

**Why this stage exists, measured:** 0 of 24 Listening parts in the repository's Cambridge packages carry a transcript, so `PassageAnchorCheck` skips every Listening part and the strongest deterministic check does not protect Listening at all.

**`IP-08` — a supplied transcript wins.** If the package already carries `part.transcript`, or the layout holds a transcript document, use it and make no AI call. This is the answer-key principle applied again: where an authoritative source exists, a model does not guess. It is also the cheaper path, and Cambridge books ship transcripts.

**The port carries no vendor type.** `IAudioTranscriber` lives in Application; `OpenAiAudioTranscriber` in Infrastructure. This is NOT `ITranscriptSource`, which is the Speaking-marking seam for learner audio and stays exactly as it is — `NoTranscriptSource`, `P-02`, untouched.

- [ ] **Step 1: Write the failing tests**

```csharp
/// <summary>
/// IP-08. A transcript in the package is authoritative; calling a model over
/// the top of it spends money to get a worse answer.
/// </summary>
[Fact]
public async Task A_supplied_transcript_is_used_and_no_model_is_called()
{
    var transcriber = new CountingTranscriber();
    var package = ListeningPackage(transcript: "The train leaves at half past six.");

    var result = await AudioTranscriptionStage.RunAsync(
        package, AudioFiles("listening/audio/part1.mp3"), transcriber, default);

    Assert.Equal(0, transcriber.Calls);
    Assert.Contains("half past six", TranscriptAt(result.PackageJson, part: 0));
}

[Fact]
public async Task A_part_with_no_transcript_is_transcribed_from_its_audio()
{
    var transcriber = new StubTranscriber("The train leaves at half past six.");
    var package = ListeningPackage(transcript: null);

    var result = await AudioTranscriptionStage.RunAsync(
        package, AudioFiles("listening/audio/part1.mp3"), transcriber, default);

    Assert.Contains("half past six", TranscriptAt(result.PackageJson, part: 0));
}

/// <summary>
/// A transcript is exam content and a Reading passage is not audio. Sending a
/// Reading part to a transcriber would be a paid call for nothing.
/// </summary>
[Fact]
public async Task Reading_parts_are_never_transcribed()
{
    var transcriber = new CountingTranscriber();

    await AudioTranscriptionStage.RunAsync(
        ReadingPackage(), AudioFiles("listening/audio/part1.mp3"), transcriber, default);

    Assert.Equal(0, transcriber.Calls);
}

/// <summary>
/// A refusal must not become an empty transcript. An empty string reads as
/// "this recording says nothing", and the anchor check would then report
/// every answer in the part as absent.
/// </summary>
[Fact]
public async Task A_refused_transcription_leaves_the_transcript_absent_and_says_so()
{
    var result = await AudioTranscriptionStage.RunAsync(
        ListeningPackage(transcript: null), AudioFiles("listening/audio/part1.mp3"),
        new RefusingTranscriber(), default);

    Assert.Null(TranscriptAt(result.PackageJson, part: 0));
    Assert.Contains(result.Warnings, w => w.Contains("part 1", StringComparison.OrdinalIgnoreCase));
}

/// <summary>With no transcriber configured the stage is a no-op, not a failure.</summary>
[Fact]
public async Task An_unconfigured_transcriber_changes_nothing()
{
    var package = ListeningPackage(transcript: null);

    var result = await AudioTranscriptionStage.RunAsync(
        package, AudioFiles("listening/audio/part1.mp3"),
        new UnconfiguredAudioTranscriber(), default);

    Assert.Null(TranscriptAt(result.PackageJson, part: 0));
}
```

- [ ] **Step 2: Run to verify they fail**

Expected: FAIL to compile.

- [ ] **Step 3: Write the port**

```csharp
namespace Vni.Ielts.Application.Importing;

/// <summary>
/// Turns one Listening recording into readable text, at import time.
///
/// <b>Not <see cref="Vni.Ielts.Application.Assessment.ITranscriptSource"/>,
/// and the difference matters.</b> That port transcribes a LEARNER'S speech:
/// personal data under PDPL, needing word-level timings for pronunciation, and
/// deferred by `P-02` until a provider is chosen. This one transcribes
/// published exam audio — third-party material, not personal data, needing only
/// text. Keeping them apart is what lets this ship while Speaking stays
/// deferred.
/// </summary>
public interface IAudioTranscriber
{
    bool IsConfigured { get; }

    Task<TranscriptionResult> TranscribeAsync(
        Stream audio, string fileName, CancellationToken ct);
}

public sealed record TranscriptionResult(bool IsSuccess, string? Text, string? RefusalCode);
```

- [ ] **Step 4: Write the stage**

For each Listening part, in part order:

1. If `part.transcript` is present and non-blank, leave it and count it as supplied.
2. Otherwise find the audio for that part. Match by the part's own `audio` asset reference when it has one; fall back to the Audio-role files in part order. **When the count of audio files does not match the count of parts needing one, transcribe none of them and warn** — guessing which recording belongs to which part is how a transcript lands on the wrong questions, the same failure class as a shifted answer key.
3. Call the transcriber. On success write `part.transcript`. On refusal leave it absent and add a warning naming the part.

Never write an empty string. Absent and empty mean different things downstream.

- [ ] **Step 5: Write the adapters**

`UnconfiguredAudioTranscriber`: `IsConfigured = false`, returns a refusal. Registered by default.

`OpenAiAudioTranscriber`: posts the audio to the configured transcription endpoint using the `Import:Transcription` section (`Provider`, `Model`, `ApiKey`, `BaseUrl?`), honouring `Ai:AllowCrossBorderTransfer` the way the Writing clients do. Same configuration-gate shape as Task 4: complete or absent, never half.

- [ ] **Step 6: Run the stage from the pipeline**

Between Parsing and Keying, at `ImportJobStage.Transcribing`. It must run before `AnswerKeyInjection` so the anchor check has text to work with.

- [ ] **Step 7: Record the decisions**

Add `IP-07`, `IP-08`, `IP-09` to `docs/requirements/confirmed.md` with Source `Owner decision 11/09/2026`, following the `IP-01`…`IP-06` block. Note explicitly that `P-02` is untouched and why.

- [ ] **Step 8: Run to verify they pass, then verify red-when-removed**

Two observations. Remove the supplied-transcript short-circuit, confirm `A_supplied_transcript_is_used_and_no_model_is_called` fails on a non-zero call count. Then make a refusal write `string.Empty`, confirm `A_refused_transcription_leaves_the_transcript_absent_and_says_so` fails. Restore both.

- [ ] **Step 9: Commit**

```bash
node scripts/check-docs.mjs
git add backend/src/Vni.Ielts.Application/Importing backend/src/Vni.Ielts.Infrastructure/Ai/Importing \
        backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs \
        docs/requirements/confirmed.md backend/tests
git commit -m "feat(import): transcribe Listening audio, unless the package brought its own text"
```

---

### Task 6: Explanations are written at import

**Files:**
- Modify: `backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs`
- Modify: `backend/src/Vni.Ielts.Infrastructure/DependencyInjection.cs` (register `CanonicalExplanationWorkflow`)
- Test: `backend/tests/Vni.Ielts.Infrastructure.Tests/Content/Import/ExamPackageImportPipelineTests.cs`

**Interfaces:**
- Consumes: `ImportReviewWorkflow.EnrichCanonicalExplanationsAsync(draftId, expectedRevision, actor, ct)` and `CanonicalExplanationWorkflow.EnrichDraftAsync` — both built, both tested, neither ever called from the API.
- Produces: `question.explanation` populated on the draft's package.

**This is the owner's original complaint, and the whole reason for this plan.** Today `AnswerReviewList` prefers `question.canonicalExplanation` and falls back to a per-learner call. Because nothing ever produces the canonical one through the CMS door, every explanation a learner sees is generated while they wait, on a button press. Once this stage runs, the existing UI shows the prepared text with no change to it.

**The generation gate stays.** `policyProfile.explanation.mode` must be `ai-generated`. `mode: none` means no explanations and no AI call, which is the correct behaviour for content whose rights are not cleared. Do not weaken it.

**Leave the on-demand path alone.** It is the fallback for every exam published before today. Removing it breaks them.

- [ ] **Step 1: Write the failing tests**

```csharp
/// <summary>
/// The complaint this plan exists for: a learner must not wait for an AI call
/// at results time.
/// </summary>
[Fact]
public async Task An_imported_package_carries_its_explanations_before_any_learner_sees_it()
{
    var attempt = await ImportWithExplanations(AiGeneratedPolicyPackage());

    var question = FirstAutoScoredQuestion(attempt.Draft!.PackageJson);
    Assert.NotNull(question["explanation"]);
    Assert.False(string.IsNullOrWhiteSpace(
        question["explanation"]!["shortReason"]!.GetValue<string>()));
}

/// <summary>
/// Rights, not capability. A package nobody cleared for AI processing must not
/// be sent to one.
/// </summary>
[Fact]
public async Task A_package_whose_policy_forbids_explanations_gets_none_and_costs_nothing()
{
    var generator = new CountingExplanationGenerator();

    var attempt = await ImportWithExplanations(NoExplanationPolicyPackage(), generator);

    Assert.Equal(0, generator.Calls);
    Assert.Null(FirstAutoScoredQuestion(attempt.Draft!.PackageJson)["explanation"]);
}

/// <summary>
/// A refused explanation is a review warning, not a lost import. Forty
/// questions and one refusal must not discard a paid parse.
/// </summary>
[Fact]
public async Task A_refused_explanation_becomes_a_warning_and_the_draft_survives()
{
    var attempt = await ImportWithExplanations(
        AiGeneratedPolicyPackage(), new RefusingExplanationGenerator());

    Assert.True(attempt.IsAccepted);
    Assert.Contains(attempt.Draft!.Warnings, w => w.Id.StartsWith("exp-", StringComparison.Ordinal));
}
```

- [ ] **Step 2: Run to verify they fail**

Expected: FAIL — no explanation is written, because nothing calls the workflow.

- [ ] **Step 3: Run the pass from the pipeline**

At `ImportJobStage.Explaining`, after Checking. `CanonicalExplanationWorkflow` is already injected-or-null in `ImportReviewWorkflow`; register it in `DependencyInjection` so it is non-null when a generator is configured.

The pass runs after the key is applied, because an explanation is generated against the answer the key supplied — not against the model's guess.

- [ ] **Step 4: Run to verify they pass, then verify red-when-removed**

Remove the enrich call, confirm `An_imported_package_carries_its_explanations_before_any_learner_sees_it` fails on a null explanation. Restore. Record the message.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Vni.Ielts.Infrastructure backend/tests
git commit -m "feat(import): write the explanations at import, so nobody waits for them"
```

---

### Task 7: The CMS hands out the skeleton

**Files:**
- Create: `backend/src/Vni.Ielts.Api/Endpoints/ImportTemplate.cs`
- Modify: `backend/src/Vni.Ielts.Api/Endpoints/AdminImportEndpoints.cs`
- Test: `backend/tests/Vni.Ielts.Integration.Tests/AdminImportEndpointsTests.cs`

**Interfaces:**
- Consumes: the folder names `ExamPackageArchiveInspector.RoleFolders` accepts.
- Produces: `GET /api/v1/admin/import/template`.

**Why an endpoint and not a file in the repo.** The folder names it ships must be the ones the inspector accepts. A checked-in ZIP drifts from the code the first time a name is added; one built from the same constants cannot.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task The_template_is_a_zip_whose_folders_the_inspector_accepts()
{
    var bytes = await client.GetByteArrayAsync("/api/v1/admin/import/template");

    using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
    var names = archive.Entries.Select(e => e.FullName).ToArray();

    Assert.Contains(names, n => n.StartsWith("reading/de/", StringComparison.Ordinal));
    Assert.Contains(names, n => n.StartsWith("reading/dap-an/", StringComparison.Ordinal));
    Assert.Contains(names, n => n.StartsWith("listening/audio/", StringComparison.Ordinal));

    var inspection = await inspector.InspectAsync(new MemoryStream(bytes), Limits, default);
    Assert.True(inspection.IsAcceptable, Describe(inspection));
    Assert.Empty(inspection.Findings.Where(f => f.Code == ArchiveFindingCodes.LayoutUnknownRoleFolder));
}

[Fact]
public async Task Downloading_the_template_needs_the_upload_permission()
{
    var response = await UnprivilegedClient().GetAsync("/api/v1/admin/import/template");

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

- [ ] **Step 2: Run to verify they fail**

Expected: FAIL — 404.

- [ ] **Step 3: Build the skeleton**

Compose the ZIP in memory. Each folder holds `HUONG-DAN.txt` naming what goes in it, which formats are read, and that unaccented folder names are required. A ZIP cannot carry a meaningful empty directory, and an operator opening it needs to know what `dap-an/` expects.

Permission `exam.submit`. `Content-Disposition: attachment; filename="vni-exam-package-template.zip"`.

- [ ] **Step 4: Run to verify they pass, then verify red-when-removed**

Rename `dap-an/` to `dapan_/` in the builder, confirm the first test fails on the missing prefix AND on an unknown-role-folder finding. Restore.

- [ ] **Step 5: Regenerate the contract and commit**

```bash
pnpm run generate:api-client && node scripts/check-generated-drift.mjs --mode=all
git add backend/src/Vni.Ielts.Api contracts/openapi/v1.json backend/tests
git commit -m "feat(import): hand out the package skeleton so nobody guesses the folder names"
```

---

### Task 8: The transcript reaches the learner, and not one moment early

**Files:**
- Modify: `backend/src/Vni.Ielts.Application/Exams/ExamViews.cs:83-94` (`PartView`)
- Modify: `backend/src/Vni.Ielts.Application/Exams/ExamHandlers.cs` (populate post-submit only)
- Modify: `apps/web/src/features/exam/examApi.ts` (type + the comment at line 363)
- Test: `backend/tests/Vni.Ielts.Integration.Tests/ExamRunContractTests.cs`

**Interfaces:**
- Consumes: `part.transcript`, written by Task 5.
- Produces: `PartView.Transcript`, non-null only on `SessionResultsView.Content`.

**`IP-09`, and the line it must not cross.** A learner who misheard question 12 learns most from reading what that passage actually said. But a transcript during a sitting is the answer sheet. `SessionResultsView.Content` is already gated on the whole sitting's status, not each section's — a Full Test candidate still on Listening must not receive Reading's content early, and the same gate carries this.

`ExamViews.cs:8` currently says the view "carries no transcript" and `examApi.ts:363` says transcripts are blocked by `P-02`. Both comments become wrong with this task; correct them rather than leaving a reader to trust them.

- [ ] **Step 1: Write the failing tests**

```csharp
/// <summary>The answer sheet, handed out mid-exam. This must never pass.</summary>
[Fact]
public async Task A_sitting_in_progress_carries_no_transcript()
{
    var session = await StartListeningSitting();

    var run = await client.GetFromJsonAsync<SessionRunView>($"/api/v1/sessions/{session}");

    Assert.All(run!.Section.Parts, p => Assert.Null(p.Transcript));
}

[Fact]
public async Task After_submission_the_listening_transcript_is_returned()
{
    var session = await CompleteListeningSitting();

    var results = await client.GetFromJsonAsync<SessionResultsView>(
        $"/api/v1/sessions/{session}/results");

    var listening = results!.Content.Single(c => c.Module == "listening");
    Assert.False(string.IsNullOrWhiteSpace(listening.Parts[0].Transcript));
}

/// <summary>
/// A Full Test candidate still on Listening must not be handed Reading's
/// transcript because Reading's own section already closed.
/// </summary>
[Fact]
public async Task A_closed_section_inside_an_open_sitting_still_carries_no_transcript()
{
    var session = await FullTestWithReadingClosedAndListeningOpen();

    var results = await client.GetFromJsonAsync<SessionResultsView>(
        $"/api/v1/sessions/{session}/results");

    Assert.Empty(results!.Content);
}
```

- [ ] **Step 2: Run to verify they fail**

Expected: FAIL to compile — `PartView` has no `Transcript`.

- [ ] **Step 3: Add the field and populate it in one place only**

Add `string? Transcript` to `PartView`. Populate it **only** where `SessionResultsView.Content` is built, and only when the sitting's status is not `InProgress`. Every other construction passes null.

Update the two stale comments.

- [ ] **Step 4: Run to verify they pass, then verify red-when-removed**

Populate `Transcript` in the in-progress run view as well; confirm `A_sitting_in_progress_carries_no_transcript` fails. Restore. **This is the most important negative proof in the task** — record the exact message.

- [ ] **Step 5: Regenerate the contract and commit**

```bash
pnpm run generate:api-client && node scripts/check-generated-drift.mjs --mode=all
pnpm -C apps/web test
git add backend/src apps/web/src contracts/openapi/v1.json backend/tests
git commit -m "feat(exam): return the Listening transcript after submission, never during"
```

---

## What this plan deliberately leaves out

| Left out | Why |
|---|---|
| The CMS review panel's per-question anchors and coverage statement | Interface work. The current phase is explicit: code and API first, the interface later. `AnchorReport.Anchors` is ready for it |
| Rendering the transcript on the learner results screen | Same reason. Task 8 puts it in the contract; drawing it is a later, small piece |
| Layer 5's own finding code | The model's dispute still files under a generic category. Named in the design, still deferred |
| Speaking marking | `P-02`. `ITranscriptSource` and `NoTranscriptSource` are untouched by Task 5 |
| Batch import over HTTP | `ImportBatchRunner` has a checkpoint store; no endpoint. Unchanged |
| Part B — Writing marking notes and model answers | `IP-04`: Reading and Listening first |
