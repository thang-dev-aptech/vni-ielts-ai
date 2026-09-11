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

  /// <summary>
  /// The gate this task's coordinator asked to be enforced inside the
  /// workflow, not only by whoever calls it. Called directly here — bypassing
  /// <c>ExamPackageImportPipeline</c>'s own call-site check entirely — so the
  /// proof is that the workflow refuses on its own, not that its one existing
  /// caller happens to be careful.
  /// </summary>
  [Fact]
  public async Task A_package_whose_policy_forbids_explanations_is_refused_even_called_directly()
  {
    var generator = new CountingExplanationGenerator();
    var cache = new InMemoryCanonicalExplanationCache();
    var workflow = new CanonicalExplanationWorkflow(generator, cache);
    var version = SampleVersion();
    var draft = SampleDraft(version, policyMode: "none");

    var result = await workflow.EnrichDraftAsync(draft, default);

    Assert.Equal(0, generator.CallCount);
    Assert.Equal(0, result.Generated);
    Assert.Equal(0, result.Cached);
    Assert.Equal(0, result.Refused);
    Assert.Same(draft, result.Draft);
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

  private static ExamImportDraft SampleDraft(ExamVersion version, string policyMode = "ai-generated")
  {
    var packageJson = $$"""{ "policyProfile": { "explanation": { "mode": "{{policyMode}}" } } }""";
    return new(
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
        packageJson,
        packageJson,
        ImportReviewChecklist.Empty,
        [],
        0,
        null);
  }

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

    /// <summary>
    /// The twin of the import pipeline's own gate, and the same bug.
    /// <c>"mode": 3</c> parses and then throws
    /// <see cref="InvalidOperationException"/> out of
    /// <c>GetValue&lt;string&gt;()</c>, escaping a method documented as never
    /// throwing. Every unreadable answer refuses; the one value that means yes
    /// still means yes.
    /// </summary>
    [Theory]
    [InlineData("""{ "policyProfile": { "explanation": { "mode": 3 } } }""", false)]
    [InlineData("""{ "policyProfile": { "explanation": { "mode": true } } }""", false)]
    [InlineData("""{ "policyProfile": { "explanation": { "mode": "authored" } } }""", false)]
    [InlineData("""not json at all""", false)]
    [InlineData("""{ "policyProfile": { "explanation": { "mode": "ai-generated" } } }""", true)]
    public void The_generation_gate_refuses_every_mode_it_cannot_read(string packageJson, bool allowed) =>
        Assert.Equal(allowed, CanonicalExplanationWorkflow.AllowsAiGeneration(packageJson));
}
