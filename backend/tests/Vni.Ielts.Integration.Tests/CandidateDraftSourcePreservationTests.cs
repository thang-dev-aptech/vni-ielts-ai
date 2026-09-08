using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Candidate → canonical JSON → stored Draft must keep the package's
/// <c>contentSourceRef.sourceId</c> when the creator restamps <c>AuthorId</c>.
/// </summary>
public sealed class CandidateDraftSourcePreservationTests
{
    private static string SchemaPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "contracts", "schemas", "exam.schema.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("contracts/schemas/exam.schema.json not found.");
    }

    private static string CanonicalJsonWithSource(string sourceId) => $$"""
    {
      "formatVersion": "2.0", "formatProfile": "vni-practice", "scoringProfileRef": "candidate-source",
      "contentSourceRef": { "sourceId": "{{sourceId}}", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
      "title": "Candidate source preservation", "variant": "academic",
      "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
      "scoringProfile": { "rawToBand": { "reading": [
        { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }, { "minRaw": 2, "band": 2 } ] } },
      "sequenceProfile": { "modules": ["reading"] },
      "sections": [{ "module": "reading", "order": 1, "parts": [{ "order": 1, "kind": "passage",
        "body": "Evidence here.", "questions": [{
          "id": "q-1", "order": 1, "type": "multiple-select", "marks": 2,
          "options": [{ "key": "A", "text": "Alpha" }, { "key": "B", "text": "Beta" }],
          "group": { "id": "bank-1", "instruction": "Choose." },
          "slots": [
            { "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } },
            { "id": "slot-2", "number": 2, "answerKey": { "accepted": ["B"] } }
          ],
          "explanation": { "shortReason": "Both are stated.", "evidence": ["Evidence here."] }
        }]
      }]}]
    }
    """;

    private sealed class FixedJsonBuilder(string json) : IConfirmedCandidatePackageBuilder
    {
        public string BuildCanonicalJson(
            ParsedExamCandidate candidate,
            CandidateCompletionData completion,
            ExamDefinitionId definitionId,
            int versionNumber) => json;
    }

    [SkippableFact]
    public async Task Candidate_canonical_json_source_survives_author_restamp()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_cand_src_{Guid.NewGuid():n}",
        }));

        var source = new ParsedSourceProvenance("exam.docx", 1, "Reading", null);
        var question = new ParsedQuestionCandidate(
            "q-1", 1, QuestionType.MultipleSelect, "Choose.",
            [new ParsedQuestionOptionCandidate("A", "Alpha"), new ParsedQuestionOptionCandidate("B", "Beta")],
            new ParsedAnswerKeyCandidate(["A"], null), source);
        var part = new ParsedPartCandidate("p1", 1, "Passage", "Evidence here.", [question], source);
        var module = new ParsedModuleCandidate(
            ExamModule.Reading, ParsedExamClassification.Reading, 0.9m, [part], source);
        var candidate = ParsedExamCandidate.Create(
            "cand-source", "pkg-source", "Candidate source preservation",
            ParsedExamClassification.Reading, 0.9m, [module], [source]);

        var author = UserId.New();
        var candidates = new MongoParsedExamCandidateRepository(context);
        await candidates.SaveAsync(candidate, 0, default);
        candidate.Confirm(author, DateTimeOffset.UtcNow);
        await candidates.SaveAsync(candidate, 0, default);

        var stored = await candidates.FindAsync(candidate.PackageId, candidate.Id, default);
        Assert.NotNull(stored);

        var creator = new MongoConfirmedCandidateDraftCreator(
            context,
            new FixedJsonBuilder(CanonicalJsonWithSource("source-a")),
            ExamPackageReader.FromSchemaFile(SchemaPath()),
            new SystemClock());

        var draft = await creator.CreateOnceAsync(
            new CandidateDraftCreation(
                stored!,
                new CandidateCompletionData(
                    ExamVariant.Academic,
                    new CandidateTimingProfile(new Dictionary<ExamModule, CandidateSectionTiming>
                    {
                        [ExamModule.Reading] = new(3600),
                    }),
                    new CandidateScoringProfile()),
                author,
                "candidate-source-preservation"),
            default);

        Assert.Equal("source-a", draft.ContentSourceId?.Value);
        Assert.Equal(author, draft.AuthorId);

        var persisted = await context.ExamVersions
            .Find(v => v.Id == draft.Id.Value)
            .FirstOrDefaultAsync();
        Assert.NotNull(persisted);
        var rehydrated = persisted!.ToDomain();
        Assert.Equal("source-a", rehydrated.ContentSourceId?.Value);
        Assert.Equal(author, rehydrated.AuthorId);
    }
}
