using Microsoft.Extensions.Options;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests.Persistence.Exams;

public sealed class MongoSectionMarkingStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 7, 0, 0, TimeSpan.Zero);

    private static async Task<(MongoSectionMarkingStore Store, MongoContext Context)> NewStoreAsync()
    {
        var context = new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27018/?directConnection=true",
            Database = $"vni_ielts_marking_history_{Guid.NewGuid():n}",
        }));
        await context.EnsureIndexesAsync(default);
        return (new MongoSectionMarkingStore(context, new FixedClock(Now)), context);
    }

    [Fact]
    public async Task A_rerun_supersedes_the_prior_version_without_changing_learner_reads()
    {
        var (store, _) = await NewStoreAsync();
        var session = ExamSessionId.New();

        await store.SaveAsync(session, Marking("first"), default);
        await store.SaveAsync(session, Marking("second"), default);

        var learner = await store.ListAsync(session, default);
        var history = await store.QueryAsync(
            new SectionMarkingHistoryQuery(IsCurrent: null), default);

        var current = Assert.Single(learner);
        Assert.Equal("second", current.MarkingId);
        Assert.Equal(2, current.Version);
        Assert.True(current.IsCurrent);
        Assert.Equal("first", current.SupersedesId);

        Assert.Equal(2, history.TotalCount);
        Assert.Equal(["second", "first"], history.Items.Select(i => i.Marking.MarkingId));
        Assert.False(history.Items[1].Marking.IsCurrent);
        Assert.Equal("second", history.Items[1].Marking.SupersededById);
    }

    [Fact]
    public async Task History_filters_current_module_and_flag_and_pages_newest_first()
    {
        var (store, _) = await NewStoreAsync();
        await store.SaveAsync(ExamSessionId.New(), Marking("unflagged"), default);
        await store.SaveAsync(ExamSessionId.New(), Marking("flagged-a", flagged: true), default);
        await store.SaveAsync(ExamSessionId.New(), Marking("flagged-b", flagged: true), default);

        var page = await store.QueryAsync(new SectionMarkingHistoryQuery(
            From: Now.AddMinutes(-1), To: Now.AddMinutes(1), Module: ExamModule.Writing,
            IsFlagged: true, IsCurrent: true, Page: 1, PageSize: 1), default);
        var nextPage = await store.QueryAsync(new SectionMarkingHistoryQuery(
            Module: ExamModule.Writing, IsFlagged: true, IsCurrent: true, Page: 2, PageSize: 1), default);

        Assert.Equal(2, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Equal("flagged-b", item.Marking.MarkingId);
        Assert.True(item.Marking.IsFlagged);
        Assert.Equal("flagged-a", Assert.Single(nextPage.Items).Marking.MarkingId);
    }

    [Fact]
    public async Task A_document_from_before_history_is_a_current_first_version_on_read()
    {
        var (store, context) = await NewStoreAsync();
        var session = ExamSessionId.New();
        await context.SectionMarkings.InsertOneAsync(new SectionMarkingDocument
        {
            Id = "legacy",
            SessionId = session.Value,
            Module = ExamModule.Writing.ToString(),
            TaskNumber = 1,
            RubricVersion = "writing-v1",
            Band = 6m,
            Criteria =
            [
                new CriterionAssessmentDocument
                {
                    Criterion = "taskResponse",
                    Band = 6m,
                    Feedback = "feedback",
                    Evidence = ["evidence"],
                },
            ],
            MarkedAt = Now.UtcDateTime,
        });

        var marking = Assert.Single(await store.ListAsync(session, default));

        Assert.Equal("legacy", marking.MarkingId);
        Assert.Equal(1, marking.Version);
        Assert.True(marking.IsCurrent);
        Assert.False(marking.IsFlagged);
    }

    private static SectionMarking Marking(string id, bool flagged = false) => new(
        ExamModule.Writing,
        "writing-v1",
        [CriterionAssessment.Create("taskResponse", BandScore.Create(6m), "feedback", ["evidence"])],
        BandScore.Create(6m),
        null,
        flagged ? [MarkingFlag.EvidenceNotGrounded] : [],
        [],
        TaskNumber: 1,
        MarkingId: id);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
