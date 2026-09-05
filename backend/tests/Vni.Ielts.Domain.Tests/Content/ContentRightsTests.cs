using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Content;

/// <summary>
/// The rights rule, on its own, with no storage and no HTTP.
///
/// <b>The case that matters most is the empty one.</b> A registry whose
/// interesting behaviour is "a granted source is allowed" would be safe on the
/// day it was written and unsafe the first time somebody dropped a folder into
/// the workspace without registering it. So the rule under test here is the
/// other way round: <i>absence of a record is absence of a right</i>, and the
/// presence of a file grants nothing at all.
///
/// <b><c>M-53</c> — which papers may be shown to a learner — is open.</b> These
/// tests therefore never assert that any real source <i>is</i> publishable.
/// They assert the shape of the gate. → CLAUDE.md `G-11`
/// </summary>
public sealed class ContentRightsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 9, 0, 0, TimeSpan.Zero);

    private static ContentSource Source(
        IEnumerable<ContentEnvironment>? allowed = null,
        RightsProof? proof = null,
        DateTimeOffset? expiresAt = null,
        IEnumerable<ContentFileRef>? files = null) =>
        ContentSource.Register(
            new ContentSourceId("example-source"),
            "Example",
            owner: null,
            proof: proof,
            allowedEnvironments: allowed ?? [ContentEnvironment.Fixture],
            expiresAt: expiresAt,
            rootPath: "example",
            files: files ?? [],
            boundExamVersionIds: [],
            boundExamDefinitionIds: []);

    // ── Absence is the default-deny case ────────────────────────────────

    [Fact]
    public void A_source_with_no_registry_entry_has_no_rights_at_all()
    {
        /*
         * The whole point of the registry. A file that is simply present in the
         * workspace — dropped there by an import, a restore or a developer —
         * has never been reviewed by anybody, and treating "unknown" as
         * "unrestricted" is how unlicensed material reaches a learner.
         */
        var decision = ContentRightsPolicy.Evaluate(
            source: null, ContentEnvironment.LearnerProduction, Now);

        Assert.False(decision.Allowed);
        Assert.Equal(ContentRightsDenial.NoRegistryEntry, decision.Denial);
    }

    [Fact]
    public void An_unregistered_source_is_refused_for_every_environment_including_fixture()
    {
        foreach (var environment in Enum.GetValues<ContentEnvironment>())
        {
            var decision = ContentRightsPolicy.Evaluate(null, environment, Now);
            Assert.False(decision.Allowed);
        }
    }

    // ── The seeded state: fixture only ──────────────────────────────────

    [Fact]
    public void A_fixture_only_source_is_refused_for_learner_production()
    {
        var decision = ContentRightsPolicy.Evaluate(
            Source([ContentEnvironment.Fixture]), ContentEnvironment.LearnerProduction, Now);

        Assert.False(decision.Allowed);
        Assert.Equal(ContentRightsDenial.EnvironmentNotGranted, decision.Denial);
    }

    [Fact]
    public void A_fixture_only_source_is_still_usable_as_a_fixture()
    {
        // Importing content nobody may publish is deliberately allowed: the
        // exam screens were built against exactly such a paper.
        var decision = ContentRightsPolicy.Evaluate(
            Source([ContentEnvironment.Fixture]), ContentEnvironment.Fixture, Now);

        Assert.True(decision.Allowed);
        Assert.Null(decision.Denial);
    }

    [Fact]
    public void Internal_review_does_not_imply_learner_production()
    {
        var decision = ContentRightsPolicy.Evaluate(
            Source([ContentEnvironment.Fixture, ContentEnvironment.InternalReview]),
            ContentEnvironment.LearnerProduction,
            Now);

        Assert.False(decision.Allowed);
        Assert.Equal(ContentRightsDenial.EnvironmentNotGranted, decision.Denial);
    }

    // ── A grant is a dated, evidenced thing ─────────────────────────────

    [Fact]
    public void A_granted_and_unexpired_source_is_allowed()
    {
        var granted = Source(
            [ContentEnvironment.Fixture, ContentEnvironment.LearnerProduction],
            proof: new RightsProof("contract/2026-001", "someone@vni.example", Now),
            expiresAt: Now.AddDays(30));

        var decision = ContentRightsPolicy.Evaluate(
            granted, ContentEnvironment.LearnerProduction, Now);

        Assert.True(decision.Allowed);
        Assert.Equal("example-source", decision.SourceId);
    }

    [Fact]
    public void A_grant_that_has_expired_is_refused()
    {
        /*
         * A licence with an end date is the normal commercial shape, and the
         * day it lapses is exactly the day nobody is looking. Expiry is
         * evaluated against the server clock at the moment of the decision,
         * never cached into a boolean at import time.
         */
        var lapsed = Source(
            [ContentEnvironment.LearnerProduction],
            proof: new RightsProof("contract/2026-001", "someone@vni.example", Now.AddYears(-1)),
            expiresAt: Now.AddSeconds(-1));

        var decision = ContentRightsPolicy.Evaluate(
            lapsed, ContentEnvironment.LearnerProduction, Now);

        Assert.False(decision.Allowed);
        Assert.Equal(ContentRightsDenial.RightExpired, decision.Denial);
    }

    [Fact]
    public void Granting_learner_production_without_a_proof_reference_is_refused_at_construction()
    {
        // A right nobody can point at is not a right. This is the structural
        // half of `M-53`: the registry cannot express "publishable, source
        // unknown" at all, so nobody can seed one by accident.
        Assert.Throws<UnprovenPublishRightException>(() =>
            Source([ContentEnvironment.LearnerProduction], proof: null));
    }

    [Fact]
    public void A_stored_grant_without_a_proof_reference_is_not_honoured()
    {
        /*
         * The same rule on the read path. `Register` refuses to build one, but
         * a document can arrive from storage that a hand edit or an older
         * writer produced, and rehydrating must not turn that into a right.
         * Fail closed on read rather than throwing: a corrupt row should cost
         * one refusal, not the whole listing.
         */
        var rehydrated = ContentSource.Rehydrate(
            new ContentSourceId("hand-edited"),
            "Hand edited",
            owner: null,
            proof: null,
            allowedEnvironments: [ContentEnvironment.LearnerProduction],
            expiresAt: null,
            rootPath: "example",
            files: [],
            boundExamVersionIds: [],
            boundExamDefinitionIds: []);

        var decision = ContentRightsPolicy.Evaluate(
            rehydrated, ContentEnvironment.LearnerProduction, Now);

        Assert.False(decision.Allowed);
        Assert.Equal(ContentRightsDenial.ProofMissing, decision.Denial);
    }

    // ── Binding a source to the exam versions built from it ─────────────

    [Fact]
    public void A_source_knows_which_exam_versions_were_built_from_it()
    {
        var version = ExamVersionId.New();

        var source = ContentSource.Register(
            new ContentSourceId("exam1"), "Exam 1", null, null,
            [ContentEnvironment.Fixture], null, "exam/Exam1", [], [version], []);

        Assert.True(source.Produced(version));
        Assert.False(source.Produced(ExamVersionId.New()));
    }

    [Fact]
    public void A_source_also_binds_to_an_exam_definition_which_survives_a_content_edit()
    {
        // The seeder derives a version id from a fingerprint of the paper, so
        // an edit mints a new version id. The definition id does not move.
        var definition = new ExamDefinitionId("seed-exam-1");

        var source = ContentSource.Register(
            new ContentSourceId("exam1"), "Exam 1", null, null,
            [ContentEnvironment.Fixture], null, "exam/Exam1", [], [], [definition]);

        Assert.True(source.Covers(definition));
        Assert.False(source.Covers(new ExamDefinitionId("seed-something-else")));
    }

    // ── Identifiers ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Cam 16")]                     // spaces
    [InlineData("cam/16")]                     // a path
    [InlineData("cam\\16")]
    [InlineData("../cam-16")]
    [InlineData("Đề-CAM")]                     // non-ASCII
    [InlineData("Cam-16")]                     // upper case
    public void An_identifier_that_looks_like_a_path_is_refused(string candidate)
    {
        /*
         * A `sourceId` is a stable name a human chose, not a slug of wherever
         * the bytes happen to sit today. The VOL 9 directory carries a Google
         * Drive export stamp — `-20260819T082203Z-1-001` — baked into a path
         * segment, and an id derived from it would change the moment anybody
         * re-exported the folder.
         */
        Assert.Throws<ArgumentException>(() => new ContentSourceId(candidate));
    }

    [Fact]
    public void A_slug_identifier_is_accepted()
    {
        Assert.Equal("vol9-test-1", new ContentSourceId("vol9-test-1").Value);
    }

    // ── File references and hashes ──────────────────────────────────────

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void A_file_reference_that_escapes_the_workspace_is_refused(string path)
    {
        Assert.Throws<ArgumentException>(() => new ContentFileRef(path, null, null));
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("9F026296E81EF64BBE61B6109D33F9A490725463EA5FC48815F4050EBCCC003B")] // upper
    [InlineData("9f026296e81ef64bbe61b6109d33f9a490725463ea5fc48815f4050ebccc003")]  // 63
    public void A_malformed_hash_is_refused(string hash)
    {
        Assert.Throws<ArgumentException>(() =>
            new ContentFileRef("exam/Exam1/exam.json", hash, null));
    }

    [Fact]
    public void Backslashes_in_a_recorded_path_are_normalised()
    {
        // The registry is seeded and read on Windows and on Linux. A path that
        // compares unequal because of a separator is a false "missing file".
        Assert.Equal(
            "exam/Exam1/exam.json",
            new ContentFileRef("exam\\Exam1\\exam.json", null, null).RelativePath);
    }

    // ── Integrity: a file whose hash changed must be detected ───────────

    private const string HashA =
        "9f026296e81ef64bbe61b6109d33f9a490725463ea5fc48815f4050ebccc003b";

    private const string HashB =
        "867a6dca9350b1463331d4842f0cbc3276b14d45aeee63ebf50e04b1afe0114d";

    [Fact]
    public void A_file_whose_content_hash_changed_is_reported_as_changed()
    {
        var source = Source(files: [new ContentFileRef("a.mp3", HashA, null)]);

        var report = ContentIntegrity.Compare(source, new Dictionary<string, ContentFileObservation>
        {
            ["a.mp3"] = new(Exists: true, Sha256: HashB),
        });

        var file = Assert.Single(report.Files);
        Assert.Equal(ContentFileState.Changed, file.State);
        Assert.Equal(HashA, file.RecordedSha256);
        Assert.Equal(HashB, file.ObservedSha256);
        Assert.True(report.AnyChanged);
    }

    [Fact]
    public void A_file_whose_content_hash_is_unchanged_matches()
    {
        var source = Source(files: [new ContentFileRef("a.mp3", HashA, null)]);

        var report = ContentIntegrity.Compare(source, new Dictionary<string, ContentFileObservation>
        {
            ["a.mp3"] = new(Exists: true, Sha256: HashA),
        });

        Assert.Equal(ContentFileState.Matches, Assert.Single(report.Files).State);
        Assert.False(report.AnyChanged);
        Assert.True(report.FullyVerified);
    }

    [Fact]
    public void An_absent_file_is_reported_as_missing_rather_than_as_matching()
    {
        // The content directories are gitignored, so "absent" is the normal
        // state in CI. It must never read as agreement.
        var source = Source(files: [new ContentFileRef("a.mp3", HashA, null)]);

        var report = ContentIntegrity.Compare(source, new Dictionary<string, ContentFileObservation>
        {
            ["a.mp3"] = new(Exists: false, Sha256: null),
        });

        Assert.Equal(ContentFileState.Missing, Assert.Single(report.Files).State);
        Assert.True(report.AnyMissing);
        Assert.False(report.FullyVerified);
    }

    [Fact]
    public void A_file_that_was_never_hashed_is_reported_as_unhashed_rather_than_as_matching()
    {
        /*
         * Most sources have no recorded hash yet — nobody has computed one, and
         * the plan does not pretend otherwise. `NotHashed` is the honest third
         * answer; folding it into `Matches` would report verification that
         * never happened.
         */
        var source = Source(files: [new ContentFileRef("a.mp3", null, null)]);

        var report = ContentIntegrity.Compare(source, new Dictionary<string, ContentFileObservation>
        {
            ["a.mp3"] = new(Exists: true, Sha256: HashA),
        });

        Assert.Equal(ContentFileState.NotHashed, Assert.Single(report.Files).State);
        Assert.False(report.AnyChanged);
        Assert.False(report.FullyVerified);
    }

    [Fact]
    public void A_file_nobody_looked_at_is_reported_as_missing_rather_than_skipped()
    {
        // A prober that returns no observation for a path has not agreed with
        // the record; it has said nothing. Silence is not verification.
        var source = Source(files: [new ContentFileRef("a.mp3", HashA, null)]);

        var report = ContentIntegrity.Compare(
            source, new Dictionary<string, ContentFileObservation>());

        Assert.Equal(ContentFileState.Missing, Assert.Single(report.Files).State);
    }
}
