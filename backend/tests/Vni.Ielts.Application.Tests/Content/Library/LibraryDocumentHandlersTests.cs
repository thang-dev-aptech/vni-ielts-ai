using Vni.Ielts.Application.Content.Library;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content.Library;

namespace Vni.Ielts.Application.Tests.Content.Library;

/// <summary>
/// Documents share <see cref="ArticleHandlersTests"/>'s store shape and
/// four-state lifecycle exactly — this is the part that is specific to
/// documents: the filter fields and <c>relatedExamIds</c> again, since a
/// document is addressed by id rather than slug.
/// </summary>
public sealed class LibraryDocumentHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Author = new("editor-1");

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class Harness
    {
        public FakeLibraryDocumentStore Store { get; } = new();
        private IClock Clock { get; } = new FixedClock(Now);

        public ListLibraryDocuments Learner => new(Store);
        public GetLibraryDocument LearnerById => new(Store);
        public CreateLibraryDocument Create => new(Store, Clock);
        public ChangeLibraryDocumentStatus ChangeStatus => new(Store, Clock);
    }

    private static LibraryDocumentDetails Details(
        DocumentSkill skill = DocumentSkill.Reading, string? targetBand = "7.0+") => new(
        "huong-dan-reading-t-f-ng", "Hướng dẫn True/False/Not Given", "Mẹo làm bài",
        skill, "guide", DocumentType.Guide, DocumentFormat.Pdf, targetBand, "Reading",
        12, "1.1 MB", null, false, true, false, false, DocumentAccess.Free);

    [Fact]
    public async Task A_learner_never_sees_a_draft()
    {
        var h = new Harness();
        await h.Create.HandleAsync(Details(), Author, default);

        Assert.Empty(await h.Learner.HandleAsync(new LibraryDocumentFilter(), default));
    }

    [Fact]
    public async Task A_published_document_is_filterable_by_skill()
    {
        var h = new Harness();
        var created = await h.Create.HandleAsync(Details(DocumentSkill.Writing), Author, default);
        await h.ChangeStatus.HandleAsync(
            new LibraryDocumentId(created.Id), LibraryTransition.Publish, default);

        var matching = await h.Learner.HandleAsync(
            new LibraryDocumentFilter(Skill: DocumentSkill.Writing), default);
        var nonMatching = await h.Learner.HandleAsync(
            new LibraryDocumentFilter(Skill: DocumentSkill.Listening), default);

        Assert.Single(matching);
        Assert.Empty(nonMatching);
    }

    [Fact]
    public async Task A_draft_document_id_is_not_found_to_a_learner()
    {
        var h = new Harness();
        var created = await h.Create.HandleAsync(Details(), Author, default);

        var result = await h.LearnerById.HandleAsync(new LibraryDocumentId(created.Id), default);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RelatedExamIds_is_reserved_and_null()
    {
        var h = new Harness();
        var created = await h.Create.HandleAsync(Details(), Author, default);

        Assert.Null(created.RelatedExamIds);
    }
}
