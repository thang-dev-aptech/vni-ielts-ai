using System.Reflection;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// FS9.1 — sitting views and AI request shapes cannot carry answer keys,
/// explanations, or learner identity to a provider. <see cref="PartView"/>
/// now carries a transcript field (`IP-09`), so its guarantee narrowed: the
/// field exists, but stays null unless the one call site that builds
/// <see cref="SessionResultsView.Content"/> deliberately populates it.
/// </summary>
public sealed class FunctionalCoreSecurityTests
{
    [Fact]
    public void Sitting_question_view_has_no_answer_key_or_explanation_slot()
    {
        var names = typeof(QuestionView).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("AnswerKey", names);
        Assert.DoesNotContain("Explanation", names);
        Assert.DoesNotContain("CorrectAnswer", names);
        Assert.DoesNotContain("Transcript", names);
    }

    /// <summary>
    /// `IP-09` gave <see cref="PartView"/> a transcript field on purpose —
    /// the client needs it after submission. The security property that
    /// matters now is not its absence but its default: a caller that does
    /// not deliberately pass one gets null, the same as before the field
    /// existed. <see cref="ExamHandlers"/> proves the one place that opts in.
    /// </summary>
    [Fact]
    public void Sitting_part_view_carries_a_transcript_field_that_defaults_to_null()
    {
        var names = typeof(PartView).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Transcript", names);

        var part = new SectionPart(
            1, "listening", "Part 1", null, "audio/1.mp3", null,
            "SECRET_TRANSCRIPT_THE_CLIENT_MUST_NOT_SEE",
            null, null, null, null, []);

        Assert.Null(part.ToView().Transcript);
    }

    [Fact]
    public void Part_projection_drops_transcript_even_when_the_domain_part_has_one()
    {
        var part = new SectionPart(
            1, "listening", "Part 1", null, "audio/1.mp3", null,
            "SECRET_TRANSCRIPT_THE_CLIENT_MUST_NOT_SEE",
            null, null, null, null,
            [new Question("l-1", 1, QuestionType.Completion, "Write one word", [], null,
                new AnswerKey([new AcceptedAnswer("paper", null, null)], null))]);

        var view = part.ToView();
        var serialised = System.Text.Json.JsonSerializer.Serialize(view);

        Assert.DoesNotContain("SECRET_TRANSCRIPT_THE_CLIENT_MUST_NOT_SEE", serialised, StringComparison.Ordinal);
        Assert.DoesNotContain("paper", serialised, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Question_projection_drops_answer_key_and_authored_explanation()
    {
        var question = new Question(
            "r-1", 1, QuestionType.MultipleChoice, "Pick one",
            [new QuestionOption("A", "Alpha"), new QuestionOption("B", "Beta")],
            null,
            new AnswerKey([new AcceptedAnswer("B", null, null)], null),
            Explanation: new QuestionExplanation("B", "Because the text says so.", ["span"], null));

        var view = question.ToView();
        var serialised = System.Text.Json.JsonSerializer.Serialize(view);

        Assert.DoesNotContain("Because the text says so", serialised, StringComparison.Ordinal);
        Assert.False(serialised.Contains("\"correctAnswer\"", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("AnswerKey", serialised, StringComparison.Ordinal);
    }

    [Fact]
    public void Explanation_generation_request_carries_no_learner_identity()
    {
        var forbidden = new[] { "UserId", "Email", "DisplayName", "LearnerId", "AccountId" };
        var names = typeof(ExplanationGenerationRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in forbidden)
            Assert.DoesNotContain(name, names);
    }

    [Fact]
    public void Speaking_recording_audit_detail_never_holds_a_signed_url()
    {
        var metadata = new SpeakingRecordingMetadata(
            UploadId: "upload-1",
            RecordingId: "rec-abcdef",
            ObjectKey: "recordings/abcdef0123456789abcdef0123456789",
            OwnerId: UserId.New(),
            SessionId: ExamSessionId.New(),
            QuestionId: "s-part-1",
            ContentType: "audio/webm",
            ExpectedSizeBytes: 1024,
            ExpectedChecksumSha256: new string('a', 64),
            ActualSizeBytes: 1024,
            ActualChecksumSha256: new string('a', 64),
            Status: SpeakingRecordingStatus.Linked,
            CreatedAt: DateTimeOffset.UtcNow,
            RetentionExpiresAt: DateTimeOffset.UtcNow.AddDays(90),
            LinkedAt: DateTimeOffset.UtcNow);

        var detail = SpeakingAuditDetail.ForMetadata(metadata);
        var init = SpeakingAuditDetail.ForInit(
            metadata.UploadId, metadata.RecordingId, metadata.ObjectKey, metadata.QuestionId);
        var purge = SpeakingAuditDetail.ForPurge(
            metadata.RecordingId, metadata.SessionId.Value, metadata.QuestionId);

        Assert.False(SpeakingAuditDetail.LooksLikeSignedUrlLeak(detail));
        Assert.False(SpeakingAuditDetail.LooksLikeSignedUrlLeak(init));
        Assert.False(SpeakingAuditDetail.LooksLikeSignedUrlLeak(purge));
        Assert.Equal(metadata.ObjectKey, detail["objectKey"]);
        Assert.DoesNotContain(detail.Keys, k => k.Contains("url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LooksLikeSignedUrlLeak_catches_query_credentials()
    {
        var leak = new Dictionary<string, string>
        {
            ["uploadUrl"] =
                "https://storage.example.com/recordings/x?X-Amz-Signature=FAKE&X-Amz-Credential=FAKE",
        };

        Assert.True(SpeakingAuditDetail.LooksLikeSignedUrlLeak(leak));
        Assert.Throws<ArgumentException>(() =>
            SpeakingAuditDetail.RejectLongLivedAudioUrls(leak));
    }
}
