using System.Security.Cryptography;
using Vni.Ielts.Domain.Content;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

/// <summary>
/// The seeded registry, and the rule that nothing in it may be published.
///
/// <b><c>M-53</c> is open.</b> The owner has said some papers are cleared and
/// has not said which. So the seed grants <c>fixture</c> and nothing else to
/// every source — the <c>G-11</c> configured seam with a null implementation,
/// not an invented default. These tests are what stop that quietly loosening.
/// </summary>
public sealed class ContentRightsSeedTests
{
    [Fact]
    public void Nothing_in_the_seed_may_be_published_to_a_learner()
    {
        /*
         * The single assertion this whole file exists for. If it ever goes
         * red, somebody has granted a publication right in code — which is not
         * where a rights decision belongs even after `M-53` is answered: the
         * grant needs a named reviewer and a licence reference, and a source
         * file has neither.
         */
        var granted = ContentRightsSeed.Sources
            .Where(s => s.AllowedEnvironments.Contains(ContentEnvironment.LearnerProduction))
            .Select(s => s.Id.Value)
            .ToArray();

        Assert.True(
            granted.Length == 0,
            "M-53 is unanswered: no source may be seeded with a learner-production right. "
                + $"Granted: {string.Join(", ", granted)}");
    }

    [Fact]
    public void Every_seeded_source_is_fixture_only()
    {
        foreach (var source in ContentRightsSeed.Sources)
        {
            Assert.Equal(
                [ContentEnvironment.Fixture],
                source.AllowedEnvironments.Order().ToArray());
        }
    }

    [Fact]
    public void No_seeded_source_carries_a_rights_proof_because_none_has_been_established()
    {
        // `exam/Exam1/README.md` says it outright: "this material was not
        // authored by VNI and the right to use it has not been established".
        // Nothing else in the workspace has a licence recorded either.
        Assert.All(ContentRightsSeed.Sources, source => Assert.Null(source.Proof));
    }

    [Fact]
    public void Every_source_the_plan_names_has_an_entry()
    {
        // FS0.1: "Gắn riêng từng Cambridge book, VOL 9 test, Writing/Speaking
        // resource và Exam1."
        string[] expected =
        [
            "cambridge-ielts-16", "cambridge-ielts-17", "cambridge-ielts-18",
            "cambridge-ielts-19", "cambridge-ielts-20", "cambridge-ielts-21",

            "vol9-test-1", "vol9-test-2", "vol9-test-3", "vol9-test-4",
            "vol9-test-5", "vol9-test-6", "vol9-test-7", "vol9-test-8",

            "ielts-writing-band-descriptors",
            "ielts-writing-key-assessment-criteria",
            "ielts-academic-writing-sample-tasks-2023",
            "ielts-speaking-band-descriptors",
            "ielts-speaking-key-assessment-criteria",

            "exam1",
        ];

        var present = ContentRightsSeed.Sources.Select(s => s.Id.Value).ToArray();

        Assert.Empty(expected.Except(present));
    }

    [Fact]
    public void Source_ids_are_unique()
    {
        var ids = ContentRightsSeed.Sources.Select(s => s.Id.Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_seeded_source_records_where_its_material_lives()
    {
        Assert.All(ContentRightsSeed.Sources, s => Assert.False(string.IsNullOrWhiteSpace(s.RootPath)));
    }

    [Fact]
    public void Exam1_records_the_six_asset_hashes_from_its_manifest()
    {
        /*
         * The only per-file content hashes that exist anywhere in this project
         * today. They are copied out of `exam/Exam1/manifest.json` — the folder
         * is gitignored, so the manifest is not in a clean checkout and the
         * hashes would otherwise be lost with it. A hash is a fingerprint, not
         * content, so recording one here redistributes nothing.
         */
        var exam1 = ContentRightsSeed.Sources.Single(s => s.Id.Value == "exam1");

        var hashed = exam1.Files.Where(f => f.Sha256 is not null).ToArray();

        Assert.Equal(6, hashed.Length);
        Assert.All(hashed, f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256!));
    }

    [Fact]
    public void A_vol9_test_records_its_reading_key_listening_and_audio_files()
    {
        // Test 2 rather than test 1, because it carries both anomalies the
        // content survey found: the reading key is spelled `KET TEST 2-R.docx`
        // and the key directory is `KEY - EXPLAINATION`. A registry that
        // recorded the corrected spellings would report two false "missing"s
        // forever.
        var test2 = ContentRightsSeed.Sources.Single(s => s.Id.Value == "vol9-test-2");

        var paths = test2.Files.Select(f => f.RelativePath).ToArray();

        Assert.Contains(paths, p => p.EndsWith("READING/TEST 2-R.docx", StringComparison.Ordinal));
        Assert.Contains(
            paths,
            p => p.EndsWith("READING/KEY - EXPLAINATION/KET TEST 2-R.docx", StringComparison.Ordinal));
        Assert.Contains(paths, p => p.EndsWith("LISTENING/TEST 2-L.docx", StringComparison.Ordinal));
        Assert.Contains(
            paths,
            p => p.EndsWith("LISTENING/KEY - TRANSCRIPT/KEY TEST 2-L.docx", StringComparison.Ordinal));
        Assert.Contains(paths, p => p.EndsWith("LISTENING/AUDIO/TEST 2.mp4", StringComparison.Ordinal));
    }

    [Fact]
    public void Vol9_test_1_records_the_stray_space_in_its_reading_filename()
    {
        // `TEST 1 -R.docx` — the space before the hyphen exists in exactly one
        // file, and a tidied-up path here would be a permanent false negative.
        var test1 = ContentRightsSeed.Sources.Single(s => s.Id.Value == "vol9-test-1");

        Assert.Contains(
            test1.Files,
            f => f.RelativePath.EndsWith("READING/TEST 1 -R.docx", StringComparison.Ordinal));
    }

    [Fact]
    public void Recorded_paths_use_forward_slashes_so_the_registry_reads_the_same_on_linux()
    {
        Assert.All(
            ContentRightsSeed.Sources.SelectMany(s => s.Files),
            f => Assert.DoesNotContain('\\', f.RelativePath));
    }
}

/// <summary>
/// The adapter that actually reads a file, and the hash-change detection built
/// on it.
///
/// <b>Every case here creates its own file in a temp directory.</b> The real
/// material is gitignored, so a test that depended on it would be a test that
/// only runs on one laptop. → <see cref="RecordedContentIntegrityTests"/> for
/// the one case that does look at the real thing, and skips loudly.
/// </summary>
public sealed class FileSystemContentProbeTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("vni-content-probe-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Sha256Of(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    [Fact]
    public async Task An_absent_file_is_reported_as_absent_rather_than_throwing()
    {
        // The normal state in CI and in a clean checkout.
        var probe = new FileSystemContentProbe(_root);

        var seen = await probe.ObserveAsync("Đề IELTS/Đề CAM/Cam 16/Cam 16.pdf", default);

        Assert.False(seen.Exists);
        Assert.Null(seen.Sha256);
    }

    [Fact]
    public async Task A_present_file_is_hashed()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "a.txt"), "hello");

        var seen = await new FileSystemContentProbe(_root).ObserveAsync("a.txt", default);

        Assert.True(seen.Exists);
        Assert.Equal(Sha256Of("hello"), seen.Sha256);
    }

    [Fact]
    public async Task A_path_escaping_the_content_root_is_refused_rather_than_followed()
    {
        // Registry paths are data. A record that walked out of the content root
        // would turn the verification endpoint into an arbitrary file reader.
        var probe = new FileSystemContentProbe(_root);

        await Assert.ThrowsAsync<ArgumentException>(
            () => probe.ObserveAsync("../outside.txt", default));
    }

    [Fact]
    public async Task A_file_whose_bytes_changed_is_detected_against_its_recorded_hash()
    {
        /*
         * The FS0 phase gate: "file bị thay đổi hash được phát hiện". Proven
         * end to end — a real file, a real SHA-256, a real re-read — without
         * touching any gitignored material.
         */
        var path = Path.Combine(_root, "paper.txt");
        await File.WriteAllTextAsync(path, "the original paper");

        var probe = new FileSystemContentProbe(_root);
        var recorded = (await probe.ObserveAsync("paper.txt", default)).Sha256;

        var source = ContentSource.Register(
            new ContentSourceId("probe-example"), "Probe example", null, null,
            [ContentEnvironment.Fixture], null, ".",
            [new ContentFileRef("paper.txt", recorded, null)], [], []);

        var before = ContentIntegrity.Compare(
            source,
            new Dictionary<string, ContentFileObservation>
            {
                ["paper.txt"] = await probe.ObserveAsync("paper.txt", default),
            });

        Assert.True(before.FullyVerified);

        // Somebody swaps the file.
        await File.WriteAllTextAsync(path, "a different paper entirely");

        var after = ContentIntegrity.Compare(
            source,
            new Dictionary<string, ContentFileObservation>
            {
                ["paper.txt"] = await probe.ObserveAsync("paper.txt", default),
            });

        Assert.True(after.AnyChanged);
        Assert.Equal(ContentFileState.Changed, after.Files[0].State);
        Assert.NotEqual(recorded, after.Files[0].ObservedSha256);
    }
}

/// <summary>
/// The one suite that looks at the real, gitignored material.
///
/// <b>It skips when the content is absent, and it says so loudly.</b> The
/// directories are gitignored — <c>/exam/</c> and <c>/Đề IELTS/</c> — so CI and
/// a clean checkout have none of it and a hard failure there would be noise.
/// But a skip that nobody can see is worse than no test: it reports success
/// over a check that never ran.
///
/// So, exactly as the Mongo suites do with <c>VNI_REQUIRE_MONGO</c>: set
/// <c>VNI_REQUIRE_CONTENT</c> and an absent workspace becomes a failed run
/// rather than a quiet skip.
/// </summary>
public sealed class RecordedContentIntegrityTests
{
    private static string? RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;

        return directory?.FullName;
    }

    private static bool ContentRequired =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VNI_REQUIRE_CONTENT"));

    private const string Absent =
        "The Exam1 material is not in this working tree. `/exam/` is gitignored — nobody has "
        + "established the right to redistribute it — so a clean checkout and CI have none of "
        + "it. Set VNI_REQUIRE_CONTENT=1 to turn this skip into a failure on a machine that "
        + "is supposed to have the content.";

    [SkippableFact]
    public async Task The_recorded_Exam1_hashes_match_the_files_that_are_actually_there()
    {
        /*
         * <b>What this proves that the temp-file test cannot.</b> The six
         * hashes in the seed were transcribed out of `exam/Exam1/manifest.json`
         * by hand. A transcription error would make every future verification
         * report "changed" against material nobody touched — the registry
         * crying wolf, which is how a rights check stops being read.
         */
        var root = RepositoryRoot();
        var exam1 = ContentRightsSeed.Sources.Single(s => s.Id.Value == "exam1");

        var present = root is not null
            && exam1.Files.Any(f => File.Exists(Path.Combine(root, f.RelativePath)));

        if (!present && ContentRequired)
            Assert.Fail(Absent);

        Skip.IfNot(present, Absent);

        var probe = new FileSystemContentProbe(root!);
        var observed = new Dictionary<string, ContentFileObservation>(StringComparer.Ordinal);

        foreach (var file in exam1.Files)
            observed[file.RelativePath] = await probe.ObserveAsync(file.RelativePath, default);

        var report = ContentIntegrity.Compare(exam1, observed);

        var changed = report.Files
            .Where(f => f.State == ContentFileState.Changed)
            .Select(f => $"{f.RelativePath}: recorded {f.RecordedSha256}, found {f.ObservedSha256}")
            .ToArray();

        Assert.True(
            changed.Length == 0,
            "A recorded content hash no longer matches the file it describes:\n  "
                + string.Join("\n  ", changed));
    }
}
