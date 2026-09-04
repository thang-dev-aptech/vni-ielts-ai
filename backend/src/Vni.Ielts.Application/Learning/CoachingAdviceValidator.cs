using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vni.Ielts.Application.Learning;

/// <summary>
/// What a model's coaching reply must look like before a learner reads it.
///
/// <b>Advisory text is still untrusted output.</b> It reaches a screen, so
/// the checks are about what a screen can carry: bounded length, only the
/// four skills, no markup, no links (a link in AI text is a phishing vector
/// the product did not write). Anything else is refused, and the learner gets
/// the deterministic advice alone. → CLAUDE.md rule 2
/// </summary>
public static class CoachingAdviceValidator
{
    public const int MaxSummaryChars = 600;
    public const int MaxTipChars = 260;
    public const int MaxTips = 5;

    private static readonly HashSet<string> Modules =
        new(["reading", "listening", "writing", "speaking"], StringComparer.Ordinal);

    private static readonly Regex Forbidden = new(
        @"https?://|www\.|<[a-z/!]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (string Summary, IReadOnlyList<CoachingTip> Tips)? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

        if (!doc.RootElement.TryGetProperty("summary", out var summaryEl)
            || summaryEl.ValueKind != JsonValueKind.String) return null;

        var summary = Clean(summaryEl.GetString() ?? string.Empty);
        if (summary.Length is 0 or > MaxSummaryChars || Forbidden.IsMatch(summary)) return null;

        var tips = new List<CoachingTip>();
        if (doc.RootElement.TryGetProperty("tips", out var tipsEl) && tipsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tip in tipsEl.EnumerateArray())
            {
                if (tip.ValueKind != JsonValueKind.Object) return null;
                var module = tip.TryGetProperty("module", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString()!.Trim().ToLowerInvariant() : null;
                var text = tip.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String
                    ? Clean(tx.GetString()!) : null;

                if (module is null || !Modules.Contains(module) || text is null) return null;
                if (text.Length is 0 or > MaxTipChars || Forbidden.IsMatch(text)) return null;

                tips.Add(new CoachingTip(module, text));
                if (tips.Count > MaxTips) return null;
            }
        }

        return (summary, tips);
    }

    private static string Clean(string s) =>
        Regex.Replace(s.Replace("\r", " ").Replace("\n", " "), @"\s{2,}", " ").Trim();
}
