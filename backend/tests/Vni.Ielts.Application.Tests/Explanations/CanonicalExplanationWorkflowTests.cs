using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Explanations;

public sealed class CanonicalExplanationWorkflowTests
{
  [Fact]
  public async Task Cache_prevents_second_provider_call_for_same_question()
  {
    var generator = new CountingExplanationGenerator();
    var cache = new InMemoryCanonicalExplanationCache();
    var workflow = new CanonicalExplanationWorkflow(generator, cache);
    var version = SampleVersion();
    var draft = SampleDraft(version);

    var first = await workflow.EnrichDraftAsync(draft, default);
    Assert.True(first.Generated >= 1);

    generator.ResetCount();
    var second = await workflow.EnrichDraftAsync(first.Draft, default);

    Assert.Equal(0, generator.CallCount);
    Assert.True(second.Cached >= 1);
  }

  private static ExamVersion SampleVersion()
  {
    var question = new Question(
        "q1",
        1,
        QuestionType.MultipleChoice,
        "What is the answer?",
        [],
        null,
        new AnswerKey([new AcceptedAnswer("B", null, null)], null));

    var part = new SectionPart(
        1, "passage", null, "Passage with sample passage evidence inside.", null, null, null,
        null, null, null, null, [question]);

    var section = new Section(ExamModule.Reading, 1, [part]);

    return ExamVersion.CreateDraft(
        ExamDefinitionId.New(),
        1,
        "Sample",
        ExamVariant.Academic,
        new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
        new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
        [section]);
  }

  private static ExamImportDraft SampleDraft(ExamVersion version) =>
      new(
          Guid.NewGuid(),
          version.DefinitionId,
          version.VersionNumber,
          ExamImportRoute.StructuredPackage,
          "hash",
          "hash",
          version,
          null,
          ImportApprovalState.ReviewRequired,
          [],
          "{}",
          "{}",
          ImportReviewChecklist.Empty,
          [],
          0,
          null);

  private sealed class CountingExplanationGenerator : IReadingListeningExplanationGenerator
  {
    public int CallCount { get; private set; }

    public void ResetCount() => CallCount = 0;

    public Task<ExplanationGenerationResult> GenerateAsync(
        ExplanationGenerationRequest request, CancellationToken ct)
    {
      CallCount++;
      return Task.FromResult(new ExplanationGenerationResult(
          true,
          """
          {
            "correctAnswer": "B",
            "shortReason": "Because the text says so.",
            "evidence": ["sample passage evidence"]
          }
          """,
          new ExplanationProviderMetadata("test", "fixture", "v1", "req-1"),
          null));
    }
  }

  private sealed class InMemoryCanonicalExplanationCache : ICanonicalExplanationCache
  {
    private readonly Dictionary<string, StoredCanonicalExplanation> _entries = new();

    public Task<StoredCanonicalExplanation?> FindAsync(
        ExamVersionId versionId, string questionId, CancellationToken ct) =>
        Task.FromResult(_entries.TryGetValue($"{versionId.Value}:{questionId}", out var e) ? e : null);

    public Task SaveAsync(StoredCanonicalExplanation entry, CancellationToken ct)
    {
      _entries[$"{entry.VersionId.Value}:{entry.QuestionId}"] = entry;
      return Task.CompletedTask;
    }
  }
}
