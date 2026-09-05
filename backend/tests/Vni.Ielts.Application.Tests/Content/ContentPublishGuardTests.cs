using Vni.Ielts.Application.Content;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Content;

/// <summary>
/// The use case that stands between a publish request and the registry.
///
/// <b>The interesting case is the exam nobody registered.</b> The binding from
/// an exam back to the material it was built from lives in the registry — so
/// an exam that arrived by any route the registry does not know about resolves
/// to no source, and no source is no right.
/// </summary>
public sealed class ContentPublishGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 9, 0, 0, TimeSpan.Zero);

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeRegistry(params ContentSource[] sources) : IContentRightsRegistry
    {
        public Task<ContentSource?> FindAsync(ContentSourceId id, CancellationToken ct) =>
            Task.FromResult(sources.FirstOrDefault(s => s.Id.Value == id.Value));

        public Task<ContentSource?> FindForExamAsync(
            ExamVersionId versionId, ExamDefinitionId definitionId, CancellationToken ct) =>
            Task.FromResult(sources.FirstOrDefault(
                s => s.Produced(versionId) || s.Covers(definitionId)));

        public Task<IReadOnlyList<ContentSource>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentSource>>([.. sources]);

        public Task<bool> RegisterIfAbsentAsync(ContentSource source, CancellationToken ct) =>
            Task.FromResult(false);
    }

    private static ContentPublishGuard Guard(params ContentSource[] sources) =>
        new(new FakeRegistry(sources), new FakeClock(Now));

    /// <summary>A minimal paper. Its content is irrelevant here; its identity is not.</summary>
    private static ExamVersion Paper(ExamDefinitionId? definitionId = null) =>
        ExamVersion.Rehydrate(
            ExamVersionId.New(), definitionId ?? ExamDefinitionId.New(), 1, "Paper",
            ExamVariant.Academic, ExamVersionStatus.Draft, null,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [new Section(ExamModule.Reading, 1, [])]);

    private static ContentSource FixtureOnly(ExamVersion paper) =>
        ContentSource.Register(
            new ContentSourceId("exam1"), "Exam 1 (borrowed test bank)",
            owner: null, proof: null,
            allowedEnvironments: [ContentEnvironment.Fixture],
            expiresAt: null, rootPath: "exam/Exam1", files: [],
            boundExamVersionIds: [paper.Id], boundExamDefinitionIds: []);

    [Fact]
    public async Task An_exam_no_registry_entry_covers_is_refused()
    {
        var decision = await Guard().MayPublishToLearnersAsync(Paper(), default);

        Assert.False(decision.Allowed);
        Assert.Equal(ContentRightsDenial.NoRegistryEntry, decision.Denial);
    }

    [Fact]
    public async Task A_fixture_only_source_is_refused_for_publication()
    {
        var paper = Paper();

        var decision = await Guard(FixtureOnly(paper)).MayPublishToLearnersAsync(paper, default);

        Assert.False(decision.Allowed);
        Assert.Equal(ContentRightsDenial.EnvironmentNotGranted, decision.Denial);
        Assert.Equal("exam1", decision.SourceId);
    }

    [Fact]
    public async Task A_record_bound_by_definition_id_still_covers_a_re_fingerprinted_version()
    {
        /*
         * The seeder derives an exam version id from a fingerprint of the
         * paper, so correcting a typo mints a new version id. A registry bound
         * only to version ids would lose track of its own material on that
         * edit — refusing, which is the safe direction, but for the wrong
         * reason and with a misleading message.
         */
        var definition = new ExamDefinitionId("seed-exam-1");

        var bound = ContentSource.Register(
            new ContentSourceId("exam1"), "Exam 1", null, null,
            [ContentEnvironment.Fixture], null, "exam/Exam1", [],
            boundExamVersionIds: [], boundExamDefinitionIds: [definition]);

        var decision = await Guard(bound).MayPublishToLearnersAsync(Paper(definition), default);

        Assert.Equal(ContentRightsDenial.EnvironmentNotGranted, decision.Denial);
        Assert.Equal("exam1", decision.SourceId);
    }

    [Fact]
    public async Task A_source_granted_learner_production_with_proof_is_allowed()
    {
        /*
         * Written so the gate is proven to be a gate rather than a constant
         * "no". Nothing in the seeded registry reaches this branch today —
         * `M-53` is open — so without this case the refusal tests would pass
         * against an implementation that always refused.
         */
        var paper = Paper();

        var cleared = ContentSource.Register(
            new ContentSourceId("cleared-example"), "A hypothetically cleared source",
            owner: "VNI Education",
            proof: new RightsProof("test-only", "test@vni.example", Now.AddDays(-1)),
            allowedEnvironments: [ContentEnvironment.Fixture, ContentEnvironment.LearnerProduction],
            expiresAt: null, rootPath: "example", files: [],
            boundExamVersionIds: [paper.Id], boundExamDefinitionIds: []);

        var decision = await Guard(cleared).MayPublishToLearnersAsync(paper, default);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task The_guard_asks_about_learner_production_and_nothing_weaker()
    {
        // A guard that happened to check `fixture` would pass every one of the
        // refusal tests above in the seeded state, because everything holds
        // `fixture`. This is what distinguishes the two.
        var paper = Paper();

        var reviewable = ContentSource.Register(
            new ContentSourceId("review-only"), "Cleared for internal review only",
            owner: null, proof: null,
            allowedEnvironments: [ContentEnvironment.Fixture, ContentEnvironment.InternalReview],
            expiresAt: null, rootPath: "example", files: [],
            boundExamVersionIds: [paper.Id], boundExamDefinitionIds: []);

        var decision = await Guard(reviewable).MayPublishToLearnersAsync(paper, default);

        Assert.False(decision.Allowed);
    }
}

/// <summary>
/// Verification of recorded hashes, through the port that reads files.
///
/// <b>No real content is touched.</b> The material is gitignored and absent in
/// CI; these cases hand the use case observations directly, which is the same
/// path the filesystem adapter takes.
/// </summary>
public sealed class VerifyContentSourceTests
{
    private const string Recorded =
        "9f026296e81ef64bbe61b6109d33f9a490725463ea5fc48815f4050ebccc003b";

    private const string Different =
        "867a6dca9350b1463331d4842f0cbc3276b14d45aeee63ebf50e04b1afe0114d";

    private sealed class FakeRegistry(ContentSource source) : IContentRightsRegistry
    {
        public Task<ContentSource?> FindAsync(ContentSourceId id, CancellationToken ct) =>
            Task.FromResult<ContentSource?>(id.Value == source.Id.Value ? source : null);

        public Task<ContentSource?> FindForExamAsync(
            ExamVersionId versionId, ExamDefinitionId definitionId, CancellationToken ct) =>
            Task.FromResult<ContentSource?>(null);

        public Task<IReadOnlyList<ContentSource>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentSource>>([source]);

        public Task<bool> RegisterIfAbsentAsync(ContentSource s, CancellationToken ct) =>
            Task.FromResult(false);
    }

    private sealed class FakeProbe(Dictionary<string, ContentFileObservation> seen) : IContentFileProbe
    {
        public Task<ContentFileObservation> ObserveAsync(string relativePath, CancellationToken ct) =>
            Task.FromResult(seen.TryGetValue(relativePath, out var found)
                ? found
                : new ContentFileObservation(false, null));
    }

    private static ContentSource Source() =>
        ContentSource.Register(
            new ContentSourceId("exam1"), "Exam 1", null, null,
            [ContentEnvironment.Fixture], null, "exam/Exam1",
            [new ContentFileRef("exam/Exam1/assets/audio/listening-part1.mp3", Recorded, null)],
            [], []);

    [Fact]
    public async Task A_changed_file_is_reported_as_changed()
    {
        var verify = new VerifyContentSource(
            new FakeRegistry(Source()),
            new FakeProbe(new()
            {
                ["exam/Exam1/assets/audio/listening-part1.mp3"] = new(true, Different),
            }));

        var report = await verify.RunAsync(new ContentSourceId("exam1"), default);

        Assert.NotNull(report);
        Assert.True(report!.AnyChanged);
        Assert.Equal(ContentFileState.Changed, report.Files[0].State);
    }

    [Fact]
    public async Task An_absent_file_is_reported_as_missing_and_not_as_verified()
    {
        var verify = new VerifyContentSource(new FakeRegistry(Source()), new FakeProbe(new()));

        var report = await verify.RunAsync(new ContentSourceId("exam1"), default);

        Assert.True(report!.AnyMissing);
        Assert.False(report.FullyVerified);
    }

    [Fact]
    public async Task An_unknown_source_yields_no_report_rather_than_an_empty_clean_one()
    {
        // An empty report reads as "checked, nothing wrong". Nothing was checked.
        var verify = new VerifyContentSource(new FakeRegistry(Source()), new FakeProbe(new()));

        Assert.Null(await verify.RunAsync(new ContentSourceId("not-registered"), default));
    }
}
