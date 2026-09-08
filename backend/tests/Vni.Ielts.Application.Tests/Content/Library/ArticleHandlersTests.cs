using Vni.Ielts.Application.Content.Library;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content.Library;

namespace Vni.Ielts.Application.Tests.Content.Library;

/// <summary>
/// The Articles half of the two independent libraries — P-22. Documents
/// (<see cref="LibraryDocumentHandlersTests"/>) share the same store shape,
/// the same four-state lifecycle and the same slug-uniqueness rule, so this
/// file carries the full matrix and the Documents file stays a smoke test.
/// </summary>
public sealed class ArticleHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Author = new("editor-1");

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class Harness
    {
        public FakeArticleStore Store { get; } = new();
        private IClock Clock { get; } = new FixedClock(Now);

        public ListArticles Learner => new(Store);
        public GetArticleBySlug LearnerBySlug => new(Store);
        public ListAllArticles Admin => new(Store);
        public GetArticleForEditing AdminById => new(Store);
        public CreateArticle Create => new(Store, Clock);
        public UpdateArticle Update => new(Store, Clock);
        public DeleteArticle Delete => new(Store);
        public ChangeArticleStatus ChangeStatus => new(Store, Clock);
    }

    private static ArticleDetails Details(string slug = "huong-dan-viet-task-2") => new(
        slug, "Hướng dẫn viết Task 2", "Tóm tắt ngắn", ArticleCategory.HuongDan, 5, "VNI", ["Đoạn 1"]);

    private static async Task<Article> PublishedAsync(Harness h, string slug = "huong-dan-viet-task-2")
    {
        var created = await h.Create.HandleAsync(Details(slug), Author, default);
        await h.ChangeStatus.HandleAsync(new ArticleId(created.Value!.Id), LibraryTransition.Publish, default);
        return (await h.Store.FindAsync(new ArticleId(created.Value!.Id), default))!;
    }

    // ── Learner surface sees published only ─────────────────────────────

    [Fact]
    public async Task A_learner_never_sees_a_draft_in_the_list()
    {
        var h = new Harness();
        await h.Create.HandleAsync(Details(), Author, default); // stays Draft

        var list = await h.Learner.HandleAsync(null, default);

        Assert.Empty(list);
    }

    [Fact]
    public async Task A_learner_sees_a_published_article_in_the_list()
    {
        var h = new Harness();
        await PublishedAsync(h);

        var list = await h.Learner.HandleAsync(null, default);

        Assert.Single(list);
    }

    [Fact]
    public async Task Fetching_a_draft_by_slug_is_not_found_not_forbidden()
    {
        // A draft slug must teach a guesser nothing — the same rule as an
        // unpublished exam version.
        var h = new Harness();
        await h.Create.HandleAsync(Details(), Author, default);

        var result = await h.LearnerBySlug.HandleAsync("huong-dan-viet-task-2", default);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Fetching_a_published_article_by_slug_succeeds()
    {
        var h = new Harness();
        await PublishedAsync(h);

        var result = await h.LearnerBySlug.HandleAsync("huong-dan-viet-task-2", default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hướng dẫn viết Task 2", result.Value!.Title);
    }

    [Fact]
    public async Task Unpublishing_removes_it_from_the_learner_list_immediately()
    {
        var h = new Harness();
        var article = await PublishedAsync(h);

        await h.ChangeStatus.HandleAsync(article.Id, LibraryTransition.Unpublish, default);

        Assert.Empty(await h.Learner.HandleAsync(null, default));
        // But the CMS still has it — nothing is deleted by unpublishing.
        Assert.Single(await h.Admin.HandleAsync(default));
    }

    // ── relatedExamIds stays the reserved, null seam ─────────────────────

    [Fact]
    public async Task RelatedExamIds_round_trips_as_null()
    {
        var h = new Harness();
        var created = await h.Create.HandleAsync(Details(), Author, default);

        Assert.Null(created.Value!.RelatedExamIds);

        var reread = await h.AdminById.HandleAsync(new ArticleId(created.Value.Id), default);
        Assert.Null(reread.Value!.RelatedExamIds);
    }

    // ── Slug uniqueness ───────────────────────────────────────────────────

    [Fact]
    public async Task A_second_article_cannot_take_an_occupied_slug()
    {
        var h = new Harness();
        await h.Create.HandleAsync(Details(), Author, default);

        var second = await h.Create.HandleAsync(Details(), Author, default);

        Assert.False(second.IsSuccess);
        Assert.Equal(LibraryErrorCodes.SlugTaken, second.Error!.Code);
    }

    [Fact]
    public async Task A_draft_reserves_its_slug_even_before_it_is_published()
    {
        // The rule the class remarks name directly: a draft that holds a
        // slug must block a second draft from also taking it, or publishing
        // either one could collide with a slug the other already claimed.
        var h = new Harness();
        await h.Create.HandleAsync(Details(), Author, default); // Draft, never published

        var second = await h.Create.HandleAsync(Details(), Author, default);

        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task Renaming_an_article_onto_another_ones_slug_is_refused()
    {
        var h = new Harness();
        var first = await h.Create.HandleAsync(Details("bai-mot"), Author, default);
        var second = await h.Create.HandleAsync(Details("bai-hai"), Author, default);

        var renamed = await h.Update.HandleAsync(
            new ArticleId(second.Value!.Id), Details("bai-mot"), default);

        Assert.False(renamed.IsSuccess);
        Assert.Equal(LibraryErrorCodes.SlugTaken, renamed.Error!.Code);
        _ = first;
    }

    [Fact]
    public async Task An_article_can_keep_its_own_slug_when_updated()
    {
        var h = new Harness();
        var created = await h.Create.HandleAsync(Details(), Author, default);

        var updated = await h.Update.HandleAsync(
            new ArticleId(created.Value!.Id),
            Details() with { Title = "Tiêu đề mới" },
            default);

        Assert.True(updated.IsSuccess);
        Assert.Equal("Tiêu đề mới", updated.Value!.Title);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_is_allowed_straight_from_draft()
    {
        // No separate "approved" state — an admin writing and shipping their
        // own post does not submit it to themselves first.
        var h = new Harness();
        var created = await h.Create.HandleAsync(Details(), Author, default);

        var published = await h.ChangeStatus.HandleAsync(
            new ArticleId(created.Value!.Id), LibraryTransition.Publish, default);

        Assert.True(published.IsSuccess);
        Assert.Equal("published", published.Value!.Status);
    }

    [Fact]
    public async Task Publishing_a_published_article_again_is_a_conflict_not_a_second_publish()
    {
        var h = new Harness();
        var article = await PublishedAsync(h);

        var again = await h.ChangeStatus.HandleAsync(article.Id, LibraryTransition.Publish, default);

        Assert.False(again.IsSuccess);
        Assert.Equal(LibraryErrorCodes.ContentStatusConflict, again.Error!.Code);
    }

    [Fact]
    public async Task Returning_a_draft_that_was_never_submitted_is_refused()
    {
        var h = new Harness();
        var created = await h.Create.HandleAsync(Details(), Author, default);

        var returned = await h.ChangeStatus.HandleAsync(
            new ArticleId(created.Value!.Id), LibraryTransition.Return, default);

        Assert.False(returned.IsSuccess);
    }

    [Fact]
    public async Task Deleting_a_published_article_is_refused_until_it_is_unpublished()
    {
        // Learner-visible content disappears by unpublishing, never by a
        // delete with no audit line.
        var h = new Harness();
        var article = await PublishedAsync(h);

        var deleted = await h.Delete.HandleAsync(article.Id, default);
        Assert.False(deleted.IsSuccess);

        await h.ChangeStatus.HandleAsync(article.Id, LibraryTransition.Unpublish, default);
        var deletedAfter = await h.Delete.HandleAsync(article.Id, default);
        Assert.True(deletedAfter.IsSuccess);
    }
}
