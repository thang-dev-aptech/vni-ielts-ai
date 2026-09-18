using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Dictation;
using Vni.Ielts.Domain.Dictation;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// W4 — the boot log says when a content shelf is empty.
///
/// <b>Deliberately not a readiness probe.</b> Readiness answers "can this
/// instance serve a request", and an empty dictation shelf does not stop the
/// API serving exams, sign-in or Writing marking. Wiring content into
/// readiness would mean one missing content package takes the whole product
/// down — an outage manufactured by the monitoring choice rather than by the
/// fault. So this follows the shape
/// `ContentRights:AllowPublicationWithoutProvenRights` already uses: an
/// abnormal state is announced at startup, on the `[config]` channel, where
/// whoever inherits the deployment reads it.
/// </summary>
public sealed class DictationContentAnnounceTests
{
    [Fact]
    public void An_empty_dictation_catalogue_is_announced_at_startup()
    {
        var warnings = StartupConfiguration.ContentInventoryWarnings(new StubCatalogue([]));

        var line = Assert.Single(warnings);
        Assert.Contains("dictation", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_stocked_dictation_catalogue_says_nothing()
    {
        // Without this the announcement can be unconditional, which is the same
        // as no announcement: a line printed on every boot is read on none.
        var set = new DictationSet(
            "everyday-1", "Câu hằng ngày", "",
            [new DictationSentence(1, "assets/sentence-1.m4a", "The library opens at half past eight.")]);

        var warnings = StartupConfiguration.ContentInventoryWarnings(new StubCatalogue([set]));

        Assert.Empty(warnings);
    }

    private sealed class StubCatalogue(IReadOnlyList<DictationSet> sets) : IDictationCatalogue
    {
        public IReadOnlyList<DictationSet> List() => sets;

        public DictationSet? Find(string id) => sets.FirstOrDefault(s => s.Id == id);
    }
}
