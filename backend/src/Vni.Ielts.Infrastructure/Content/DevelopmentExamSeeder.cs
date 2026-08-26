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
    IExamCatalogue catalogue, IClock clock, ILogger<DevelopmentExamSeeder> logger)
{
    /// <summary>
    /// Seeded fixtures need a real <c>CreatedBy</c> — the field is required,
    /// not nullable — but there is no registered user at seed time to own
    /// them. A fixed placeholder id, not a real account: nobody's own-scoped
    /// permission will ever match it, so seeded exams behave like admin-owned
    /// fixtures rather than silently becoming "yours" for whichever developer
    /// registers first. Genuine per-author ownership testing waits on Phase 3
    /// (the Question Builder), which is where exams get created by a real
    /// operator through the API.
    /// </summary>
    private static readonly UserId SeedAuthor = new("seed-author");

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

            // Deterministic, so a restart updates the same row rather than
            // stacking a new copy of the same exam into the catalogue.
            var definitionId = new ExamDefinitionId($"seed-{slug}");
            var versionId = new ExamVersionId($"seed-{slug}-v1");

            var result = reader.Read(await File.ReadAllTextAsync(file, ct), definitionId, 1, SeedAuthor);

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
                draft.CreatedBy,
                submittedBy: null,
                submittedAt: null,
                reviewedBy: null,
                reviewedAt: null,
                publishedAt: clock.UtcNow,
                draft.Scoring,
                draft.Timing,
                draft.Sections);

            await catalogue.UpsertAsync(published, ct);

            logger.LogInformation(
                "Seeded exam {Title} ({Id}) with {Sections} section(s).",
                published.Title, versionId.Value, published.Sections.Count);
        }
    }

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
