using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Explanations;

public sealed record RequestPersonalizedExplanationCommand(
    UserId UserId,
    ExamSessionId SessionId,
    string QuestionId,
    string OperationId);

public sealed record PersonalizedExplanationView(
    string QuestionId,
    string State,
    int Attempts,
    string? Reason,
    ExplanationContentView? Explanation);

public sealed record ExplanationContentView(
    string CorrectAnswer,
    string ShortReason,
    IReadOnlyList<string> Evidence,
    string? CommonMistake,
    string? Translation);

/// <summary>
/// On-demand personalized explanations after submit. Quota enforced by rate
/// limiting at the endpoint; cache prevents duplicate provider calls.
/// </summary>
public sealed class PersonalizedExplanationService(
    IExamCatalogue catalogue,
    IExamSessionRepository sessions,
    IAnswerSheetStore answers,
    IPersonalizedExplanationStore store,
    IReadingListeningExplanationGenerator generator,
    IClock clock,
    Usage.UsageRecorder? usage = null)
{
    public const int MaxAttempts = 3;

    /// <summary>
    /// A job still "Running" after this long is dead, not busy.
    ///
    /// <b><c>[QUYẾT ĐỊNH kỹ thuật]</c></b> The provider call happens inside the
    /// learner's own HTTP request, so a job is only ever Running for as long
    /// as one call takes — and the explanation HttpClient is clamped to at
    /// most 300 s. A record older than that with no outcome belongs to a
    /// process that was killed mid-call (a deploy, a crash, a dev restart),
    /// and before this seam existed it made the question say "Đang tạo giải
    /// thích…" forever, because every later request found it Running and
    /// returned it untouched. Past this window the request runs the job
    /// again; the cost of being wrong is one duplicate provider call.
    /// </summary>
    public static readonly TimeSpan StaleRunningAfter = TimeSpan.FromMinutes(5);

    public async Task<PersonalizedExplanationView> RequestAsync(
        RequestPersonalizedExplanationCommand command, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(
            catalogue, command.SessionId, command.UserId, ct);

        if (session.Status != SessionStatus.Submitted && session.Status != SessionStatus.Expired)
            throw new InvalidOperationException("EXPLANATION_SESSION_NOT_SUBMITTED");

        var question = FindQuestion(version, command.QuestionId)
            ?? throw new InvalidOperationException("EXPLANATION_QUESTION_NOT_FOUND");

        var section = version.Sections.First(s => s.Questions.Any(q => q.Id == command.QuestionId));
        if (section.Module is not (ExamModule.Reading or ExamModule.Listening))
            throw new InvalidOperationException("EXPLANATION_MODULE_NOT_ELIGIBLE");

        var part = section.Parts.First(p => p.Questions.Any(q => q.Id == command.QuestionId));
        var sheet = await answers.ReadAsync(session.Id, section.Module, ct);
        var submitted = ResolveSubmitted(question, sheet.Answers);

        if (question.Explanation is { } canonical)
        {
            return ToView(
                command.QuestionId,
                ExplanationJobState.Ready,
                0,
                null,
                ToContent(canonical));
        }

        var answerHash = ExplanationAnswerHash.Compute(submitted);
        var operationId = string.IsNullOrWhiteSpace(command.OperationId)
            ? PersonalizedExplanationOperation.IdFor(session.Id, command.QuestionId, answerHash)
            : command.OperationId;

        var existing = await store.FindByOperationAsync(operationId, ct)
            ?? await store.FindReadyAsync(session.Id, command.QuestionId, answerHash, ct);

        if (existing is { State: ExplanationJobState.Ready, Content: not null })
            return ToView(existing);

        if (existing is { State: ExplanationJobState.Failed } failed && failed.Attempts >= MaxAttempts)
            return ToView(failed);

        var now = clock.UtcNow;

        if (existing is { State: ExplanationJobState.Pending or ExplanationJobState.Running }
            && now - (existing.StartedAt ?? existing.CreatedAt) < StaleRunningAfter)
            return ToView(existing);

        var job = existing ?? new PersonalizedExplanationJob(
            operationId,
            session.Id,
            version.Id,
            command.QuestionId,
            answerHash,
            ExplanationJobState.Pending,
            null,
            null,
            Attempts: 0,
            null,
            now,
            null);

        if (existing is null)
        {
            // The first provider call is attempt one. Left at zero, the cap
            // of three allowed a fourth call — the job went Running with
            // `Attempts: 0`, failed at 0, and only the retries were counted.
            job = job with { State = ExplanationJobState.Running, Attempts = 1, StartedAt = now };
            if (!await store.TryInsertAsync(job, ct))
            {
                var raced = await store.FindByOperationAsync(operationId, ct);
                return raced is null
                    ? throw new InvalidOperationException("EXPLANATION_CONFLICT")
                    : ToView(raced);
            }
        }
        else
        {
            job = job with
            {
                State = ExplanationJobState.Running,
                Attempts = job.Attempts + 1,
                StartedAt = now,
                Error = null,
            };
            await store.UpdateAsync(job, ct);
        }

        var expected = FormatExpectedAnswer(question);
        var source = section.Module == ExamModule.Reading
            ? new EvidenceSourceContext(part.Body, null)
            : new EvidenceSourceContext(null, part.Transcript);

        // Learner answer is data — strip delimiter sequences before any provider
        // call. Identity never travels: ExplanationGenerationRequest has no
        // UserId/email fields (PDPL T12).
        var safeSubmitted = ExplanationPromptSafety.SanitizeLearnerAnswer(submitted);

        try
        {
            var result = await generator.GenerateAsync(
                new ExplanationGenerationRequest(
                    section.Module,
                    question.Id,
                    ExplanationQuestionText.Compose(question),
                    expected,
                    safeSubmitted,
                    source.PassageBody ?? source.Transcript,
                    Personalized: true,
                    QuestionOptions: ExplanationPromptSafety.FormatOptions(question.Options)),
                ct);

            // The generator was asked, so the ledger records a use whether or
            // not the answer validates — the provider call is what costs.
            // Canonical and cached explanations never reach this line. → `P-14`
            if (usage is not null)
            {
                await usage.ExplanationGeneratedAsync(
                    command.UserId, session.Id, result.Metadata?.Provider, result.Metadata?.Model, ct);
            }

            if (!result.IsSuccess || result.RawJson is null)
            {
                var failedJob = job with
                {
                    State = ExplanationJobState.Failed,
                    Error = result.RefusalCode ?? "EXPLANATION_PROVIDER_FAILED",
                    CompletedAt = now,
                };
                await store.UpdateAsync(failedJob, ct);
                return ToView(failedJob);
            }

            var validation = ExplanationOutputValidator.Validate(
                result.RawJson, expected, section.Module, source);

            if (!validation.IsValid || validation.Explanation is null)
            {
                var failedJob = job with
                {
                    State = ExplanationJobState.Failed,
                    Error = validation.RefusalCode,
                    CompletedAt = now,
                };
                await store.UpdateAsync(failedJob, ct);
                return ToView(failedJob);
            }

            var ready = job with
            {
                State = ExplanationJobState.Ready,
                Content = validation.Explanation,
                Metadata = result.Metadata,
                CompletedAt = now,
                Error = null,
            };
            await store.UpdateAsync(ready, ct);
            return ToView(ready);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var timedOut = job with
            {
                State = ExplanationJobState.Failed,
                Error = "EXPLANATION_PROVIDER_TIMEOUT",
                CompletedAt = now,
            };
            await store.UpdateAsync(timedOut, ct);
            return ToView(timedOut);
        }
        catch (OperationCanceledException)
        {
            // The learner's request went away mid-call — a closed tab, a
            // navigation. The job must not stay Running: the next request
            // for this question would find it and wait on nothing. Written
            // with no token, because the one we were given is the cancelled
            // one.
            await store.UpdateAsync(
                job with
                {
                    State = ExplanationJobState.Failed,
                    Error = "EXPLANATION_REQUEST_ABORTED",
                    CompletedAt = now,
                },
                CancellationToken.None);
            throw;
        }
    }

    public static IReadOnlyList<QuestionExplanationStatusView> ProjectStatuses(
        ExamVersion version,
        IReadOnlyList<PersonalizedExplanationJob> jobs)
    {
        /*
         * One job per question, chosen rather than assumed. A question can
         * hold several jobs — one per operation id, one per answer hash —
         * and `ToDictionary` threw on the second, which took the whole
         * results page down with a 500. A Ready job outranks any other;
         * among the rest the newest is the one the learner is waiting on.
         */
        var byQuestion = jobs
            .GroupBy(j => j.QuestionId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(j => j.State == ExplanationJobState.Ready)
                      .ThenByDescending(j => j.StartedAt ?? j.CreatedAt)
                      .First());
        var statuses = new List<QuestionExplanationStatusView>();

        foreach (var section in version.Sections.Where(s =>
                     s.Module is ExamModule.Reading or ExamModule.Listening))
        {
            foreach (var question in section.Questions.Where(q => q.Type.IsAutoScored()))
            {
                if (question.Explanation is not null)
                {
                    statuses.Add(new QuestionExplanationStatusView(
                        question.Id,
                        section.Module.ToString().ToLowerInvariant(),
                        "ready",
                        0,
                        null));
                    continue;
                }

                if (byQuestion.TryGetValue(question.Id, out var job))
                {
                    statuses.Add(new QuestionExplanationStatusView(
                        question.Id,
                        section.Module.ToString().ToLowerInvariant(),
                        job.State.ToString().ToLowerInvariant(),
                        job.Attempts,
                        SafeReason(job)));
                    continue;
                }

                statuses.Add(new QuestionExplanationStatusView(
                    question.Id,
                    section.Module.ToString().ToLowerInvariant(),
                    "none",
                    0,
                    null));
            }
        }

        return statuses;
    }

    private static Question? FindQuestion(ExamVersion version, string questionId) =>
        version.Sections.SelectMany(s => s.Questions).FirstOrDefault(q => q.Id == questionId);

    private static string? ResolveSubmitted(
        Question question, IReadOnlyDictionary<string, string?> answers)
    {
        if (answers.TryGetValue(question.Id, out var direct))
            return direct;

        if (question.Slots is not { Count: > 0 } slots) return null;

        var parts = slots
            .OrderBy(s => s.Number)
            .Select(s => answers.TryGetValue(s.Id, out var v) ? v : null);

        return string.Join('|', parts.Select(v => v ?? string.Empty));
    }

    private static string FormatExpectedAnswer(Question question)
    {
        if (question.Slots is { Count: > 0 } slots)
        {
            return string.Join(
                " / ",
                slots.OrderBy(s => s.Number)
                    .Select(s => AnswerMatcher.FormatAcceptedAnswer(s.AnswerKey, AnswerMatchingRules.Default)
                             ?? string.Empty));
        }

        return AnswerMatcher.FormatAcceptedAnswer(question.AnswerKey, AnswerMatchingRules.Default)
               ?? string.Empty;
    }

    private static ExplanationContentView ToContent(QuestionExplanation explanation) =>
        new(explanation.CorrectAnswer ?? string.Empty, explanation.ShortReason, explanation.Evidence,
            explanation.CommonMistake, explanation.Translation);

    private static PersonalizedExplanationView ToView(PersonalizedExplanationJob job) =>
        ToView(job.QuestionId, job.State, job.Attempts, SafeReason(job),
            job.Content is null ? null : new ExplanationContentView(
                job.Content.CorrectAnswer,
                job.Content.ShortReason,
                job.Content.Evidence,
                job.Content.CommonMistake,
                job.Content.Translation));

    private static PersonalizedExplanationView ToView(
        string questionId,
        ExplanationJobState state,
        int attempts,
        string? reason,
        ExplanationContentView? content) =>
        new(questionId, state.ToString().ToLowerInvariant(), attempts, reason, content);

    private static string? SafeReason(PersonalizedExplanationJob job) =>
        job.State switch
        {
            ExplanationJobState.Ready => null,
            ExplanationJobState.Pending or ExplanationJobState.Running =>
                "Đang tạo giải thích cá nhân.",
            // The cap is final: RequestAsync returns the failed job without
            // another provider call, so a "try again" message here would invite
            // a retry that can never succeed.
            ExplanationJobState.Failed when job.Attempts >= MaxAttempts =>
                $"Đã thử {job.Attempts} lần nhưng chưa tạo được giải thích cho câu này.",
            ExplanationJobState.Failed when job.Error == "EXPLANATION_PROVIDER_TIMEOUT" =>
                "Giải thích chưa sẵn sàng do hệ thống bận. Bạn có thể thử lại.",
            ExplanationJobState.Failed => "Giải thích chưa sẵn sàng. Bạn có thể thử lại.",
            _ => null,
        };
}

public sealed record QuestionExplanationStatusView(
    string QuestionId,
    string Module,
    string State,
    int Attempts,
    string? Reason);
