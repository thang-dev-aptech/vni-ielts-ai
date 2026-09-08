using MongoDB.Bson;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests.Exams;

/// <summary>
/// What a stored extraction run keeps, and what it has no room to keep.
/// </summary>
public sealed class ExamExtractionRunMapperTests
{
    private static readonly DateTimeOffset RequestedAt =
        new(2026, 9, 7, 16, 30, 0, TimeSpan.FromHours(7));

    [Fact]
    public void A_successful_run_round_trips_without_losing_a_field()
    {
        var run = Run();

        var restored = run.ToDocument().ToDomain();

        Assert.Equal(run, restored with
        {
            RequestedAt = run.RequestedAt,
            CompletedAt = run.CompletedAt,
        });

        Assert.Equal(run.RequestedAt.ToUniversalTime(), restored.RequestedAt);
        Assert.Equal(TimeSpan.Zero, restored.RequestedAt.Offset);
        Assert.Equal(run.CompletedAt.ToUniversalTime(), restored.CompletedAt);
    }

    [Fact]
    public void A_refused_run_keeps_its_code_and_stores_no_candidate()
    {
        var run = Run() with
        {
            CandidateId = null,
            FailureCode = ExamExtractionRejection.SchemaInvalid,
        };

        var document = run.ToDocument();
        var restored = document.ToDomain();

        Assert.Null(restored.CandidateId);
        Assert.Equal(ExamExtractionRejection.SchemaInvalid, restored.FailureCode);

        // BsonIgnoreIfNull: an absent candidate is an absent element, not a
        // stored null that a query for "runs without a candidate" would miss.
        Assert.False(document.ToBsonDocument().Contains("candidateId"));
    }

    /// <summary>
    /// <b>The check is on the stored document, not on the mapper.</b> A run
    /// record is read by operators and support, so "we do not write the source
    /// text" is not enough — there must be no element to write it into.
    /// </summary>
    [Fact]
    public void The_stored_document_has_no_element_for_source_text_a_prompt_or_a_key()
    {
        var elements = Run().ToDocument().ToBsonDocument().Names.ToArray();

        Assert.Equal(
            [
                "_id", "packageId", "candidateId", "provider", "model", "promptVersion",
                "schemaId", "contractVersion", "requestId", "inputHash", "sourceCount",
                "inputTokens", "outputTokens", "requestedAt", "completedAt",
            ],
            elements);

        foreach (var forbidden in new[]
                 {
                     "sourceText", "prompt", "systemPrompt", "answerKey", "apiKey",
                     "response", "payload", "entryPath", "uploadedBy",
                 })
        {
            Assert.DoesNotContain(forbidden, elements, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A worker restarted mid-flight, or a queue redelivery, replaces its own
    /// record rather than writing a second one that reads as a second call.
    /// </summary>
    [Fact]
    public void Package_and_request_together_are_the_identity()
    {
        Assert.Equal("package-1:provider-request-1", Run().ToDocument().Id);

        Assert.NotEqual(
            Run().ToDocument().Id,
            (Run() with { RequestId = "provider-request-2" }).ToDocument().Id);
    }

    private static ExamExtractionRunMetadata Run() => new(
        "package-1",
        "candidate-1",
        "OpenAi",
        "model-under-test",
        "exam-extraction-prompt-v1",
        ExamExtractionContract.SchemaId,
        ExamExtractionContract.Version,
        "provider-request-1",
        new string('a', 64),
        2,
        1100,
        2200,
        RequestedAt,
        RequestedAt.AddSeconds(9),
        null);
}
