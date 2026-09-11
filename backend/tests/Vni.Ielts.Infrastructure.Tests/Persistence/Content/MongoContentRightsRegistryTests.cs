using Microsoft.Extensions.Options;
using Vni.Ielts.Domain.Content;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Content;

namespace Vni.Ielts.Infrastructure.Tests.Persistence.Content;

public sealed class MongoContentRightsRegistryTests
{
    private static MongoContentRightsRegistry NewRegistry()
    {
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_content_rights_test_{Guid.NewGuid():n}",
        }));

        return new MongoContentRightsRegistry(context);
    }

    private static ContentSource Source(
        string id,
        IEnumerable<ExamVersionId>? versions = null,
        IEnumerable<ExamDefinitionId>? definitions = null,
        IEnumerable<ContentEnvironment>? environments = null,
        RightsProof? proof = null) =>
        ContentSource.Register(
            new ContentSourceId(id),
            $"Source {id}",
            owner: "test",
            proof: proof,
            allowedEnvironments: environments ?? [ContentEnvironment.Fixture],
            expiresAt: null,
            rootPath: "fixtures",
            files: [],
            boundExamVersionIds: versions ?? [],
            boundExamDefinitionIds: definitions ?? []);

    [Fact]
    public async Task An_explicit_source_id_finds_the_row_even_without_exam_bindings()
    {
        var registry = NewRegistry();
        await registry.RegisterIfAbsentAsync(Source("source-a"), default);

        var found = await registry.FindForExamAsync(
            ExamVersionId.New(), ExamDefinitionId.New(), new ContentSourceId("source-a"), default);

        Assert.NotNull(found);
        Assert.Equal("source-a", found!.Id.Value);
    }

    [Fact]
    public async Task An_explicit_missing_id_returns_null_despite_a_stale_binding()
    {
        var registry = NewRegistry();
        var versionId = ExamVersionId.New();
        var definitionId = ExamDefinitionId.New();
        await registry.RegisterIfAbsentAsync(
            Source("exam1", versions: [versionId], definitions: [definitionId]), default);

        var found = await registry.FindForExamAsync(
            versionId, definitionId, new ContentSourceId("source-missing"), default);

        Assert.Null(found);
    }

    [Fact]
    public async Task A_null_source_id_falls_back_to_definition_binding()
    {
        var registry = NewRegistry();
        var definitionId = ExamDefinitionId.New();
        await registry.RegisterIfAbsentAsync(Source("exam1", definitions: [definitionId]), default);

        var found = await registry.FindForExamAsync(
            ExamVersionId.New(), definitionId, contentSourceId: null, default);

        Assert.NotNull(found);
        Assert.Equal("exam1", found!.Id.Value);
    }
}
