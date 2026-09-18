using Microsoft.Extensions.Logging;
using Vni.Ielts.Infrastructure.Content;

namespace Vni.Ielts.Infrastructure.Tests.Content;

/// <summary>
/// W4 — an empty dictation shelf must be loud.
///
/// <b>Why this test exists.</b> `fixtures/dictation` was deleted on 2026-08-28
/// and nothing said so. The catalogue found no directory, wrote one
/// Information line, and `/dictation` served an empty list — no error, no
/// warning, one of the four learner modules blank for three weeks. A content
/// store that is empty in a deployment is an incident, and Information is the
/// level you read after you already suspect a problem.
/// </summary>
public sealed class FixtureDictationCatalogueTests
{
    [Fact]
    public void A_missing_fixtures_directory_is_a_warning_not_an_information_line()
    {
        var logger = new RecordingLogger<FixtureDictationCatalogue>();

        var catalogue = new FixtureDictationCatalogue(logger, directory: null);

        Assert.Empty(catalogue.List());
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void A_directory_holding_no_sets_is_a_warning_too()
    {
        // The subtler half. A directory that exists but holds nothing took the
        // other branch entirely and reported "Loaded 0 dictation set(s)" at
        // Information — the same silence, one `if` further along.
        var empty = Directory.CreateTempSubdirectory("dictation-empty-");

        try
        {
            var logger = new RecordingLogger<FixtureDictationCatalogue>();

            var catalogue = new FixtureDictationCatalogue(logger, empty.FullName);

            Assert.Empty(catalogue.List());
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_shelf_with_sets_on_it_does_not_warn()
    {
        // The other side of the assertion. Without this, "always warn" passes
        // both tests above and every boot cries wolf.
        var directory = Directory.CreateTempSubdirectory("dictation-loaded-");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "set.json"),
                """
                {
                  "id": "t-1",
                  "title": "One",
                  "description": "",
                  "sentences": [{ "order": 1, "audio": "assets/a.m4a", "text": "Hello." }]
                }
                """);

            var logger = new RecordingLogger<FixtureDictationCatalogue>();

            var catalogue = new FixtureDictationCatalogue(logger, directory.FullName);

            Assert.Single(catalogue.List());
            Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The real `fixtures/dictation`, found the way the running process finds
    /// it. This is the test that fails if the shelf is ever emptied again.
    /// </summary>
    [Fact]
    public void The_repository_ships_at_least_one_playable_set()
    {
        var logger = new RecordingLogger<FixtureDictationCatalogue>();

        var catalogue = new FixtureDictationCatalogue(logger);
        var sets = catalogue.List();

        Assert.NotEmpty(sets);

        foreach (var set in sets)
        {
            Assert.NotEmpty(set.Sentences);

            foreach (var sentence in set.Sentences)
            {
                Assert.False(string.IsNullOrWhiteSpace(sentence.AudioKey), $"{set.Id} #{sentence.Order} has no audio reference.");
                Assert.False(string.IsNullOrWhiteSpace(sentence.Text), $"{set.Id} #{sentence.Order} has no text.");
            }
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
