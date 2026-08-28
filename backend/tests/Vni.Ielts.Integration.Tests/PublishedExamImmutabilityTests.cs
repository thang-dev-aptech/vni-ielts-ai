using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// A published exam cannot be edited, and that is enforced rather than
/// documented.
///
/// <b>The entity has said "immutable once published" since it was written, and
/// nothing enforced it until 2026-08-28.</b> `UpsertAsync` was a replace-or-
/// insert, so any caller could rewrite a published version wholesale — and one
/// did, on every restart: the development seeder loads the fixtures and
/// publishes them under a deterministic id. Editing a fixture and restarting
/// the API changed the exam <i>underneath</i> every sitting running it. The
/// learner's screen kept the old passage; the marker used the new answer key.
///
/// That failure is invisible until somebody disputes a band, which is the worst
/// possible moment to discover it.
/// </summary>
public sealed class PublishedExamImmutabilityTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private IServiceScope Scope() => app.Services.CreateScope();

    private static IExamCatalogue CatalogueIn(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IExamCatalogue>();

    private static readonly DateTimeOffset At = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A paper built from the seeded catalogue, so the shape is the product's
    /// own rather than one this test invented.
    ///
    /// <b>Given a fresh id every time.</b> These cases publish and then try to
    /// rewrite; sharing one id would make each case depend on what the last one
    /// left behind, which is exactly the class of coupling immutability exists
    /// to prevent.
    /// </summary>
    private static async Task<ExamVersion> SeededPaperAsync(IExamCatalogue catalogue)
    {
        var seeded = (await catalogue.ListAllAsync(default))
            .FirstOrDefault(v => v.Section(ExamModule.Reading) is not null);

        Assert.True(seeded is not null, "No seeded exam carries a Reading section.");

        return ExamVersion.Rehydrate(
            ExamVersionId.New(), ExamDefinitionId.New(), 1, seeded!.Title, seeded.Variant,
            ExamVersionStatus.Draft, null, seeded.Scoring, seeded.Timing, seeded.Sections);
    }

    /// <summary>The same paper with one answer key changed, and nothing else.</summary>
    private static ExamVersion WithAnswer(ExamVersion paper, string accepted)
    {
        var sections = paper.Sections
            .Select(section => section.Module != ExamModule.Reading
                ? section
                : section with
                {
                    Parts = [.. section.Parts.Select((part, index) => index != 0
                        ? part
                        : part with
                        {
                            Questions = [.. part.Questions.Select((question, at) => at != 0
                                ? question
                                : question with
                                {
                                    AnswerKey = new AnswerKey(
                                        [new AcceptedAnswer(accepted, null, null)], null),
                                })],
                        })],
                })
            .ToList();

        return ExamVersion.Rehydrate(
            paper.Id, paper.DefinitionId, paper.VersionNumber, paper.Title, paper.Variant,
            paper.Status, paper.PublishedAt, paper.Scoring, paper.Timing, sections);
    }

    [SkippableFact]
    public async Task A_published_version_cannot_have_its_answer_key_rewritten()
    {
        /*
         * <b>The answer key is the half a silent edit is most damaging to.</b>
         * The passage looks identical on screen and the marking changes, so
         * nothing a learner or an invigilator can see is different — the band
         * is simply wrong, and only for the people who sat it in between.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var catalogue = CatalogueIn(scope);

        var original = WithAnswer(await SeededPaperAsync(catalogue), "cartography");
        await catalogue.UpsertAsync(original, default);

        original.Publish(At);
        await catalogue.UpsertAsync(original, default);

        var rewritten = WithAnswer(original, "something else entirely");

        await Assert.ThrowsAsync<PublishedExamVersionIsImmutableException>(
            () => catalogue.UpsertAsync(rewritten, default));

        // And the stored paper is untouched, not half-written.
        var stored = await catalogue.FindAsync(original.Id, default);
        var key = stored!.Section(ExamModule.Reading)!.Parts[0].Questions[0].AnswerKey!;

        Assert.Equal("cartography", key.Accepted[0].Single);
    }

    [SkippableFact]
    public async Task A_draft_can_still_be_edited_freely()
    {
        // That is what a draft is for. An immutability rule that also froze
        // drafts would make authoring impossible.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var catalogue = CatalogueIn(scope);

        var draft = WithAnswer(await SeededPaperAsync(catalogue), "first");
        await catalogue.UpsertAsync(draft, default);

        await catalogue.UpsertAsync(WithAnswer(draft, "second"), default);

        var stored = await catalogue.FindAsync(draft.Id, default);
        var key = stored!.Section(ExamModule.Reading)!.Parts[0].Questions[0].AnswerKey!;

        Assert.Equal("second", key.Accepted[0].Single);
    }

    [SkippableFact]
    public async Task Publishing_and_unpublishing_a_version_still_work()
    {
        /*
         * <b>Status is not content.</b> Publishing and unpublishing change what
         * a version is <i>for</i>, not what it <i>says</i>, and an immutability
         * check that blocked them would make a published exam impossible to
         * withdraw — which is a worse problem than the one being solved.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var catalogue = CatalogueIn(scope);

        var paper = await SeededPaperAsync(catalogue);
        await catalogue.UpsertAsync(paper, default);

        paper.Publish(At);
        await catalogue.UpsertAsync(paper, default);

        paper.Unpublish();
        await catalogue.UpsertAsync(paper, default);

        var stored = await catalogue.FindAsync(paper.Id, default);

        Assert.Equal(ExamVersionStatus.Unpublished, stored!.Status);
    }
}
