using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Dictation;

namespace Vni.Ielts.Application.Dictation;

/// <summary>
/// Dictation content.
///
/// Read-only for now: there is no authoring surface, so sets come from
/// fixtures. The port exists so the CMS can supply them later without anything
/// above this line changing.
/// </summary>
public interface IDictationCatalogue
{
    IReadOnlyList<DictationSet> List();
    DictationSet? Find(string id);
}

/// <summary>Dictation audio. Same shape reasoning as <c>IExamAssetStore</c>.</summary>
public interface IDictationAssetStore
{
    /// <summary>
    /// Null when the reference resolves to nothing. Never throws on a bad path.
    ///
    /// Asynchronous for the same reason as <c>IExamAssetStore.OpenAsync</c>: a
    /// blocking read against object storage on a request thread is how a slow
    /// bucket becomes a thread-pool exhaustion.
    /// </summary>
    Task<DictationAsset?> OpenAsync(string reference, CancellationToken ct);
}

/// <summary>The caller owns the stream and must dispose it.</summary>
public sealed record DictationAsset(
    Stream Content, string ContentType, long? ContentLength = null, string? ETag = null);

/// <summary>
/// Dictation attempts. Append and read; nothing else.
///
/// <b>No update and no delete, for the reason <c>IUsageLedger</c> gives.</b>
/// Progress is a sum over rows, and a port that could rewrite one would make
/// that sum untrustworthy on the day a learner asks why their count went
/// down.
/// </summary>
public interface IDictationResults
{
    Task AppendAsync(DictationAttempt attempt, CancellationToken ct);

    /// <summary>Every attempt this learner has made at this set, any order.</summary>
    Task<IReadOnlyList<DictationAttempt>> ListAsync(string userId, string setId, CancellationToken ct);

    /// <summary>
    /// How many distinct sentences this learner has ever got perfect, per set.
    ///
    /// Its own call rather than <see cref="ListAsync"/> over every set,
    /// because the library screen lists them all and would otherwise read the
    /// whole history to draw one number each.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> PerfectCountsAsync(string userId, CancellationToken ct);
}

/// <param name="PerfectSentences">
/// How many of this set's sentences this learner has ever got word-perfect.
/// Zero for a learner who has not started, which is the honest reading — not
/// a null the library screen would have to translate.
/// </param>
public sealed record DictationSetSummary(
    string Id, string Title, string Description, int SentenceCount, int PerfectSentences);

/// <summary>
/// One sentence as the learner may see it: an order and an audio reference.
///
/// <b>No text.</b> That is the whole point — the sentence is what they are
/// trying to hear, and a client holding it can display the answer.
/// </summary>
/// <param name="BestCorrect">
/// The most words this learner has got right here, or null before their
/// first attempt.
///
/// <b>Null rather than zero, and <see cref="BestTotal"/> withheld with
/// it.</b> The word count is a hint about a sentence nobody has heard yet —
/// small, but the same class of leak as the text itself, and free to avoid.
/// After an attempt the learner has already been shown the sentence, so
/// neither is a secret any more.
/// </param>
/// <param name="Attempts">How many times this learner has tried this sentence.</param>
public sealed record DictationSentenceView(
    int Order, string AudioKey, int? BestCorrect, int? BestTotal, int Attempts);

public sealed record DictationSetView(
    string Id, string Title, string Description, IReadOnlyList<DictationSentenceView> Sentences);

public sealed record WordResultView(string Verdict, string? Expected, string? Typed);

/// <summary>
/// The verdict, and only now the sentence.
///
/// <see cref="Text"/> is returned <b>after</b> an attempt, because the point of
/// dictation is to compare what you heard with what was said — and you cannot
/// do that without eventually being shown it.
/// </summary>
public sealed record DictationResultView(
    int Order, string Text, IReadOnlyList<WordResultView> Words,
    int Correct, int Total, bool IsPerfect);

public sealed class ListDictationSets(IDictationCatalogue catalogue, IDictationResults results)
{
    public async Task<IReadOnlyList<DictationSetSummary>> HandleAsync(string userId, CancellationToken ct)
    {
        var perfect = await results.PerfectCountsAsync(userId, ct);

        return [.. catalogue.List().Select(s =>
            new DictationSetSummary(
                s.Id, s.Title, s.Description, s.Sentences.Count,
                perfect.TryGetValue(s.Id, out var count) ? count : 0))];
    }
}

public sealed class GetDictationSet(IDictationCatalogue catalogue, IDictationResults results)
{
    public async Task<DictationSetView?> HandleAsync(string userId, string id, CancellationToken ct)
    {
        if (catalogue.Find(id) is not { } set) return null;

        /*
         * Read once and group, rather than filter inside the projection: a
         * twenty-sentence set would otherwise walk the learner's whole
         * history twenty times, and the numbers would still be identical —
         * which is how that kind of cost survives review.
         */
        var byOrder = (await results.ListAsync(userId, id, ct))
            .GroupBy(a => a.Order)
            .ToDictionary(g => g.Key, g => g.ToList());

        return new DictationSetView(
            set.Id, set.Title, set.Description,
            [.. set.Sentences.OrderBy(s => s.Order).Select(s =>
            {
                if (!byOrder.TryGetValue(s.Order, out var attempts))
                    return new DictationSentenceView(s.Order, s.AudioKey, null, null, 0);

                /*
                 * Best by score, and `Total` taken from that same row rather
                 * than from the sentence. They agree today; reading it from
                 * the row keeps a set whose text was later corrected honest
                 * about what the learner was actually marked against.
                 */
                var best = attempts.MaxBy(a => a.Correct)!;

                return new DictationSentenceView(
                    s.Order, s.AudioKey, best.Correct, best.Total, attempts.Count);
            })]);
    }
}

public sealed class CheckDictationSentence(
    IDictationCatalogue catalogue, IDictationResults results, IClock clock)
{
    public async Task<DictationResultView?> HandleAsync(
        string userId, string setId, int order, string typed, CancellationToken ct)
    {
        if (catalogue.Find(setId) is not { } set) return null;
        if (set.Sentences.FirstOrDefault(s => s.Order == order) is not { } sentence) return null;

        var comparison = DictationComparer.Compare(sentence.Text, typed);

        /*
         * Written before the verdict is handed back, so a learner who sees a
         * result knows it was counted. The opposite order — answer first,
         * record after — turns a storage outage into a silent one: the screen
         * looks right and the progress quietly does not move.
         */
        await results.AppendAsync(
            new DictationAttempt(
                Guid.NewGuid().ToString("n"),
                userId,
                setId,
                sentence.Order,
                clock.UtcNow,
                comparison.Correct,
                comparison.Total,
                comparison.IsPerfect),
            ct);

        return new DictationResultView(
            sentence.Order,
            sentence.Text,
            [.. comparison.Words.Select(w =>
                new WordResultView(w.Verdict.ToString().ToLowerInvariant(), w.Expected, w.Typed))],
            comparison.Correct,
            comparison.Total,
            comparison.IsPerfect);
    }
}
