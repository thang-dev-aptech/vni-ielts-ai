using Microsoft.Extensions.Options;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests;

/// <summary>
/// Server-side paging for "Tất cả đề" — search, status filter, skip/take and
/// the total count, all evaluated by MongoDB rather than assumed from the
/// in-memory fakes the rest of the suite uses.
/// </summary>
public sealed class MongoExamCatalogueListPagedTests
{
    private static async Task<MongoExamCatalogue> NewCatalogueAsync()
    {
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_exam_paged_{Guid.NewGuid():n}",
        }));

        await context.EnsureIndexesAsync(default);
        return new MongoExamCatalogue(context);
    }

    private static ExamVersion Draft(string title, int versionNumber = 1) =>
        ExamVersion.CreateDraft(
            ExamDefinitionId.New(), versionNumber, title, ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [new Section(ExamModule.Reading, 1, [])]);

    [Fact]
    public async Task Pages_by_skip_and_take_and_reports_the_true_total()
    {
        var catalogue = await NewCatalogueAsync();

        for (var i = 1; i <= 5; i++)
            await catalogue.UpsertAsync(Draft($"Paper {i:00}"), default);

        var (page1, total1) = await catalogue.ListPagedAsync(null, null, skip: 0, take: 2, default);
        var (page2, total2) = await catalogue.ListPagedAsync(null, null, skip: 2, take: 2, default);
        var (page3, total3) = await catalogue.ListPagedAsync(null, null, skip: 4, take: 2, default);

        Assert.Equal(5, total1);
        Assert.Equal(5, total2);
        Assert.Equal(5, total3);
        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.Single(page3);

        // Sorted by title, so the pages partition the set with no overlap and
        // no gap — the property a caller advancing "Trang sau" relies on.
        var seen = page1.Concat(page2).Concat(page3).Select(v => v.Title).ToList();
        Assert.Equal(["Paper 01", "Paper 02", "Paper 03", "Paper 04", "Paper 05"], seen);
    }

    [Fact]
    public async Task Filters_by_title_search_case_insensitively()
    {
        var catalogue = await NewCatalogueAsync();
        await catalogue.UpsertAsync(Draft("Cambridge 18 Test 1"), default);
        await catalogue.UpsertAsync(Draft("Cambridge 18 Test 2"), default);
        await catalogue.UpsertAsync(Draft("Volume 9 Test 3"), default);

        var (matched, total) = await catalogue.ListPagedAsync("cambridge", null, skip: 0, take: 10, default);

        Assert.Equal(2, total);
        Assert.All(matched, v => Assert.Contains("Cambridge", v.Title));
    }

    [Fact]
    public async Task Filters_by_status()
    {
        var catalogue = await NewCatalogueAsync();
        var published = Draft("Published paper");
        published.Publish(DateTimeOffset.UtcNow);
        await catalogue.UpsertAsync(published, default);
        await catalogue.UpsertAsync(Draft("Draft paper"), default);

        var (drafts, draftTotal) = await catalogue.ListPagedAsync(
            null, ExamVersionStatus.Draft, skip: 0, take: 10, default);
        var (publishedOnly, publishedTotal) = await catalogue.ListPagedAsync(
            null, ExamVersionStatus.Published, skip: 0, take: 10, default);

        Assert.Equal(1, draftTotal);
        Assert.Equal("Draft paper", Assert.Single(drafts).Title);
        Assert.Equal(1, publishedTotal);
        Assert.Equal("Published paper", Assert.Single(publishedOnly).Title);
    }

    [Fact]
    public async Task An_unmatched_search_returns_an_empty_page_and_a_zero_total()
    {
        var catalogue = await NewCatalogueAsync();
        await catalogue.UpsertAsync(Draft("Cambridge 18 Test 1"), default);

        var (matched, total) = await catalogue.ListPagedAsync(
            "no such paper", null, skip: 0, take: 10, default);

        Assert.Equal(0, total);
        Assert.Empty(matched);
    }
}
