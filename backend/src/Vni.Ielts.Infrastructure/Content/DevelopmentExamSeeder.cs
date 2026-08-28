using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Loads the sample exams in <c>fixtures/exams</c> on a development boot.
///
/// <b>Through the package reader, never as a hand-written object graph.</b>
/// That is the hard constraint on `T5`: the seeder, the ZIP importer and
/// in-place CMS authoring are three producers of the same
/// <see cref="ExamVersion"/> and must go through the same validator. A seeder
/// that builds entities directly is a fourth path with no schema behind it,
/// and it is the path that silently accepts content the real importer would
/// reject.
///
/// <b>Development only.</b> It is never registered outside Development, and it
/// publishes what it loads — which is exactly what must not happen to a
/// production catalogue, where publishing is a reviewed administrative act.
/// </summary>
public sealed class DevelopmentExamSeeder(
    IExamCatalogue catalogue,
    IClock clock,
    IConfiguration configuration,
    ILogger<DevelopmentExamSeeder> logger)
{
    /// <summary>
    /// The prefix that marks a fixture as written for the test suite rather
    /// than supplied by the owner.
    /// </summary>
    private const string SyntheticPrefix = "synthetic-";

    /// <summary>
    /// Whether to seed the synthetic papers as well as the owner's content.
    ///
    /// <b>Off by default, and the default is a product decision, not a
    /// preference.</b> The owner's direction on 2026-08-27 was that only
    /// supplied content ships, and two demo papers plus a synthetic dictation
    /// set were deleted for it. <c>synthetic-full-1.json</c> exists because a
    /// clean checkout otherwise has no four-module paper and the entire exam
    /// contract suite goes untested — but it is a test fixture, and a test
    /// fixture appearing in a learner's catalogue is the exact thing that
    /// direction removed.
    ///
    /// The integration suite turns it on. A developer who wants a paper to
    /// click through without running the importer can too:
    /// <c>Seed__IncludeSyntheticExams=true dotnet run --project backend/src/Vni.Ielts.Api</c>
    /// </summary>
    private bool IncludeSynthetic =>
        configuration.GetValue("Seed:IncludeSyntheticExams", false);

    public async Task SeedAsync(CancellationToken ct)
    {
        if (LocateFixtures() is not { } directory)
        {
            logger.LogInformation("No fixtures/exams directory found; skipping exam seed.");
            return;
        }

        var schemaPath = Path.Combine(directory, "..", "..", "contracts", "schemas", "exam.schema.json");
        schemaPath = Path.GetFullPath(schemaPath);

        if (!File.Exists(schemaPath))
        {
            logger.LogWarning("Exam schema not found at {Path}; skipping exam seed.", schemaPath);
            return;
        }

        var reader = ExamPackageReader.FromSchemaFile(schemaPath);

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f))
        {
            var slug = Path.GetFileNameWithoutExtension(file);

            if (slug.StartsWith(SyntheticPrefix, StringComparison.Ordinal) && !IncludeSynthetic)
            {
                logger.LogInformation(
                    "Skipping synthetic fixture {File}; set Seed:IncludeSyntheticExams to load it.",
                    Path.GetFileName(file));
                continue;
            }

            /*
             * ── The id follows the content, not only the file name ────────
             *
             * <b>It was `seed-{slug}-v1`, and that made every restart a
             * rewrite.</b> The seeder publishes what it loads, so editing a
             * fixture and restarting replaced a <i>published</i> version's
             * content in place — changing the exam underneath every sitting
             * that was running it. The learner's screen kept the old passage;
             * the marker used the new answer key. `UpsertAsync` now refuses
             * that outright, so the old id would simply make the seed fail.
             *
             * Deriving the id from the content is the behaviour the entity has
             * always documented: <i>editing a published version produces a new
             * version</i>. An unchanged fixture keeps its id and its row; a
             * changed one becomes a new version, and the sitting already
             * running the old one keeps the exam it started.
             */
            var body = await File.ReadAllTextAsync(file, ct);
            var definitionId = new ExamDefinitionId($"seed-{slug}");
            var fingerprint = Fingerprint(body);
            var versionId = new ExamVersionId($"seed-{slug}-{fingerprint}");

            var result = reader.Read(body, definitionId, 1);

            if (!result.IsValid || result.Version is null)
            {
                // Loudly. A fixture that stopped matching the schema is a
                // broken contract, and a dev whose exam list is silently empty
                // will look everywhere except here.
                logger.LogError(
                    "Exam fixture {File} was rejected: {Findings}",
                    Path.GetFileName(file),
                    string.Join(
                        " | ",
                        result.Findings.Select(f => $"{f.Severity} {f.Code} at {f.Path}: {f.Message}")));
                continue;
            }

            var draft = result.Version;


            var published = ExamVersion.Rehydrate(
                versionId,
                draft.DefinitionId,
                draft.VersionNumber,
                draft.Title,
                draft.Variant,
                ExamVersionStatus.Published,
                clock.UtcNow,
                draft.Scoring,
                draft.Timing,
                draft.Sections);

            /*
             * <b>An identical, already-published version is a no-op — not an
             * error, and certainly not a reason to refuse to boot.</b>
             *
             * `versionId` is `seed-{slug}-{fingerprint}` where the fingerprint
             * hashes the fixture body, so a stored version carrying this id has
             * the same content by construction. Re-seeding it has nothing to
             * do.
             *
             * Without this the seeder called `UpsertAsync` on it anyway, and
             * `MongoExamCatalogue` correctly refused —
             * `PublishedExamVersionIsImmutableException`, because rewriting a
             * published version would silently change historical scores. That
             * exception propagated out of `InitialiseInfrastructureAsync` and
             * **killed the process at startup**: after one seeded run, the API
             * could never start again against the same database.
             *
             * Found by the E2E stage of `pnpm verify`, which runs twice
             * against a persistent `vni_ielts_e2e` database. It is invisible on
             * CI, where every job gets a fresh MongoDB — which is exactly why
             * it survived this long. The guard is right; the seeder was wrong
             * to treat "already correct" as "must overwrite".
             */
            var existing = await catalogue.FindAsync(versionId, ct);
            var alreadySeeded = existing is { Status: ExamVersionStatus.Published };

            if (alreadySeeded)
            {
                logger.LogInformation(
                    "Exam {Id} is already published with identical content; leaving it untouched.",
                    versionId.Value);
            }
            else
            {
                await catalogue.UpsertAsync(published, ct);
            }

            /*
             * <b>The previous take of this fixture stops being sittable.</b>
             *
             * Without this, editing a fixture leaves both versions published
             * and the catalogue shows the same paper twice — with a learner
             * able to start the stale one. Unpublishing blocks new sittings and
             * deliberately does not end running ones: terminating a timed exam
             * mid-attempt is a scoring incident, not an administrative action.
             * → `M-15`
             */
            foreach (var stale in await catalogue.ListAllAsync(ct))
            {
                if (stale.DefinitionId != definitionId) continue;
                if (stale.Id == versionId) continue;
                if (stale.Status != ExamVersionStatus.Published) continue;

                stale.Unpublish();
                await catalogue.UpsertAsync(stale, ct);

                logger.LogInformation(
                    "Unpublished {Id}: {File} has changed, so it is a new version.",
                    stale.Id.Value, Path.GetFileName(file));
            }

            if (!alreadySeeded)
            {
                logger.LogInformation(
                    "Seeded exam {Title} ({Id}) with {Sections} section(s).",
                    published.Title, versionId.Value, published.Sections.Count);
            }
        }
    }

    /// <summary>
    /// Eight hex characters of the fixture's own bytes.
    ///
    /// Short because it appears in an id a developer reads, and long enough
    /// that two fixtures colliding is not something to plan around.
    /// </summary>
    private static string Fingerprint(string body) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(body)))[..8]
            .ToLowerInvariant();

    /// <summary>
    /// Walks up from the running assembly looking for <c>fixtures/exams</c>.
    ///
    /// The API runs from <c>bin/Debug/net10.0</c>, so a relative path from the
    /// working directory depends on how it was launched — `dotnet run` and a
    /// debugger disagree, and the seed silently does nothing in one of them.
    /// </summary>
    private static string? LocateFixtures()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "fixtures", "exams");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        return null;
    }
}
