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
    Stream? Open(string reference);
}

public sealed record DictationSetSummary(string Id, string Title, string Description, int SentenceCount);

/// <summary>
/// One sentence as the learner may see it: an order and an audio reference.
///
/// <b>No text.</b> That is the whole point — the sentence is what they are
/// trying to hear, and a client holding it can display the answer.
/// </summary>
public sealed record DictationSentenceView(int Order, string AudioKey);

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

public sealed class ListDictationSets(IDictationCatalogue catalogue)
{
    public IReadOnlyList<DictationSetSummary> Handle() =>
        [.. catalogue.List().Select(s =>
            new DictationSetSummary(s.Id, s.Title, s.Description, s.Sentences.Count))];
}

public sealed class GetDictationSet(IDictationCatalogue catalogue)
{
    public DictationSetView? Handle(string id)
    {
        if (catalogue.Find(id) is not { } set) return null;

        return new DictationSetView(
            set.Id, set.Title, set.Description,
            [.. set.Sentences.OrderBy(s => s.Order)
                .Select(s => new DictationSentenceView(s.Order, s.AudioKey))]);
    }
}

public sealed class CheckDictationSentence(IDictationCatalogue catalogue)
{
    public DictationResultView? Handle(string setId, int order, string typed)
    {
        if (catalogue.Find(setId) is not { } set) return null;
        if (set.Sentences.FirstOrDefault(s => s.Order == order) is not { } sentence) return null;

        var comparison = DictationComparer.Compare(sentence.Text, typed);

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
