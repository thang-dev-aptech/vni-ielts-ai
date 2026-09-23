using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Api.Tests;

/// <summary>
/// <c>eval-verification</c> — the admin evaluation HTTP surface: permission
/// boundaries, content redaction, rejected-output evidence, rerun idempotency,
/// and that the "Thay cho" / "Bị thay bởi" supersession links the CMS detail
/// screen renders actually resolve.
///
/// <b>No SSO round trip.</b> `AdminImportHistoryEndpointsTests.cs` in this same
/// project is excluded from the build because it needs `Integration.Tests`'
/// `SsoAppFactory`, which this project does not reference. This suite mints a
/// JWT directly against the host's own signing key instead — the same pattern
/// <see cref="AdminConfigEndpointsTests"/> already uses successfully here.
/// </summary>
public sealed class AdminEvaluationEndpointsTests : IClassFixture<EvaluationAppFactory>
{
    private readonly EvaluationAppFactory _app;

    public AdminEvaluationEndpointsTests(EvaluationAppFactory app) => _app = app;

    // ── Acceptance 1 — evaluation.read alone cannot reveal learner content ──

    [SkippableFact]
    public async Task Content_is_redacted_without_learner_content_read_even_with_includeContent_true()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var session = ExamSessionId.New();
        await SeedMarkingAsync(session, "mark-content-1", version: 1);
        await SeedSubmissionAsync(session, "It is often argued that space research is not justified.");

        var client = _app.CreateClient();
        var readOnly = Token(PermissionKeys.EvaluationRead);

        using var request = Authed(
            HttpMethod.Get,
            $"/api/v1/admin/evaluations/{session.Value}/mark-content-1?includeContent=true",
            readOnly);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await BodyOf(response);
        Assert.False(body.TryGetProperty("learnerSubmission", out _));
        Assert.False(body.TryGetProperty("ungroundedEvidence", out _));
        var criterion = body.GetProperty("criteria").EnumerateArray().First();
        Assert.False(criterion.TryGetProperty("evidence", out _));

        // Redaction must not be logged as a reveal — no access row for a
        // request that received nothing protected.
        using (var scope = _app.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var (entries, _) = await audit.ListAsync(
                null, nameof(AuditAction.EvaluationContentAccessed), 0, 50, default);
            Assert.DoesNotContain(entries, e => e.TargetId == "mark-content-1");
        }
    }

    [SkippableFact]
    public async Task Content_is_revealed_and_audited_with_both_permission_and_includeContent()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var session = ExamSessionId.New();
        await SeedMarkingAsync(session, "mark-content-2", version: 1);
        const string essay = "It is often argued that space research is not justified.";
        await SeedSubmissionAsync(session, essay);

        var client = _app.CreateClient();
        var full = Token(PermissionKeys.EvaluationRead, PermissionKeys.LearnerContentRead);

        using var request = Authed(
            HttpMethod.Get,
            $"/api/v1/admin/evaluations/{session.Value}/mark-content-2?includeContent=true",
            full);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await BodyOf(response);
        var submission = body.GetProperty("learnerSubmission");
        Assert.Equal(essay, submission.GetProperty("w-task-2-slot-1").GetString());
        var criterion = body.GetProperty("criteria").EnumerateArray().First();
        Assert.True(criterion.TryGetProperty("evidence", out var evidence));
        Assert.Contains("space research", evidence[0].GetString());

        using (var scope = _app.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var (entries, _) = await audit.ListAsync(
                null, nameof(AuditAction.EvaluationContentAccessed), 0, 50, default);
            var mine = entries.Single(e => e.TargetId == "mark-content-2");
            Assert.Equal(session.Value, mine.Detail["sessionId"]);
            // Metadata only — the essay text itself never enters the audit row.
            Assert.DoesNotContain(essay, string.Join("|", mine.Detail.Values));
        }
    }

    [SkippableFact]
    public async Task includeContent_alone_without_the_permission_still_redacts()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        // The inverse of the pair above: evaluation.read is present, but a
        // caller who forgot the query string must not get content just
        // because they hold learner-content.read — includeContent is the
        // explicit ask, and its absence is itself a redaction path.
        var session = ExamSessionId.New();
        await SeedMarkingAsync(session, "mark-content-3", version: 1);
        await SeedSubmissionAsync(session, "Some essay text.");

        var client = _app.CreateClient();
        var full = Token(PermissionKeys.EvaluationRead, PermissionKeys.LearnerContentRead);

        using var request = Authed(
            HttpMethod.Get, $"/api/v1/admin/evaluations/{session.Value}/mark-content-3", full);
        var response = await client.SendAsync(request);
        var body = await BodyOf(response);

        Assert.False(body.TryGetProperty("learnerSubmission", out _));
    }

    // ── Acceptance 3 — rejected raw output: authorized path only, never in audit ──

    [SkippableFact]
    public async Task Rejected_raw_output_is_hidden_by_default_and_revealed_only_with_full_access()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var session = ExamSessionId.New();
        // DetailEndpoint derives the operation id itself from the marking's
        // own module + rubric version — a seeded attempt must be filed under
        // that same computed id or ListByOperationAsync finds nothing.
        var operationId = MarkingJob.IdFor(session, ExamModule.Writing, "writing-v2");
        const string raw = """{"criteria":{"taskAchievement":{"band":47}}}""";
        await SeedMarkingAsync(session, "mark-raw-1", version: 1, rubricVersion: "writing-v2");
        await SeedAttemptAsync(
            operationId, session, module: ExamModule.Writing, taskNumber: 2,
            outcome: EvaluationAttemptOutcome.Rejected, rawOutput: raw,
            markingId: "mark-raw-1", markingVersion: 1);

        var client = _app.CreateClient();

        using (var redacted = Authed(
                   HttpMethod.Get,
                   $"/api/v1/admin/evaluations/{session.Value}/mark-raw-1?includeContent=true",
                   Token(PermissionKeys.EvaluationRead)))
        {
            var response = await client.SendAsync(redacted);
            var body = await BodyOf(response);
            var attempt = body.GetProperty("attempts").EnumerateArray().Single();
            Assert.False(attempt.TryGetProperty("rawOutput", out _));
            Assert.True(attempt.GetProperty("rawOutputTruncated").GetBoolean() == false);
        }

        using (var full = Authed(
                   HttpMethod.Get,
                   $"/api/v1/admin/evaluations/{session.Value}/mark-raw-1?includeContent=true",
                   Token(PermissionKeys.EvaluationRead, PermissionKeys.LearnerContentRead)))
        {
            var response = await client.SendAsync(full);
            var body = await BodyOf(response);
            var attempt = body.GetProperty("attempts").EnumerateArray().Single();
            Assert.Equal(raw, attempt.GetProperty("rawOutput").GetString());
        }

        // Never in the audit row, under either access level.
        using var scope = _app.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var (entries, _) = await audit.ListAsync(
            null, nameof(AuditAction.EvaluationContentAccessed), 0, 50, default);
        Assert.DoesNotContain(entries, e => string.Join("|", e.Detail.Values).Contains("taskAchievement"));
    }

    // ── Acceptance 4 (list/detail half) — supersession chain and paging ──

    [SkippableFact]
    public async Task A_rerun_leaves_the_old_marking_reachable_and_exactly_one_current()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var session = ExamSessionId.New();
        await SeedMarkingAsync(session, "mark-v1", version: 1);
        await SeedMarkingAsync(session, "mark-v2", version: 1); // same slot → supersedes mark-v1

        var client = _app.CreateClient();
        var token = Token(PermissionKeys.EvaluationRead);

        using (var oldDetail = Authed(
                   HttpMethod.Get, $"/api/v1/admin/evaluations/{session.Value}/mark-v1", token))
        {
            // The bug this slice's verification found: DetailEndpoint used to
            // resolve a marking through a current-only read, so this 404'd —
            // exactly the "Thay cho" link the CMS detail screen renders.
            var response = await client.SendAsync(oldDetail);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await BodyOf(response);
            Assert.False(body.GetProperty("isCurrent").GetBoolean());
            Assert.Equal("mark-v2", body.GetProperty("supersededById").GetString());
        }

        using (var newDetail = Authed(
                   HttpMethod.Get, $"/api/v1/admin/evaluations/{session.Value}/mark-v2", token))
        {
            var response = await client.SendAsync(newDetail);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await BodyOf(response);
            Assert.True(body.GetProperty("isCurrent").GetBoolean());
            Assert.Equal("mark-v1", body.GetProperty("supersedesId").GetString());
            Assert.Equal(2, body.GetProperty("version").GetInt32());
        }

        using (var currentOnly = Authed(
                   HttpMethod.Get,
                   $"/api/v1/admin/evaluations?current=true",
                   token))
        {
            var response = await client.SendAsync(currentOnly);
            var body = await BodyOf(response);
            var mine = body.GetProperty("items").EnumerateArray()
                .Where(i => i.GetProperty("sessionId").GetString() == session.Value)
                .ToArray();
            var only = Assert.Single(mine);
            Assert.Equal("mark-v2", only.GetProperty("markingId").GetString());
        }
    }

    [SkippableFact]
    public async Task List_filters_return_deterministic_pages_on_repeat()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var marker = $"det-{Guid.NewGuid():n}";
        for (var i = 0; i < 4; i++)
            await SeedMarkingAsync(ExamSessionId.New(), $"{marker}-{i}", version: 1);

        var client = _app.CreateClient();
        var token = Token(PermissionKeys.EvaluationRead);
        var path = "/api/v1/admin/evaluations?module=writing&page=1";

        var first = await BodyOf(await client.SendAsync(Authed(HttpMethod.Get, path, token)));
        var second = await BodyOf(await client.SendAsync(Authed(HttpMethod.Get, path, token)));

        var firstIds = first.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("markingId").GetString()).ToArray();
        var secondIds = second.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("markingId").GetString()).ToArray();

        Assert.Equal(firstIds, secondIds);
    }

    [SkippableFact]
    public async Task Invalid_filters_are_rejected_with_a_400_not_a_500()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var client = _app.CreateClient();
        var token = Token(PermissionKeys.EvaluationRead);

        using (var badModule = Authed(
                   HttpMethod.Get, "/api/v1/admin/evaluations?module=not-a-module", token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(badModule)).StatusCode);
        }

        using (var badRange = Authed(
                   HttpMethod.Get, "/api/v1/admin/evaluations?from=2026-09-21&to=2026-01-01", token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(badRange)).StatusCode);
        }

        using (var badFlagged = Authed(
                   HttpMethod.Get, "/api/v1/admin/evaluations?flagged=maybe", token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(badFlagged)).StatusCode);
        }
    }

    [SkippableFact]
    public async Task Detail_404s_on_an_unknown_marking_id_rather_than_a_stray_row()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var session = ExamSessionId.New();
        await SeedMarkingAsync(session, "mark-exists", version: 1);

        var client = _app.CreateClient();
        using var request = Authed(
            HttpMethod.Get, $"/api/v1/admin/evaluations/{session.Value}/does-not-exist",
            Token(PermissionKeys.EvaluationRead));
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(request)).StatusCode);
    }

    // ── Acceptance 2 — evaluation.rerun independence and idempotent replay ──

    [SkippableFact]
    public async Task Rerun_requires_its_own_permission_independent_of_evaluation_read()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var operationId = await SeedFailedJobAsync(ExamSessionId.New());
        var client = _app.CreateClient();

        using var request = Authed(
            HttpMethod.Post, $"/api/v1/admin/evaluations/failed-jobs/{operationId}/rerun",
            Token(PermissionKeys.EvaluationRead));
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _app.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IMarkingOutbox>();
        var job = (await outbox.ListAsync(ExamSessionId.New(), default)).SingleOrDefault();
        // Nothing about the seeded job changed — the 403 happened before any
        // outbox transition.
        var jobs = await outbox.QueryAsync(
            new MarkingJobQuery(MarkingJobState.Failed, PageSize: 50), default);
        Assert.Contains(jobs.Items, j => j.OperationId == operationId && j.Attempts == 3);
    }

    [SkippableFact]
    public async Task Rerun_needs_an_idempotency_key()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var operationId = await SeedFailedJobAsync(ExamSessionId.New());
        var client = _app.CreateClient();

        using var request = Authed(
            HttpMethod.Post, $"/api/v1/admin/evaluations/failed-jobs/{operationId}/rerun",
            Token(PermissionKeys.EvaluationRerun));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// <b>The generic <see cref="IdempotencyMiddleware"/>, not the outbox's own
    /// reopen-key tracking, is what actually answers a replay at this layer.</b>
    /// It caches the whole HTTP response by the <c>Idempotency-Key</c> header
    /// and returns it verbatim without re-entering the handler — so a same-key
    /// repeat never reaches <c>IMarkingOutbox.ReopenFailedAsync</c> a second
    /// time at all, and the response body a caller sees is byte-identical, not
    /// merely equivalent. (The outbox's own <c>ReopenKey</c> comparison, proven
    /// separately in <c>MarkingOutboxReopenTests</c>, exists as a second layer
    /// for a caller that reaches the store directly — a worker, or a request
    /// whose cached entry already expired — not as what this HTTP path relies
    /// on day to day.) What must hold here is the caller-visible guarantee:
    /// one transition, one audit row, one cost.
    /// </summary>
    [SkippableFact]
    public async Task Replaying_the_same_idempotency_key_returns_the_cached_response_and_does_not_enqueue_or_audit_twice()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var session = ExamSessionId.New();
        var operationId = await SeedFailedJobAsync(session);
        var client = _app.CreateClient();
        var token = Token(PermissionKeys.EvaluationRerun);
        var key = Guid.NewGuid().ToString("n");

        async Task<(HttpStatusCode Status, string Body)> RerunAsync()
        {
            using var request = Authed(
                HttpMethod.Post, $"/api/v1/admin/evaluations/failed-jobs/{operationId}/rerun", token);
            request.Headers.Add("Idempotency-Key", key);
            var response = await client.SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        var first = await RerunAsync();
        Assert.Equal(HttpStatusCode.Accepted, first.Status);
        using (var firstBody = JsonDocument.Parse(first.Body))
            Assert.False(firstBody.RootElement.GetProperty("replayed").GetBoolean());

        var second = await RerunAsync();
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Body, second.Body);

        using var scope = _app.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IMarkingOutbox>();
        var jobs = await outbox.QueryAsync(new MarkingJobQuery(PageSize: 50), default);
        var job = jobs.Items.Single(j => j.OperationId == operationId);
        // Reopened exactly once: still Pending (or already claimed), never
        // Failed a second time.
        Assert.NotEqual(MarkingJobState.Failed, job.State);
        Assert.Equal(key, job.ReopenKey);

        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var (entries, _) = await audit.ListAsync(
            null, nameof(AuditAction.EvaluationRerunRequested), 0, 50, default);
        Assert.Single(entries, e => e.TargetId == operationId);
    }

    /// <summary>
    /// The outbox's own reopen-key replay detection — the layer that matters
    /// once a caller reaches <see cref="IMarkingOutbox"/> directly, past
    /// whatever the HTTP-level cache in <see cref="IdempotencyMiddleware"/>
    /// did or did not retain.
    /// </summary>
    [SkippableFact]
    public async Task The_outbox_itself_treats_a_repeated_reopen_key_as_a_replay()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var operationId = await SeedFailedJobAsync(ExamSessionId.New());
        var key = Guid.NewGuid().ToString("n");

        using var scope = _app.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IMarkingOutbox>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var first = await outbox.ReopenFailedAsync(operationId, key, clock.UtcNow, default);
        var second = await outbox.ReopenFailedAsync(operationId, key, clock.UtcNow, default);

        Assert.Equal(MarkingJobReopenStatus.Reopened, first.Status);
        Assert.Equal(MarkingJobReopenStatus.Replayed, second.Status);
        Assert.Equal(first.Job?.OperationId, second.Job?.OperationId);
    }

    [SkippableFact]
    public async Task A_different_idempotency_key_on_a_no_longer_failed_job_is_a_conflict()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var operationId = await SeedFailedJobAsync(ExamSessionId.New());
        var client = _app.CreateClient();
        var token = Token(PermissionKeys.EvaluationRerun);

        using (var first = Authed(
                   HttpMethod.Post, $"/api/v1/admin/evaluations/failed-jobs/{operationId}/rerun", token))
        {
            first.Headers.Add("Idempotency-Key", "key-a");
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(first)).StatusCode);
        }

        using var second = Authed(
            HttpMethod.Post, $"/api/v1/admin/evaluations/failed-jobs/{operationId}/rerun", token);
        second.Headers.Add("Idempotency-Key", "key-b");
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(second)).StatusCode);
    }

    [SkippableFact]
    public async Task Failed_jobs_queue_is_paged_and_permission_gated()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        await SeedFailedJobAsync(ExamSessionId.New());

        var client = _app.CreateClient();

        using (var denied = Authed(
                   HttpMethod.Get, "/api/v1/admin/evaluations/failed-jobs", Token()))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(denied)).StatusCode);
        }

        using var allowed = Authed(
            HttpMethod.Get, "/api/v1/admin/evaluations/failed-jobs", Token(PermissionKeys.EvaluationRead));
        var response = await client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyOf(response);
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 1);
        Assert.True(body.GetProperty("pageSize").GetInt32() <= 50);
    }

    [SkippableFact]
    public async Task Anonymous_requests_are_unauthorized_not_forbidden()
    {
        Skip.IfNot(EvaluationAppFactory.MongoAvailable, EvaluationAppFactory.SkipReason);

        var client = _app.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/evaluations");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Seeding helpers ──────────────────────────────────────────────────

    private async Task SeedMarkingAsync(
        ExamSessionId session, string markingId, int version, string rubricVersion = "writing-v2")
    {
        using var scope = _app.Services.CreateScope();
        var markings = scope.ServiceProvider.GetRequiredService<ISectionMarkingStore>();
        await markings.SaveAsync(session, new SectionMarking(
            ExamModule.Writing,
            rubricVersion,
            [
                CriterionAssessment.Create(
                    "taskResponse", BandScore.Create(6.5m), "Đủ ý.",
                    ["space research delivers practical benefits"]),
            ],
            BandScore.Create(6.5m),
            BandScore.Create(6.5m),
            [],
            [],
            TaskNumber: 2,
            MarkingId: markingId), default);
    }

    private async Task SeedSubmissionAsync(ExamSessionId session, string essay)
    {
        using var scope = _app.Services.CreateScope();
        var answers = scope.ServiceProvider.GetRequiredService<IAnswerSheetStore>();
        await answers.SetAnswerAsync(
            session, ExamModule.Writing, "w-task-2-slot-1", essay, DateTimeOffset.UtcNow, default);
    }

    private async Task SeedAttemptAsync(
        string operationId, ExamSessionId session, ExamModule module, int? taskNumber,
        EvaluationAttemptOutcome outcome, string? rawOutput, string? markingId, int? markingVersion)
    {
        using var scope = _app.Services.CreateScope();
        var attempts = scope.ServiceProvider.GetRequiredService<IEvaluationAttemptStore>();
        await attempts.RecordAsync(EvaluationAttempt.Capture(
            operationId, session, module, taskNumber, "OpenAi", "deepseek-v4-pro", "req-1",
            DateTimeOffset.UtcNow.AddSeconds(-10), DateTimeOffset.UtcNow, outcome,
            "CRITERION_SET_MISMATCH", "Missing: taskResponse. Unexpected: taskAchievement.",
            rawOutput) with
        {
            MarkingId = markingId,
            MarkingVersion = markingVersion,
        }, default);
    }

    /// <summary>Enqueue → claim → fail, so the job lands in <see cref="MarkingJobState.Failed"/>.</summary>
    private async Task<string> SeedFailedJobAsync(ExamSessionId session)
    {
        using var scope = _app.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IMarkingOutbox>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var operationId = MarkingJob.IdFor(session, ExamModule.Writing, "writing-v2");
        var now = clock.UtcNow;

        await outbox.EnqueueAsync(new MarkingJob(
            operationId, session, ExamModule.Writing, "writing-v2", MarkingJobState.Pending,
            Attempts: 0, CreatedAt: now, NextAttemptAt: now, LeaseUntil: null, LeaseToken: null,
            LastError: null, CompletedAt: null), default);

        var leaseToken = Guid.NewGuid().ToString("n");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var claimed = await outbox.ClaimAsync(leaseToken, clock.UtcNow, TimeSpan.FromMinutes(5), default);
            Assert.NotNull(claimed);
            if (attempt < 2)
                await outbox.RetryAsync(operationId, leaseToken, clock.UtcNow, "transient", default);
            else
                await outbox.FailAsync(operationId, leaseToken, "Rejected: missing taskAchievement", default);
        }

        return operationId;
    }

    // ── HTTP helpers ─────────────────────────────────────────────────────

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Token(params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, $"eval-verify-operator-{Guid.NewGuid():n}"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n")),
            new("name", "Eval Verification"),
        };
        claims.AddRange(permissions.Select(p => new Claim("perm", p)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(EvaluationAppFactory.JwtSigningKey)),
            SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: "vni-ielts",
            audience: "vni-ielts-clients",
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, string access)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return request;
    }
}

/// <summary>
/// Boots the real API with a throwaway Mongo database — no SSO, no AI
/// provider config: this slice tests admin reads and outbox transitions, not
/// marking itself.
/// </summary>
public sealed class EvaluationAppFactory : WebApplicationFactory<Program>
{
    public const string JwtSigningKey = "EVAL_VERIFY_SENTINEL_SIGNING_KEY_xxxxxxxx";

    private readonly string _database = $"vni_ielts_api_eval_{Guid.NewGuid():n}";

    private static readonly Lazy<(bool Ok, int Port)> _mongo = new(() =>
    {
        foreach (var port in new[] { 27018, 27017 })
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var client = new MongoClient(new MongoClientSettings
                    {
                        Server = new MongoServerAddress("localhost", port),
                        DirectConnection = true,
                        ServerSelectionTimeout = TimeSpan.FromSeconds(3),
                        ConnectTimeout = TimeSpan.FromSeconds(3),
                    });
                    client.ListDatabaseNames().MoveNext();
                    return (true, port);
                }
                catch
                {
                    if (attempt < 3) Thread.Sleep(TimeSpan.FromSeconds(1));
                }
            }
        }

        return (false, 27018);
    });

    public static bool MongoAvailable => _mongo.Value.Ok;

    public const string SkipReason =
        "No MongoDB on localhost:27018 or :27017. Start it with "
        + "`docker compose -f infra/docker/compose.yaml up -d`.";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);

        builder.UseSetting(
            "Mongo:ConnectionString",
            $"mongodb://localhost:{_mongo.Value.Port}/?directConnection=true");
        builder.UseSetting("Mongo:Database", _database);
        builder.UseSetting("Jwt:SigningKey", JwtSigningKey);
        builder.UseSetting("Sso:EnableStubProvider", "true");
        builder.UseSetting("Sso:Google:ClientId", string.Empty);
        builder.UseSetting("Sso:Google:ClientSecret", string.Empty);
        builder.UseSetting("Sso:ClientBaseUrl", "http://localhost:5173");
        builder.UseSetting("Sso:Google:RedirectUri", "http://localhost/api/v1/auth/sso/google/callback");
    }

    public override async ValueTask DisposeAsync()
    {
        if (MongoAvailable)
        {
            await new MongoClient($"mongodb://localhost:{_mongo.Value.Port}/?directConnection=true")
                .DropDatabaseAsync(_database);
        }

        await base.DisposeAsync();
    }
}
