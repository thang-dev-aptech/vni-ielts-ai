using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Importing;

namespace Vni.Ielts.Infrastructure.Tests.Persistence.Importing;

/// <summary>
/// CRUD and the compare-and-swap for <see cref="MongoImportDraftStore"/>
/// against a real MongoDB — one fresh database per test run, same technique
/// as <c>SpeakingRecordingUploadTests.Env</c>.
/// </summary>
public sealed class MongoImportDraftStoreTests
{
    private static (MongoImportDraftStore Store, ExamPackageValidator Validator) NewStore()
    {
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_import_draft_test_{Guid.NewGuid():n}",
        }));

        var validator = new ExamPackageValidator(
            ExamPackageReader.FromSchemaFile(SchemaPath()));

        return (new MongoImportDraftStore(context, validator), validator);
    }

    private static string SchemaPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "contracts", "schemas", "exam.schema.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("contracts/schemas/exam.schema.json not found above " + AppContext.BaseDirectory);
    }

    private static ExamImportDraft Draft(ExamPackageValidator validator, string packageJson, Guid? id = null)
    {
        var definitionId = ExamDefinitionId.New();
        var validation = validator.Validate(packageJson, definitionId, 1);
        Assert.True(validation.IsValid, string.Join("; ", validation.Findings.Select(f => f.Message)));

        return new ExamImportDraft(
            id ?? Guid.NewGuid(), definitionId, 1, ExamImportRoute.StructuredPackage,
            ExamImportWorkflow.Hash(packageJson), ExamImportWorkflow.Hash(packageJson),
            validation.Version!, null, ImportApprovalState.ReviewRequired, [],
            "source text", packageJson, ImportReviewChecklist.Empty, [], 0, null);
    }

    private const string PackageJson = """
    {
      "formatVersion": "2.0", "formatProfile": "vni-practice", "scoringProfileRef": "store-test",
      "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
      "title": "Store test", "variant": "academic",
      "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
      "scoringProfile": { "rawToBand": { "reading": [
        { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 } ] } },
      "sequenceProfile": { "modules": ["reading"] },
      "sections": [{ "module": "reading", "order": 1, "parts": [{ "order": 1, "kind": "passage",
        "body": "Evidence.", "questions": [{
          "id": "q-1", "order": 1, "type": "multiple-select", "marks": 1,
          "options": [{ "key": "A", "text": "Alpha" }],
          "group": { "id": "bank-1", "instruction": "Choose." },
          "slots": [{ "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } }],
          "explanation": { "shortReason": "Stated.", "evidence": ["Evidence."] }
        }]
      }]}]
    }
    """;

    [Fact]
    public async Task Save_then_find_round_trips_the_draft()
    {
        var (store, validator) = NewStore();
        var draft = Draft(validator, PackageJson);

        await store.SaveAsync(draft, default);
        var found = await store.FindAsync(draft.Id, default);

        Assert.NotNull(found);
        Assert.Equal(draft.Id, found!.Id);
        Assert.Equal(draft.DefinitionId, found.DefinitionId);
        Assert.Equal(draft.PackageJson, found.PackageJson);
        Assert.Equal(ImportApprovalState.ReviewRequired, found.ApprovalState);
        Assert.Equal(0, found.Revision);
    }

    [Fact]
    public async Task Find_returns_null_for_an_unknown_id()
    {
        var (store, _) = NewStore();
        Assert.Null(await store.FindAsync(Guid.NewGuid(), default));
    }

    /// <summary>
    /// The idempotency contract: a second save with the SAME id (and, by
    /// construction, identical content — the id is derived from the content
    /// hash) must not clobber review progress that happened after the first
    /// save.
    /// </summary>
    [Fact]
    public async Task Saving_the_same_draft_id_twice_does_not_overwrite_review_progress()
    {
        var (store, validator) = NewStore();
        var draft = Draft(validator, PackageJson);
        await store.SaveAsync(draft, default);

        var reviewed = draft with
        {
            Checklist = new ImportReviewChecklist(Enum.GetValues<ImportReviewCategory>().ToHashSet()),
            Revision = 1,
        };
        var replaced = await store.ReplaceAsync(reviewed, 0, default);
        Assert.True(replaced);

        // A replay of the exact same import (same id) must be a no-op.
        await store.SaveAsync(draft, default);

        var found = await store.FindAsync(draft.Id, default);
        Assert.Equal(1, found!.Revision);
        Assert.True(found.Checklist.IsComplete);
    }

    [Fact]
    public async Task Replace_succeeds_only_at_the_expected_revision()
    {
        var (store, validator) = NewStore();
        var draft = Draft(validator, PackageJson);
        await store.SaveAsync(draft, default);

        var updated = draft with { ReviewedBy = "reviewer-1", Revision = 1 };

        // Stale revision — refused, nothing changes.
        var staleResult = await store.ReplaceAsync(updated, expectedRevision: 5, default);
        Assert.False(staleResult);
        Assert.Null((await store.FindAsync(draft.Id, default))!.ReviewedBy);

        // Correct revision — succeeds.
        var result = await store.ReplaceAsync(updated, expectedRevision: 0, default);
        Assert.True(result);
        Assert.Equal("reviewer-1", (await store.FindAsync(draft.Id, default))!.ReviewedBy);

        // The same call again, now stale (the document moved to revision 1) — refused.
        var replay = await store.ReplaceAsync(updated, expectedRevision: 0, default);
        Assert.False(replay);
    }

    [Fact]
    public async Task A_draft_document_without_checklistRequired_reads_as_required()
    {
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_import_draft_test_{Guid.NewGuid():n}",
        }));
        var validator = new ExamPackageValidator(
            ExamPackageReader.FromSchemaFile(SchemaPath()));
        var store = new MongoImportDraftStore(context, validator);
        var draft = Draft(validator, PackageJson);
        await store.SaveAsync(draft, default);

        var unset = await context.ImportDrafts.UpdateOneAsync(
            Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Id, draft.Id.ToString("D")),
            Builders<ExamImportDraftDocument>.Update.Unset(d => d.ChecklistRequired));
        Assert.Equal(1, unset.ModifiedCount);

        var found = await store.FindAsync(draft.Id, default);
        Assert.True(found!.ChecklistRequired);
    }

    [Fact]
    public async Task Warnings_and_their_override_reason_round_trip()
    {
        var (store, validator) = NewStore();
        var draft = Draft(validator, PackageJson) with
        {
            Warnings = [new ImportReviewWarning(
                "w1", ImportReviewCategory.TranscriptAndEvidence, "/sections/0", "Missing transcript.", false)],
        };
        await store.SaveAsync(draft, default);

        var resolved = draft with
        {
            Warnings = [draft.Warnings[0] with { Resolved = true, OverrideReason = "Checked by hand." }],
            Revision = 1,
        };
        Assert.True(await store.ReplaceAsync(resolved, 0, default));

        var found = await store.FindAsync(draft.Id, default);
        var warning = Assert.Single(found!.Warnings);
        Assert.True(warning.Resolved);
        Assert.Equal("Checked by hand.", warning.OverrideReason);
    }

    [Fact]
    public async Task CreatedBy_and_CreatedAt_round_trip()
    {
        var (store, validator) = NewStore();
        var at = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
        var draft = Draft(validator, PackageJson) with
        {
            CreatedBy = new UserId("uploader-1"),
            CreatedAt = at,
        };

        await store.SaveAsync(draft, default);
        var found = await store.FindAsync(draft.Id, default);

        Assert.Equal(draft.CreatedBy, found!.CreatedBy);
        Assert.Equal(at, found.CreatedAt);
    }

    [Fact]
    public async Task A_legacy_document_without_created_fields_maps_to_null()
    {
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_import_draft_test_{Guid.NewGuid():n}",
        }));
        var validator = new ExamPackageValidator(
            ExamPackageReader.FromSchemaFile(SchemaPath()));
        var store = new MongoImportDraftStore(context, validator);
        var draft = Draft(validator, PackageJson);
        await store.SaveAsync(draft, default);

        var unset = await context.ImportDrafts.UpdateOneAsync(
            Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Id, draft.Id.ToString("D")),
            Builders<ExamImportDraftDocument>.Update
                .Unset(d => d.CreatedBy)
                .Unset(d => d.CreatedAt));
        Assert.Equal(1, unset.MatchedCount);

        var found = await store.FindAsync(draft.Id, default);
        Assert.Null(found!.CreatedBy);
        Assert.Null(found.CreatedAt);
    }

    [Fact]
    public async Task List_returns_newest_timestamp_first_and_legacy_rows_last()
    {
        var (store, validator) = NewStore();
        var older = Draft(validator, PackageJson.Replace("store-test", "older")) with
        {
            CreatedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var newer = Draft(validator, PackageJson.Replace("store-test", "newer")) with
        {
            CreatedAt = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero),
        };
        var legacy = Draft(validator, PackageJson.Replace("store-test", "legacy"));

        await store.SaveAsync(older, default);
        await store.SaveAsync(legacy, default);
        await store.SaveAsync(newer, default);

        var listed = await store.ListAsync(default);
        Assert.Equal(3, listed.Count);
        Assert.Equal(newer.Id, listed[0].Id);
        Assert.Equal(older.Id, listed[1].Id);
        Assert.Equal(legacy.Id, listed[2].Id);
        Assert.Null(listed[2].CreatedAt);
    }

    [Fact]
    public async Task Asset_manifest_round_trips()
    {
        var (store, validator) = NewStore();
        var draft = Draft(validator, PackageJson) with
        {
            AssetManifest =
            [
                new ImportAssetManifestEntry(
                    "assets/a.mp3",
                    "imports/exam-drafts/00000000-0000-0000-0000-000000000001/assets/a.mp3",
                    "audio/mpeg",
                    12,
                    new string('b', 64)),
            ],
        };

        await store.SaveAsync(draft, default);
        var found = await store.FindAsync(draft.Id, default);
        Assert.Equal(draft.Assets, found!.Assets);
    }

    [Fact]
    public async Task A_draft_document_without_asset_manifest_reads_as_empty()
    {
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_import_draft_test_{Guid.NewGuid():n}",
        }));
        var validator = new ExamPackageValidator(
            ExamPackageReader.FromSchemaFile(SchemaPath()));
        var store = new MongoImportDraftStore(context, validator);
        var draft = Draft(validator, PackageJson);
        await store.SaveAsync(draft, default);

        var unset = await context.ImportDrafts.UpdateOneAsync(
            Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Id, draft.Id.ToString("D")),
            Builders<ExamImportDraftDocument>.Update.Unset(d => d.AssetManifest));
        Assert.Equal(1, unset.ModifiedCount);

        var found = await store.FindAsync(draft.Id, default);
        Assert.Empty(found!.Assets);
    }
}
