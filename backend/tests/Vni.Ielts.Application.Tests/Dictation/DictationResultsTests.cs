using Vni.Ielts.Application.Dictation;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Dictation;

namespace Vni.Ielts.Application.Tests.Dictation;

/// <summary>
/// Dictation results survive the request that produced them.
///
/// <b>Until this landed, a check was compared and thrown away.</b> A learner
/// could work through a twenty-sentence set, close the tab, and come back to
/// a screen that had never heard of them — the one thing that makes repeated
/// practice feel like practice rather than a demo.
///
/// The rows are append-only (<c>IDictationResults</c> says why), so every
/// number here is derived by reading them back rather than kept as a total.
/// </summary>
public sealed class DictationResultsTests
{
    private const string Learner = "user-1";

    private static DictationSet Set() => new(
        "set-1", "Cambridge 17 · Part 1", "Ten sentences",
        [
            new DictationSentence(1, "audio/d1.mp3", "the meeting starts at nine"),
            new DictationSentence(2, "audio/d2.mp3", "bring your passport"),
        ]);

    private sealed class Catalogue : IDictationCatalogue
    {
        public IReadOnlyList<DictationSet> List() => [Set()];
        public DictationSet? Find(string id) => id == "set-1" ? Set() : null;
    }

    /// <summary>
    /// Append and read. Deliberately not a dictionary keyed by sentence: the
    /// port promises every attempt is kept, and a fake that quietly collapsed
    /// them would let a handler that overwrites history pass.
    /// </summary>
    private sealed class FakeResults : IDictationResults
    {
        public List<DictationAttempt> Rows { get; } = [];

        public Task AppendAsync(DictationAttempt attempt, CancellationToken ct)
        {
            Rows.Add(attempt);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DictationAttempt>> ListAsync(
            string userId, string setId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DictationAttempt>>(
                [.. Rows.Where(r => r.UserId == userId && r.SetId == setId)]);

        public Task<IReadOnlyDictionary<string, int>> PerfectCountsAsync(
            string userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(
                Rows.Where(r => r.UserId == userId && r.IsPerfect)
                    .GroupBy(r => r.SetId)
                    .ToDictionary(g => g.Key, g => g.Select(r => r.Order).Distinct().Count()));
    }

    private sealed class FixedClock(DateTimeOffset at) : IClock
    {
        public DateTimeOffset UtcNow => at;
    }

    [Fact]
    public async Task A_check_is_kept_rather_than_compared_and_forgotten()
    {
        var results = new FakeResults();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 18, 3, 0, 0, TimeSpan.Zero));
        var check = new CheckDictationSentence(new Catalogue(), results, clock);

        await check.HandleAsync(Learner, "set-1", 1, "the meeting starts at ten", default);

        var row = Assert.Single(results.Rows);
        Assert.Equal(Learner, row.UserId);
        Assert.Equal("set-1", row.SetId);
        Assert.Equal(1, row.Order);
        Assert.Equal(clock.UtcNow, row.At);
        Assert.Equal(4, row.Correct);
        Assert.Equal(5, row.Total);
        Assert.False(row.IsPerfect);
    }

    /// <summary>
    /// Two tries at the same sentence are two rows. A store that kept only
    /// the latest would report a learner who regressed as having never got it
    /// right, and <c>BestCorrect</c> below would follow it down.
    /// </summary>
    [Fact]
    public async Task A_second_try_at_the_same_sentence_is_a_second_row()
    {
        var results = new FakeResults();
        var check = new CheckDictationSentence(
            new Catalogue(), results, new FixedClock(DateTimeOffset.UtcNow));

        await check.HandleAsync(Learner, "set-1", 1, "wrong", default);
        await check.HandleAsync(Learner, "set-1", 1, "the meeting starts at nine", default);

        Assert.Equal(2, results.Rows.Count);
        Assert.True(results.Rows[1].IsPerfect);
    }

    [Fact]
    public async Task The_set_reports_the_learners_best_run_at_each_sentence()
    {
        var results = new FakeResults();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var check = new CheckDictationSentence(new Catalogue(), results, clock);

        await check.HandleAsync(Learner, "set-1", 1, "the meeting starts at ten", default);
        await check.HandleAsync(Learner, "set-1", 1, "the meeting starts at nine", default);
        await check.HandleAsync(Learner, "set-1", 1, "nothing like it", default);

        var view = await new GetDictationSet(new Catalogue(), results)
            .HandleAsync(Learner, "set-1", default);

        var first = view!.Sentences.Single(s => s.Order == 1);
        Assert.Equal(5, first.BestCorrect);
        Assert.Equal(5, first.BestTotal);
        Assert.Equal(3, first.Attempts);
    }

    /// <summary>
    /// <b>The word count is withheld until the learner has heard it.</b>
    /// `BestTotal` on an untried sentence would tell them how many words to
    /// expect — a smaller leak than the text, and free to avoid.
    /// </summary>
    [Fact]
    public async Task An_untried_sentence_reports_nothing_at_all_about_itself()
    {
        var results = new FakeResults();
        var check = new CheckDictationSentence(
            new Catalogue(), results, new FixedClock(DateTimeOffset.UtcNow));

        await check.HandleAsync(Learner, "set-1", 1, "the meeting starts at nine", default);

        var view = await new GetDictationSet(new Catalogue(), results)
            .HandleAsync(Learner, "set-1", default);

        var untried = view!.Sentences.Single(s => s.Order == 2);
        Assert.Null(untried.BestCorrect);
        Assert.Null(untried.BestTotal);
        Assert.Equal(0, untried.Attempts);
    }

    [Fact]
    public async Task The_library_counts_the_sentences_this_learner_has_got_perfect()
    {
        var results = new FakeResults();
        var check = new CheckDictationSentence(
            new Catalogue(), results, new FixedClock(DateTimeOffset.UtcNow));

        await check.HandleAsync(Learner, "set-1", 1, "the meeting starts at nine", default);
        await check.HandleAsync(Learner, "set-1", 1, "the meeting starts at nine", default);
        await check.HandleAsync(Learner, "set-1", 2, "bring your wallet", default);

        var sets = await new ListDictationSets(new Catalogue(), results)
            .HandleAsync(Learner, default);

        // One, not two: sentence 1 was perfect twice and is still one sentence.
        Assert.Equal(1, Assert.Single(sets).PerfectSentences);
    }

    [Fact]
    public async Task One_learners_progress_is_not_another_learners()
    {
        var results = new FakeResults();
        var check = new CheckDictationSentence(
            new Catalogue(), results, new FixedClock(DateTimeOffset.UtcNow));

        await check.HandleAsync("user-2", "set-1", 1, "the meeting starts at nine", default);

        var view = await new GetDictationSet(new Catalogue(), results)
            .HandleAsync(Learner, "set-1", default);

        Assert.All(view!.Sentences, s => Assert.Equal(0, s.Attempts));
        Assert.Equal(0, Assert.Single(
            await new ListDictationSets(new Catalogue(), results).HandleAsync(Learner, default))
            .PerfectSentences);
    }
}
