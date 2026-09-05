using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Tests.Assessment;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// The sweep that removes audio nothing references.
///
/// <b>An unreferenced recording is a learner's voice with nothing pointing at
/// it</b> — personal data under Vietnam's PDPL, which states storage limitation
/// as a principle. "We kept it because nothing deleted it" is not a lawful
/// basis, and it is what the product was doing.
///
/// The tests that matter here are the ones about <i>not</i> deleting. A sweep
/// that is too eager destroys a learner's only copy of a spoken answer; one
/// that is too cautious costs disk. Those are not comparable, and the code errs
/// the same way these tests do.
/// </summary>
public sealed class RecordingReconciliationTests
{
    private static readonly ExamSessionId Sitting = ExamSessionId.New();
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    private static (RecordingReconciliation Sweep, FakeRecordingStore Store, FakeAnswerSheetStore Sheets)
        Build()
    {
        var store = new FakeRecordingStore();
        var sheets = new FakeAnswerSheetStore();
        var clock = new MovableClock(Now);

        return (new RecordingReconciliation(store, sheets, clock), store, sheets);
    }

    private static async Task<string> UploadAsync(FakeRecordingStore store, string questionId) =>
        await store.SaveAsync(
            Sitting, questionId, new MemoryStream([1, 2, 3]), "audio/ogg", default);

    [Fact]
    public async Task A_recording_the_sheet_still_names_is_left_alone()
    {
        // The property that matters most. Everything else here is about
        // removing things; this is the one that says the sweep cannot remove
        // somebody's answer.
        var (sweep, store, sheets) = Build();

        var id = await UploadAsync(store, "s-part-1");
        await sheets.SetAnswerAsync(Sitting, ExamModule.Speaking, "s-part-1", id, Now, default);

        var report = await sweep.SweepAsync(TimeSpan.Zero, 100, default);

        Assert.Equal(0, report.Orphaned);
        Assert.Empty(store.Deleted);
    }

    [Fact]
    public async Task A_recording_no_sheet_names_is_removed()
    {
        /*
         * How one gets here: an upload streams its bytes and is then refused
         * because the section froze mid-stream. The handler deletes the object,
         * and that delete is allowed to fail — the refusal has to stand whether
         * or not the tidying worked. This is what picks up what it left.
         */
        var (sweep, store, sheets) = Build();

        var id = await UploadAsync(store, "s-part-1");

        var report = await sweep.SweepAsync(TimeSpan.Zero, 100, default);

        Assert.Equal(1, report.Orphaned);
        Assert.Equal(1, report.Removed);
        Assert.Contains(id, store.Deleted);
    }

    [Fact]
    public async Task A_recording_the_sheet_has_replaced_is_removed()
    {
        // The learner re-recorded. Before the object key became derived this
        // happened on every re-record and stranded the previous take every
        // time.
        var (sweep, store, sheets) = Build();

        var stale = await UploadAsync(store, "s-part-1");
        await sheets.SetAnswerAsync(
            Sitting, ExamModule.Speaking, "s-part-1", "a-newer-take", Now, default);

        var report = await sweep.SweepAsync(TimeSpan.Zero, 100, default);

        Assert.Equal(1, report.Orphaned);
        Assert.Contains(stale, store.Deleted);
    }

    [Fact]
    public async Task Nothing_is_examined_beyond_the_batch()
    {
        // Bounded so a sweep can never become a scan of every recording the
        // product has stored, holding a connection for as long as that takes.
        var (sweep, store, _) = Build();

        for (var i = 0; i < 10; i++) await UploadAsync(store, $"s-part-{i}");

        var report = await sweep.SweepAsync(TimeSpan.Zero, 3, default);

        Assert.Equal(3, report.Examined);
    }
}
