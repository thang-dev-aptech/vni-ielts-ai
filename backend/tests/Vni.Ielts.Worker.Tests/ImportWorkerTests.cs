using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure;

namespace Vni.Ielts.Worker.Tests;

/// <summary>
/// The import worker, against the real outbox, the real import pipeline and a
/// real MongoDB.
///
/// <b>Everything except the archive store is real, and that choice is the
/// point.</b> The properties worth pinning here are survival properties — a
/// job abandoned mid-import is picked up again, a worker that loses its lease
/// stops rather than finishing, an attempt budget that is spent ends in
/// <c>Failed</c> — and every one of them is decided by a compare-and-set in
/// the database. A fake outbox under a lock agrees with a two-statement claim
/// for as long as nobody looks, and what that leaves undetected is two paid
/// Cambridge parses for one upload. Only the archive store is a double,
/// because it is the seam a test needs to hold open: making the worker pause
/// <i>inside</i> a job is how a lease can be stolen while the job is genuinely
/// in flight rather than between two sequential awaits.
///
/// One fresh database per test — the same technique as
/// <c>MongoImportOutboxTests</c> — and indexes are created for real, because
/// the enqueue's idempotency depends on the unique index existing rather than
/// on the store remembering to check first.
/// </summary>
public sealed class ImportWorkerTests
{
    private const string ConnectionString = "mongodb://localhost:27018/?directConnection=true";

    /// <summary>
    /// Probed once per run, and shaped exactly like <c>SsoAppFactory</c>'s:
    /// a developer without the infra stack up gets a skipped suite and a note,
    /// CI sets <c>VNI_REQUIRE_MONGO</c> and gets a failure.
    /// </summary>
    private static readonly Lazy<bool> _mongoAvailable = new(() =>
    {
        try
        {
            var client = new MongoClient(new MongoClientSettings
            {
                Server = new MongoServerAddress("localhost", 27018),
                DirectConnection = true,
                ServerSelectionTimeout = TimeSpan.FromSeconds(3),
                ConnectTimeout = TimeSpan.FromSeconds(3),
            });

            client.ListDatabaseNames().MoveNext();
            return true;
        }
        catch (Exception)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VNI_REQUIRE_MONGO")))
            {
                throw new InvalidOperationException(
                    "VNI_REQUIRE_MONGO is set and no MongoDB answered on localhost:27018. These "
                    + "are the only tests covering the import worker's lease and attempt-budget "
                    + "behaviour, so skipping them would report a green build over rules nothing "
                    + "checked.");
            }

            return false;
        }
    });

    private static bool MongoAvailable => _mongoAvailable.Value;

    private const string SkipReason =
        "No MongoDB on localhost:27018. Start it with "
        + "`docker compose -f infra/docker/compose.yaml up -d`.";

    // ── The harness ───────────────────────────────────────────────────────

    /// <summary>
    /// A clock the test moves, so a backoff and an expired lease are asserted
    /// rather than waited for.
    ///
    /// <b>Shared with the outbox, which is what makes it work.</b>
    /// <c>MongoImportOutbox</c> reads its own due-time and lease-expiry
    /// comparisons off <see cref="IClock"/>, so moving this one moves both
    /// sides of the question "is this job claimable yet". A <c>Task.Delay</c>
    /// instead would make every one of these tests a race against a real
    /// timer.
    /// </summary>
    private sealed class MovableClock : IClock
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public DateTimeOffset UtcNow => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed record Harness(
        ServiceProvider Provider,
        ImportWorker Worker,
        IImportOutbox Outbox,
        FakeArchiveStore Archives,
        MovableClock Clock) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private static async Task<Harness> NewHarnessAsync(
        FakeArchiveStore archives,
        Action<ServiceCollection>? configure = null,
        TimeSpan? heartbeat = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = ConnectionString,
                ["Mongo:Database"] = $"vni_ielts_import_worker_test_{Guid.NewGuid():n}",
                ["Jwt:SigningKey"] = new string('k', 48),
            })
            .Build();

        var clock = new MovableClock();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, isDevelopment: true);

        // The worker's own two graph repairs, copied from Program.cs: it has
        // no request to read a device from, and the health state is shared
        // between the loop and the health endpoint.
        services.AddSingleton<IRequestDevice>(new NoRequestDevice());
        services.AddSingleton<WorkerHealthState>();

        // The two overrides this suite depends on. Registered last, so they
        // win: a test that silently ran against the real clock or a real
        // bucket would be a test that passes for the wrong reason.
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IImportArchiveStore>(archives);

        // A test that needs to count paid calls replaces the parser here, last
        // of all, for the same reason the clock and the bucket are replaced:
        // a registration that did not win would make the test pass for the
        // wrong reason.
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        await provider.InitialiseInfrastructureAsync(default);

        var worker = new ImportWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            provider.GetRequiredService<WorkerHealthState>(),
            NullLogger<ImportWorker>.Instance,
            heartbeat);

        return new Harness(
            provider, worker, provider.GetRequiredService<IImportOutbox>(), archives, clock);
    }

    private sealed class NoRequestDevice : IRequestDevice
    {
        public string? UserAgent => null;
    }

    /// <summary>
    /// The archive store, in memory, with a gate a test can hold open.
    ///
    /// <b>The gate is the only reason this is a double.</b> A restart or a
    /// lease-theft invariant cannot be pinned by a sequential test — two
    /// awaits in a row never overlap, so a worker that ignored its lease would
    /// pass. Blocking a worker <i>inside</i> a claimed job is what makes the
    /// overlap real, and this is the seam the worker enters first.
    /// </summary>
    private sealed class FakeArchiveStore : IImportArchiveStore
    {
        private readonly Dictionary<string, byte[]> _objects = [];

        /// <summary>Released by the test; null means no gate.</summary>
        public TaskCompletionSource? Gate { get; init; }

        /// <summary>Signalled the moment a worker is inside <see cref="OpenAsync"/>.</summary>
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Thrown from <see cref="OpenAsync"/> when set — a transient storage failure.</summary>
        public Func<Exception>? Failure { get; init; }

        /// <summary>
        /// When true the gate is abandoned the moment the caller's token is
        /// cancelled, the way a real object-storage read would be.
        ///
        /// <b>Opt-in, because most of this suite must not have it.</b> The
        /// existing lease-loss test releases the gate itself and would
        /// otherwise be testing a different thing.
        /// </summary>
        public bool ObservesCancellation { get; init; }

        public List<string> Deleted { get; } = [];

        public Task<string> SaveAsync(string sourceSha256, Stream archive, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            archive.CopyTo(buffer);

            var key = $"imports/archives/{sourceSha256}.zip";
            _objects[key] = buffer.ToArray();

            return Task.FromResult(key);
        }

        public async Task<Stream?> OpenAsync(string archiveKey, CancellationToken ct)
        {
            Entered.TrySetResult();

            if (Gate is not null)
            {
                if (ObservesCancellation) await Gate.Task.WaitAsync(ct);
                else await Gate.Task;
            }

            if (Failure is not null) throw Failure();

            return _objects.TryGetValue(archiveKey, out var bytes)
                // Deliberately NOT seekable: this is what object storage
                // really hands back, and the worker has to spool it before the
                // pipeline can read the ZIP central directory at the end of
                // the file.
                ? new ForwardOnlyStream(bytes)
                : null;
        }

        public Task DeleteAsync(string archiveKey, CancellationToken ct)
        {
            Deleted.Add(archiveKey);
            _objects.Remove(archiveKey);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A stream that refuses to seek, standing in for an S3 response body.
    ///
    /// <b>Not a <c>MemoryStream</c>, on purpose.</b> A seekable double would
    /// let the worker hand the pipeline the store's stream directly and still
    /// pass — and that is exactly the mistake that produces a truncated read
    /// in production, where the archive is a network stream and the ZIP
    /// central directory lives at the end of the file.
    /// </summary>
    private sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    // ── Fixtures ──────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues a job for <paramref name="zip"/> and returns its operation id.
    /// The archive is saved first, in the order the endpoint saves it: a job
    /// enqueued before its bytes are parked is a job the worker cannot do.
    /// </summary>
    private static async Task<(string OperationId, ExamDefinitionId DefinitionId)> EnqueueAsync(
        Harness harness, byte[] zip)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zip))
            .ToLowerInvariant();

        using var bytes = new MemoryStream(zip);
        var key = await harness.Archives.SaveAsync(hash, bytes, default);

        var definitionId = ExamDefinitionId.New();
        var job = ImportJob.New(
            definitionId, 1, hash, ImportJob.NoParserConfigured, key, harness.Clock.UtcNow);

        Assert.True(await harness.Outbox.EnqueueAsync(job, default));

        return (job.OperationId, definitionId);
    }

    private static byte[] StructuredPackage() =>
        BuildZip(("reading/exam.json", ValidPackageJson.Replace(
            "worker-import-test", $"worker-{Guid.NewGuid():n}")));

    /// <summary>Many zero bytes that deflate shrinks hundredfold — refused on the ratio cap.</summary>
    private static byte[] CompressionBomb()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("reading/bomb.txt", CompressionLevel.SmallestSize);
            using var output = entry.Open();
            output.Write(new byte[10 * 1024 * 1024]);
        }

        return stream.ToArray();
    }

    private static byte[] BuildZip(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    /// <summary>Copied from <c>AdminImportEndpointsTests</c>, which took it from a package proven valid.</summary>
    private const string ValidPackageJson = """
    {
      "formatVersion": "2.0", "formatProfile": "vni-practice", "scoringProfileRef": "worker-import-test",
      "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
      "title": "Import worker test", "variant": "academic",
      "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
      "scoringProfile": { "rawToBand": { "reading": [
        { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }, { "minRaw": 2, "band": 2 } ] } },
      "sequenceProfile": { "modules": ["reading"] },
      "sections": [{ "module": "reading", "order": 1, "parts": [{ "order": 1, "kind": "passage",
        "body": "Evidence here.", "questions": [{
          "id": "q-1", "order": 1, "type": "multiple-select", "marks": 2,
          "options": [{ "key": "A", "text": "Alpha" }, { "key": "B", "text": "Beta" }],
          "group": { "id": "bank-1", "instruction": "Choose." },
          "slots": [
            { "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } },
            { "id": "slot-2", "number": 2, "answerKey": { "accepted": ["B"] } }
          ],
          "explanation": { "shortReason": "Both are stated.", "evidence": ["Evidence here."] }
        }]
      }]}]
    }
    """;

    // ── The tests ─────────────────────────────────────────────────────────

    /// <summary>
    /// The whole reason this is a job. A worker that dies mid-import must
    /// leave the work owed, not lost — and the next worker must be able to
    /// finish it.
    /// </summary>
    [SkippableFact]
    public async Task A_job_abandoned_mid_import_is_picked_up_again()
    {
        Skip.IfNot(MongoAvailable, SkipReason);

        await using var harness = await NewHarnessAsync(new FakeArchiveStore());
        var (operationId, _) = await EnqueueAsync(harness, StructuredPackage());

        // A worker claims the job and then dies: it never renews, so its lease
        // simply runs out.
        Assert.NotNull(await harness.Outbox.ClaimAsync("dead", TimeSpan.FromSeconds(1), default));
        harness.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(await harness.Worker.RunOnceAsync(default));

        var job = await harness.Outbox.FindAsync(operationId, default);

        Assert.Equal(ImportJobState.Completed, job!.State);
        Assert.NotNull(job.DraftId);
        Assert.Equal(ImportJobStage.Done, job.Stage);
    }

    /// <summary>
    /// The draft the worker produced is really on disk and really reachable —
    /// not merely a job row that says <c>Completed</c>.
    /// </summary>
    [SkippableFact]
    public async Task A_completed_import_leaves_a_real_draft_and_no_stored_archive()
    {
        Skip.IfNot(MongoAvailable, SkipReason);

        var archives = new FakeArchiveStore();
        await using var harness = await NewHarnessAsync(archives);
        var (operationId, definitionId) = await EnqueueAsync(harness, StructuredPackage());

        await harness.Worker.RunOnceAsync(default);

        var job = await harness.Outbox.FindAsync(operationId, default);
        Assert.Equal(ImportJobState.Completed, job!.State);

        var drafts = harness.Provider.GetRequiredService<IImportDraftStore>();
        var draft = await drafts.FindAsync(job.DraftId!.Value, default);

        Assert.NotNull(draft);
        Assert.Equal(definitionId.Value, draft!.DefinitionId.Value);

        // An uploaded exam package is third-party copyright. The one moment it
        // is certainly no longer needed is when the job settles.
        Assert.Equal(job.ArchiveKey, Assert.Single(archives.Deleted));
    }

    /// <summary>
    /// An import that fails for a reason no retry fixes must not spin, and
    /// must not burn the budget either: a package that was extracted and then
    /// refused is permanently bad, and paying three times to learn what the
    /// first attempt proved is the cost <c>RetryAsync</c> and <c>FailAsync</c>
    /// were split apart to avoid.
    /// </summary>
    [SkippableFact]
    public async Task A_package_the_pipeline_refuses_is_failed_on_the_first_attempt()
    {
        Skip.IfNot(MongoAvailable, SkipReason);

        var archives = new FakeArchiveStore();
        await using var harness = await NewHarnessAsync(archives);
        var (operationId, _) = await EnqueueAsync(harness, CompressionBomb());

        await harness.Worker.RunOnceAsync(default);

        var job = await harness.Outbox.FindAsync(operationId, default);

        Assert.Equal(ImportJobState.Failed, job!.State);
        Assert.Equal(1, job.Attempts);
        Assert.Contains("ZIP_COMPRESSION_RATIO", job.LastError);
        Assert.Equal(job.ArchiveKey, Assert.Single(archives.Deleted));
    }

    /// <summary>
    /// <b>The attempt budget, kept rather than promised.</b>
    /// <see cref="ImportJob.MayRetry"/> existed and nothing called it — and a
    /// promise in a doc comment that no code keeps is how a budget quietly
    /// becomes infinite. Every retry here may be a paid parse.
    /// </summary>
    [SkippableFact]
    public async Task A_job_out_of_attempts_lands_in_failed_with_its_reason()
    {
        Skip.IfNot(MongoAvailable, SkipReason);

        // A transient storage failure — the shape that SHOULD be retried,
        // which is what makes the budget the only thing that stops it.
        var archives = new FakeArchiveStore
        {
            Failure = () => new TimeoutException("the object store did not answer"),
        };

        await using var harness = await NewHarnessAsync(archives);
        var (operationId, _) = await EnqueueAsync(harness, StructuredPackage());

        for (var attempt = 0; attempt < ImportJob.MaxAttempts; attempt++)
        {
            Assert.True(
                await harness.Worker.RunOnceAsync(default),
                $"Attempt {attempt + 1} found nothing to claim; the backoff was not waited out.");

            // Past the longest backoff this worker schedules, so the next pass
            // really is the next attempt rather than an empty poll.
            harness.Clock.Advance(TimeSpan.FromMinutes(10));
        }

        var job = await harness.Outbox.FindAsync(operationId, default);

        Assert.Equal(ImportJobState.Failed, job!.State);
        Assert.Equal(ImportJob.MaxAttempts, job.Attempts);
        Assert.False(string.IsNullOrWhiteSpace(job.LastError));

        // A fourth pass finds nothing: a failed job is out of the queue.
        Assert.False(await harness.Worker.RunOnceAsync(default));
    }

    /// <summary>
    /// The stored reason an operator reads is a sentence, never a provider or
    /// driver message. <c>LastError</c> reaches an admin screen, and a message
    /// this code did not write can carry a response body — or the paper being
    /// parsed.
    /// </summary>
    [SkippableFact]
    public async Task A_failure_reason_names_the_stage_and_the_type_but_never_the_providers_words()
    {
        Skip.IfNot(MongoAvailable, SkipReason);

        const string secret = "the object store did not answer: Passage 1 — The history of salt";

        var archives = new FakeArchiveStore { Failure = () => new TimeoutException(secret) };
        await using var harness = await NewHarnessAsync(archives);
        var (operationId, _) = await EnqueueAsync(harness, StructuredPackage());

        await harness.Worker.RunOnceAsync(default);

        var job = await harness.Outbox.FindAsync(operationId, default);

        // The leak assertion first: it is the one that matters, and a test
        // that reports the weaker failure first hides which half broke.
        Assert.DoesNotContain("The history of salt", job!.LastError);
        Assert.Contains("TimeoutException", job.LastError);
    }

    /// <summary>
    /// An archive that is gone is terminal, not transient: retrying twice more
    /// spends two claims to learn the same thing and leaves the job looking
    /// busy. The operator's answer is "upload it again", which they can only
    /// act on if they are told.
    /// </summary>
    [SkippableFact]
    public async Task A_job_whose_archive_is_gone_fails_with_a_sentence_an_operator_can_act_on()
    {
        Skip.IfNot(MongoAvailable, SkipReason);

        var archives = new FakeArchiveStore();
        await using var harness = await NewHarnessAsync(archives);

        // Enqueued against a key nothing was ever saved under.
        var job = ImportJob.New(
            ExamDefinitionId.New(), 1, new string('a', 64), ImportJob.NoParserConfigured,
            $"imports/archives/{new string('a', 64)}.zip", harness.Clock.UtcNow);

        await harness.Outbox.EnqueueAsync(job, default);

        await harness.Worker.RunOnceAsync(default);

        var settled = await harness.Outbox.FindAsync(job.OperationId, default);

        Assert.Equal(ImportJobState.Failed, settled!.State);
        Assert.Equal(1, settled.Attempts);
        Assert.Contains("Upload the package again", settled.LastError);
    }

    /// <summary>
    /// <b>A worker that loses its lease while it is inside a job must not
    /// finish it.</b>
    ///
    /// <b>Genuinely interleaved, because it cannot be proved any other
    /// way.</b> Two sequential awaits never overlap: a worker that ignored its
    /// lease entirely would pass a version of this test that claimed, stole
    /// and completed in order. The worker is held inside
    /// <c>IImportArchiveStore.OpenAsync</c> — which is the first thing it does
    /// after claiming — while a second claim takes the job over, and only then
    /// released. What must hold afterwards is that the first worker did not
    /// mark the job <c>Completed</c> under a lease it no longer owns, and did
    /// not delete the archive the new owner is about to read.
    /// </summary>
    [SkippableFact]
    public async Task A_worker_that_loses_its_lease_mid_import_neither_completes_it_nor_deletes_the_archive()
    {
        Skip.IfNot(MongoAvailable, SkipReason);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var archives = new FakeArchiveStore { Gate = gate };

        await using var harness = await NewHarnessAsync(archives);
        var (operationId, _) = await EnqueueAsync(harness, StructuredPackage());

        // Worker A starts and blocks inside the archive read, holding a
        // claimed job.
        var a = harness.Worker.RunOnceAsync(default);
        await archives.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // A's lease runs out while it is still in there, and another worker
        // takes the job over. This is the moment the whole lease mechanism
        // exists for.
        harness.Clock.Advance(TimeSpan.FromMinutes(30));
        var stolen = await harness.Outbox.ClaimAsync("worker-b", TimeSpan.FromMinutes(10), default);
        Assert.NotNull(stolen);

        // Now let A finish its import and try to write.
        gate.SetResult();
        await a.WaitAsync(TimeSpan.FromSeconds(60));

        var job = await harness.Outbox.FindAsync(operationId, default);

        Assert.NotEqual(ImportJobState.Completed, job!.State);
        Assert.Equal(2, job.Attempts);
        Assert.Empty(archives.Deleted);
    }

    // ── The stage, doing the job it was built for ─────────────────────────

    /// <summary>
    /// A parser that counts what it costs, and produces a schema-valid paper.
    ///
    /// <b>Counting is the assertion.</b> A real parse of a Cambridge paper is
    /// the single most expensive thing this system buys, and "a retry does not
    /// pay again" is only provable by the number of times it was bought.
    /// </summary>
    private sealed class CountingParser : IExamSourceParser
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string PromptVersion => "test-parse-prompt";

        public Task<ParsedExamPackage> ParseAsync(ExtractedImportSource source, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);

            return Task.FromResult(new ParsedExamPackage(
                OneReadingQuestion,
                new ParserRunMetadata("fake", "counting-parser", "test-parse-prompt", "req-1")));
        }

        /// <summary>
        /// One Reading question the model answered FALSE. The supplied key
        /// says TRUE — the schema forces an auto-scored question to carry
        /// <i>some</i> answer, which is exactly why a fabricated one is
        /// invisible without a key.
        /// </summary>
        private const string OneReadingQuestion = """
        {
          "formatVersion": "2.0", "formatProfile": "vni-practice",
          "scoringProfileRef": "worker-resume-test",
          "contentSourceRef": { "sourceId": "counting-parser",
            "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          "title": "Resume", "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
          "scoringProfile": { "rawToBand": { "reading": [
            { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 } ] } },
          "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
            "kind": "passage", "body": "The roof is made of slate.",
            "questions": [ { "id": "r1", "order": 1, "type": "true-false-notgiven",
              "answerKey": { "accepted": ["FALSE"] } } ] } ] } ]
        }
        """;
    }

    /// <summary>
    /// A source extractor whose answer-key read fails once and then works.
    ///
    /// <b>The failure is placed after the parse on purpose.</b> Keying is the
    /// first step past <c>Parsing</c>, so a failure there leaves a job whose
    /// recorded stage says the parse is bought and whose draft is on disk —
    /// the precise state a resumed worker has to recognise. A failure before
    /// the parse would prove nothing, because there would be nothing to reuse.
    /// </summary>
    private sealed class KeyFailsOnceExtractor : ISourceDocumentExtractor
    {
        private bool _failedOnce;

        public Task<SourceExtractionResult> ExtractAsync(
            string sandboxRoot, string relativePath, SourceExtractionLimits limits, CancellationToken ct)
        {
            var isKey = relativePath.Contains("dap-an", StringComparison.Ordinal);

            if (isKey && !_failedOnce)
            {
                _failedOnce = true;
                throw new TimeoutException("the extractor did not answer");
            }

            var text = isKey ? "Câu số 1: TRUE" : "The roof is made of slate.";
            var hash = Vni.Ielts.Application.Importing.ExamImportWorkflow.Hash(text);

            return Task.FromResult(new SourceExtractionResult(
                true,
                new ExtractedImportSource(
                    relativePath, "text/plain", text, hash, hash,
                    ImportDataClassification.Restricted),
                [],
                []));
        }
    }

    /// <summary>A paper and its answer key — the AI-parsed route, with a key folder.</summary>
    private static byte[] PaperAndKeyPackage() =>
        BuildZip(
            ("reading/de/passage.txt", $"The roof is made of slate. {Guid.NewGuid():n}"),
            ("reading/dap-an/key.txt", "Câu số 1: TRUE"));

    /// <summary>
    /// <b>C1: the recorded stage is read, and a retry does not re-buy the
    /// parse.</b>
    ///
    /// <c>ImportJobStage</c>, <c>AdvanceAsync</c>'s forward-only filter and
    /// <c>RetryAsync</c>'s refusal to reset the stage were all built, all
    /// documented as the thing that stops an import paying twice — and nothing
    /// read any of it. <c>ImportWorker</c> called the pipeline from the top on
    /// every claim, and <c>ImportExtractedAsync</c> called the parser
    /// unconditionally. One transient failure therefore bought a Cambridge
    /// paper twice, and the attempt budget allowed three.
    ///
    /// The first run fails at <c>Keying</c>, which is the first step past
    /// <c>Parsing</c>: the parse is paid for and its draft is on disk. The
    /// second run must finish the import without calling the parser again.
    /// </summary>
    [SkippableFact]
    public async Task A_retry_after_the_parse_resumes_from_the_draft_and_does_not_buy_it_again()
    {
        Skip.IfNot(MongoAvailable, SkipReason);

        var parser = new CountingParser();

        await using var harness = await NewHarnessAsync(
            new FakeArchiveStore(),
            services =>
            {
                services.AddSingleton<IExamSourceParser>(parser);
                services.AddSingleton<ISourceDocumentExtractor>(new KeyFailsOnceExtractor());
            });

        var (operationId, _) = await EnqueueAsync(harness, PaperAndKeyPackage());

        // Run one: parses, saves a draft, then fails reading the answer key.
        Assert.True(await harness.Worker.RunOnceAsync(default));

        var afterFirst = await harness.Outbox.FindAsync(operationId, default);
        Assert.Equal(ImportJobState.Retryable, afterFirst!.State);
        Assert.True(
            afterFirst.Stage > ImportJobStage.Parsing,
            $"The first run recorded stage {afterFirst.Stage}; nothing past Parsing means there is "
            + "no evidence a resumed run could act on, and the rest of this test would prove "
            + "nothing.");
        Assert.Equal(1, parser.Calls);

        // Past the backoff, so the second pass really is the next attempt.
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        Assert.True(await harness.Worker.RunOnceAsync(default));

        var job = await harness.Outbox.FindAsync(operationId, default);
        Assert.Equal(ImportJobState.Completed, job!.State);

        // The assertion this test exists for: the second run added nothing to
        // the bill for a stage the job already records as bought.
        Assert.Equal(1, parser.Calls);

        // And the resumed run really did finish the work, rather than merely
        // skipping it: the supplied key is on the draft, over the model's guess.
        var drafts = harness.Provider.GetRequiredService<IImportDraftStore>();
        var draft = await drafts.FindAsync(job.DraftId!.Value, default);

        Assert.Contains("TRUE", draft!.PackageJson);
    }

    /// <summary>
    /// <b>C2: a worker that has lost its lease stops before it buys anything
    /// else.</b>
    ///
    /// <c>RenewAsync</c> discovered the loss, logged that the parse was being
    /// performed twice, and returned. The pipeline ran on
    /// <c>CancellationToken.None</c>, so the dispossessed worker went on
    /// through the parse, the transcription and the explanations and only found
    /// out at <c>CompleteAsync</c> — which is the two-workers-two-bills case
    /// the lease exists to prevent, with the log line as the whole response.
    ///
    /// <b>Genuinely interleaved.</b> Worker A is held inside the archive read —
    /// before any paid stage — while a second claim takes the job over, and the
    /// heartbeat has to notice on its own. The gate is released after a bounded
    /// wait so that a worker which ignored its lease fails this test by
    /// <i>calling the parser</i> rather than by hanging.
    /// </summary>
    [SkippableFact]
    public async Task A_worker_that_loses_its_lease_is_cancelled_before_it_pays_for_the_parse()
    {
        Skip.IfNot(MongoAvailable, SkipReason);

        var parser = new CountingParser();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var archives = new FakeArchiveStore { Gate = gate, ObservesCancellation = true };

        await using var harness = await NewHarnessAsync(
            archives,
            services =>
            {
                services.AddSingleton<IExamSourceParser>(parser);
                services.AddSingleton<ISourceDocumentExtractor>(new KeyFailsOnceExtractor());
            },
            // The one number a fake clock cannot move: the renewal is a real
            // Task.Delay. Forty seconds of waiting is a test nobody runs.
            heartbeat: TimeSpan.FromMilliseconds(25));

        var (operationId, _) = await EnqueueAsync(harness, PaperAndKeyPackage());

        var a = harness.Worker.RunOnceAsync(default);
        await archives.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // A's lease runs out while it is still inside the job, and another
        // worker takes it over.
        harness.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.NotNull(await harness.Outbox.ClaimAsync("worker-b", TimeSpan.FromMinutes(10), default));

        // With the heartbeat wired to cancellation, A is already unwinding and
        // this returns at once. Without it, A is still sitting on the gate —
        // so the gate is opened, and A proves the bug by paying.
        await Task.WhenAny(a, Task.Delay(TimeSpan.FromSeconds(3)));
        gate.TrySetResult();

        await a.WaitAsync(TimeSpan.FromSeconds(60));

        // The assertion this test exists for.
        Assert.Equal(0, parser.Calls);

        var job = await harness.Outbox.FindAsync(operationId, default);
        Assert.NotEqual(ImportJobState.Completed, job!.State);

        // Worker B owns the job and its archive; A must not have removed either.
        Assert.Empty(archives.Deleted);
    }

    /// <summary>
    /// The real outbox in every respect except that renewing throws.
    ///
    /// <b>A decorator over the registered implementation, not a hand-written
    /// fake.</b> Every other operation in this test has to behave exactly as it
    /// does in production — the claim, the stage writes, the settle — because
    /// what is being pinned is that the worker stops, and a fake that got the
    /// claim wrong would prove nothing about that.
    /// </summary>
    private sealed class RenewalFailsOutbox(IImportOutbox inner) : IImportOutbox
    {
        public Task<bool> EnqueueAsync(ImportJob job, CancellationToken ct) =>
            inner.EnqueueAsync(job, ct);

        public Task<ImportJob?> ClaimAsync(string leaseToken, TimeSpan lease, CancellationToken ct) =>
            inner.ClaimAsync(leaseToken, lease, ct);

        /// <summary>A database blip, a socket reset, a driver bug — the loop leaves either way.</summary>
        public Task<bool> RenewAsync(
            string operationId, string leaseToken, TimeSpan lease, CancellationToken ct) =>
            throw new TimeoutException("the database did not answer the renewal");

        public Task<bool> AdvanceAsync(
            string operationId, string leaseToken, ImportJobStage stage, Guid? draftId,
            CancellationToken ct) =>
            inner.AdvanceAsync(operationId, leaseToken, stage, draftId, ct);

        public Task<bool> CompleteAsync(string operationId, string leaseToken, CancellationToken ct) =>
            inner.CompleteAsync(operationId, leaseToken, ct);

        public Task<bool> RetryAsync(
            string operationId, string leaseToken, DateTimeOffset nextAttemptAt, string error,
            CancellationToken ct) =>
            inner.RetryAsync(operationId, leaseToken, nextAttemptAt, error, ct);

        public Task<bool> FailAsync(
            string operationId, string leaseToken, string error, CancellationToken ct) =>
            inner.FailAsync(operationId, leaseToken, error, ct);

        public Task<bool> ReopenAsync(string operationId, CancellationToken ct) =>
            inner.ReopenAsync(operationId, ct);

        public Task<ImportJob?> FindAsync(string operationId, CancellationToken ct) =>
            inner.FindAsync(operationId, ct);
    }

    /// <summary>
    /// <b>F-12: the second door out of the heartbeat, shut.</b>
    ///
    /// C2 wired a <i>lost</i> lease to cancellation. A renewal that
    /// <b>throws</b> took the other exit: the loop logged "the lease will
    /// expire normally" and left for good, so nothing renewed the lease, it
    /// expired, another worker claimed the job — and this one carried on paying
    /// for every remaining stage. The same failure C2 described, reached
    /// through the error path.
    ///
    /// <b>Interleaved, because it cannot be proved otherwise.</b> The worker is
    /// held inside the archive read — before any paid stage — while the
    /// heartbeat fires for real against an outbox whose renewal throws. The
    /// gate is opened after a bounded wait so a worker that ignored the failure
    /// fails this test by calling the parser rather than by hanging.
    /// </summary>
    [SkippableFact]
    public async Task A_worker_whose_renewal_throws_stops_instead_of_paying_on_a_lease_nobody_renews()
    {
        Skip.IfNot(MongoAvailable, SkipReason);

        var parser = new CountingParser();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var archives = new FakeArchiveStore { Gate = gate, ObservesCancellation = true };

        await using var harness = await NewHarnessAsync(
            archives,
            services =>
            {
                services.AddSingleton<IExamSourceParser>(parser);
                services.AddSingleton<ISourceDocumentExtractor>(new KeyFailsOnceExtractor());

                // Wrap whatever Infrastructure registered, rather than
                // replacing it: the claim and the settle must stay real.
                var registered = services.Last(d => d.ServiceType == typeof(IImportOutbox));
                var implementation = registered.ImplementationType!;

                services.AddScoped<IImportOutbox>(sp => new RenewalFailsOutbox(
                    (IImportOutbox)ActivatorUtilities.CreateInstance(sp, implementation)));
            },
            heartbeat: TimeSpan.FromMilliseconds(25));

        var (operationId, _) = await EnqueueAsync(harness, PaperAndKeyPackage());

        var a = harness.Worker.RunOnceAsync(default);
        await archives.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await Task.WhenAny(a, Task.Delay(TimeSpan.FromSeconds(3)));
        gate.TrySetResult();

        await a.WaitAsync(TimeSpan.FromSeconds(60));

        // The assertion this test exists for.
        Assert.Equal(0, parser.Calls);

        var job = await harness.Outbox.FindAsync(operationId, default);
        Assert.NotEqual(ImportJobState.Completed, job!.State);
    }
}
