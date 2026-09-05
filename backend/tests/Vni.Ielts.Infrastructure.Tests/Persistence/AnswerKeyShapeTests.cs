using Microsoft.Extensions.Options;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests.Persistence;

/// <summary>
/// A question id is a Mongo field name now, and this is the check that says so.
///
/// <b>It runs without a database, and that is the point of moving it here.</b>
/// The same assertions lived in the integration suite behind
/// <c>Skip.IfNot(MongoAvailable)</c>, so on any machine or CI leg without a
/// server the last line between an authored question id and an update path
/// silently did not run while the suite reported green. Nothing in the guard
/// touches I/O — it throws before the driver is reached — so nothing about it
/// needed a server.
///
/// What it defends: <c>.</c> is a path separator, so <c>a.b</c> writes into a
/// nested object nobody reads, and a leading <c>$</c> is an operator. Either
/// way the value lands somewhere other than where it was addressed. The handler
/// checks ids against the exam's own questions first; this is the line
/// underneath it, for every caller that is not that handler — the seeder and,
/// later, CMS publishing.
/// </summary>
public sealed class AnswerKeyShapeTests
{
    private static readonly ExamSessionId Sitting = ExamSessionId.New();
    private static readonly DateTimeOffset At = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A context with no server behind it. Every case here throws before the
    /// driver is asked for anything, so nothing connects.
    /// </summary>
    private static MongoAnswerSheetStore Store() =>
        new(new MongoContext(Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://127.0.0.1:27017/?serverSelectionTimeoutMS=1",
            Database = "vni_ielts_unit_tests",
        })));

    [Theory]
    [InlineData("r.1")]
    [InlineData("nested.deep.id")]
    [InlineData("$where")]
    [InlineData("")]
    public async Task A_patch_naming_an_illegal_key_is_refused_before_anything_is_written(
        string questionId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Store().PatchAsync(
                Sitting, ExamModule.Reading,
                new Dictionary<string, string?> { [questionId] = "x" }, At, default));
    }

    [Theory]
    [InlineData("r.1")]
    [InlineData("$where")]
    [InlineData("")]
    public async Task Setting_one_entry_under_an_illegal_key_is_refused(string questionId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Store().SetAnswerAsync(Sitting, ExamModule.Speaking, questionId, "x", At, default));
    }

    /// <summary>
    /// <c>$</c> inside an id is fine; only a leading one is an operator.
    ///
    /// Stated as a test because a guard that rejected too much would be just as
    /// broken and much quieter — every autosave in a section refused, with the
    /// sitting stuck and nothing in the log.
    /// </summary>
    [Fact]
    public async Task An_id_that_merely_contains_a_dollar_is_not_refused()
    {
        // It gets past the guard and then fails to reach a server, which is the
        // distinction under test: refused for its shape, or refused for the
        // absence of a database.
        var reached = await Record.ExceptionAsync(() =>
            Store().SetAnswerAsync(Sitting, ExamModule.Reading, "r$1", "x", At, default));

        Assert.IsNotType<ArgumentException>(reached);
    }
}
