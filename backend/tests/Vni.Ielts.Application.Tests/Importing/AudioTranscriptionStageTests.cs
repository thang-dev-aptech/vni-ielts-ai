using System.Text.Json.Nodes;
using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Application.Tests.Importing;

/// <summary>
/// Task 5 — Listening audio becomes text, unless text was supplied.
///
/// <b>What each group of facts below is actually protecting.</b>
/// <see cref="PassageAnchorCheck"/> reads a <c>recording</c> part's
/// <c>transcript</c> and skips the part when there is none; 0 of 24 Listening
/// parts in this repository's Cambridge packages carry one, so the strongest
/// deterministic check in the import pipeline protects nothing on Listening.
/// This stage fills that field — and the three owner decisions it implements
/// (<c>IP-07</c>…<c>IP-09</c>) are each a refusal to do something that would
/// have been easier: call a model over the top of a supplied transcript,
/// guess which recording belongs to which part, or turn a refusal into an
/// empty string.
/// </summary>
public sealed class AudioTranscriptionStageTests
{
    /// <summary>
    /// <c>IP-08</c>. A transcript in the package is authoritative; calling a
    /// model over the top of it spends money to get a worse answer. Same
    /// principle the answer key already follows.
    /// </summary>
    [Fact]
    public async Task A_supplied_transcript_is_used_and_no_model_is_called()
    {
        var transcriber = new CountingTranscriber();
        var package = ListeningPackage(transcript: "The train leaves at half past six.");

        var result = await AudioTranscriptionStage.RunAsync(
            package, AudioFiles("listening/audio/part1.mp3"), transcriber, default);

        Assert.Equal(0, transcriber.Calls);
        Assert.Contains("half past six", TranscriptAt(result.PackageJson, part: 0));
        Assert.Equal(1, result.Supplied);
        Assert.Equal(0, result.Transcribed);
        Assert.False(result.Changed);
    }

    [Fact]
    public async Task A_part_with_no_transcript_is_transcribed_from_its_audio()
    {
        var transcriber = new StubTranscriber("The train leaves at half past six.");
        var package = ListeningPackage(transcript: null);

        var result = await AudioTranscriptionStage.RunAsync(
            package, AudioFiles("listening/audio/part1.mp3"), transcriber, default);

        Assert.Contains("half past six", TranscriptAt(result.PackageJson, part: 0));
        Assert.Equal(1, result.Transcribed);
        Assert.True(result.Changed);
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// A blank supplied transcript is not a supplied transcript. Whitespace in
    /// that field is how a package that "has" transcripts silently keeps the
    /// anchor check switched off.
    /// </summary>
    [Fact]
    public async Task A_blank_supplied_transcript_does_not_count_as_supplied()
    {
        var transcriber = new StubTranscriber("The train leaves at half past six.");

        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: "   "), AudioFiles("listening/audio/part1.mp3"),
            transcriber, default);

        Assert.Equal(1, transcriber.Calls);
        Assert.Contains("half past six", TranscriptAt(result.PackageJson, part: 0));
    }

    /// <summary>
    /// A transcript is exam content and a Reading passage is not audio.
    /// Sending a Reading part to a transcriber would be a paid call for
    /// nothing, and would write text into a field
    /// <see cref="PassageAnchorCheck"/> does not read for a <c>passage</c>.
    /// </summary>
    [Fact]
    public async Task Reading_parts_are_never_transcribed()
    {
        var transcriber = new CountingTranscriber();

        var result = await AudioTranscriptionStage.RunAsync(
            ReadingPackage(), AudioFiles("listening/audio/part1.mp3"), transcriber, default);

        Assert.Equal(0, transcriber.Calls);
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// A refusal must not become an empty transcript. An empty string reads as
    /// "this recording says nothing", and the anchor check would then report
    /// every answer in the part as absent. → <c>IP-09</c>
    /// </summary>
    [Fact]
    public async Task A_refused_transcription_leaves_the_transcript_absent_and_says_so()
    {
        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null), AudioFiles("listening/audio/part1.mp3"),
            new RefusingTranscriber(), default);

        Assert.Null(TranscriptAt(result.PackageJson, part: 0));
        Assert.Contains(
            result.Warnings,
            w => w.Message.Contains("part 1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(TranscriptionWarningCodes.Refused, Assert.Single(result.Warnings).Code);
    }

    /// <summary>
    /// The same rule from the other side: a provider that answers with blank
    /// text has refused, whatever its status code said. Nothing is written.
    /// </summary>
    [Fact]
    public async Task A_blank_provider_answer_is_a_refusal_not_an_empty_transcript()
    {
        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null), AudioFiles("listening/audio/part1.mp3"),
            new StubTranscriber("   "), default);

        Assert.Null(TranscriptAt(result.PackageJson, part: 0));
        Assert.Single(result.Warnings);
    }

    /// <summary>With no transcriber configured the stage is a no-op, not a failure.</summary>
    [Fact]
    public async Task An_unconfigured_transcriber_changes_nothing()
    {
        var package = ListeningPackage(transcript: null);

        var result = await AudioTranscriptionStage.RunAsync(
            package, AudioFiles("listening/audio/part1.mp3"),
            new NotConfiguredTranscriber(), default);

        Assert.Null(TranscriptAt(result.PackageJson, part: 0));
        Assert.Empty(result.Warnings);
        Assert.Equal(package, result.PackageJson);
    }

    // ── IP-09: when the mapping is uncertain, transcribe nothing ───────────

    /// <summary>
    /// <c>IP-09</c>, and the reason this stage exists in the shape it does.
    /// Three recordings and two files: <b>nothing</b> is transcribed, not two
    /// of three. Transcribing the two that "obviously" line up is exactly how
    /// a transcript lands against the wrong questions — the shifted-answer-key
    /// failure, which surfaces as a wave of false anchor warnings rather than
    /// as a mismatch anybody can read.
    ///
    /// <b>This fact asserts the call count, not only the output.</b> A version
    /// that transcribed two parts and then discarded the text would satisfy an
    /// output-only assertion while still having spent the money and still
    /// having proven nothing about the decision.
    /// </summary>
    [Fact]
    public async Task Fewer_audio_files_than_recordings_transcribes_nothing_and_warns()
    {
        var transcriber = new StubTranscriber("anything at all");

        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null, recordings: 3),
            AudioFiles("listening/audio/part1.mp3", "listening/audio/part2.mp3"),
            transcriber, default);

        Assert.Equal(0, transcriber.Calls);
        Assert.Equal(0, result.Transcribed);
        Assert.Null(TranscriptAt(result.PackageJson, part: 0));
        Assert.Null(TranscriptAt(result.PackageJson, part: 1));
        Assert.Null(TranscriptAt(result.PackageJson, part: 2));

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(TranscriptionWarningCodes.AudioCountMismatch, warning.Code);
    }

    /// <summary>
    /// The mismatch runs both ways. A spare recording in the folder is just as
    /// much a reason not to know which file is which.
    /// </summary>
    [Fact]
    public async Task More_audio_files_than_recordings_transcribes_nothing_and_warns()
    {
        var transcriber = new StubTranscriber("anything at all");

        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null, recordings: 2),
            AudioFiles("listening/audio/a.mp3", "listening/audio/b.mp3", "listening/audio/c.mp3"),
            transcriber, default);

        Assert.Equal(0, transcriber.Calls);
        Assert.Equal(TranscriptionWarningCodes.AudioCountMismatch,
            Assert.Single(result.Warnings).Code);
    }

    /// <summary>
    /// A supplied transcript takes its part out of the count. Two recordings,
    /// one of which brought its own text, and one audio file: that is a match,
    /// not a mismatch — and getting this wrong would mean a package that
    /// supplies some transcripts can never have the rest generated.
    /// </summary>
    [Fact]
    public async Task Only_the_recordings_still_needing_text_are_counted_against_the_audio()
    {
        var transcriber = new StubTranscriber("the second recording");

        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: "the first recording", recordings: 2),
            AudioFiles("listening/audio/part2.mp3"), transcriber, default);

        Assert.Equal(1, transcriber.Calls);
        Assert.Equal("the first recording", TranscriptAt(result.PackageJson, part: 0));
        Assert.Equal("the second recording", TranscriptAt(result.PackageJson, part: 1));
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// When the package states the mapping, it is used and the counts stop
    /// mattering — nothing is being guessed.
    /// </summary>
    [Fact]
    public async Task A_part_that_names_its_own_audio_is_matched_by_that_name()
    {
        var transcriber = new RecordingTranscriber();

        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null, recordings: 1, audioRef: "assets/listening/part4.mp3"),
            AudioFiles(
                "listening/audio/part1.mp3", "listening/audio/part4.mp3",
                "listening/audio/part7.mp3"),
            transcriber, default);

        Assert.Equal(["part4.mp3"], transcriber.FileNames);
        Assert.Equal("text of part4.mp3", TranscriptAt(result.PackageJson, part: 0));
    }

    /// <summary>
    /// A named audio file that is not in the package is a package that says
    /// one thing and holds another. Falling back to order here would build a
    /// confident mapping on top of a contradiction.
    /// </summary>
    [Fact]
    public async Task An_audio_reference_that_matches_no_file_transcribes_nothing_and_warns()
    {
        var transcriber = new StubTranscriber("anything at all");

        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null, recordings: 1, audioRef: "assets/listening/part9.mp3"),
            AudioFiles("listening/audio/part1.mp3"), transcriber, default);

        Assert.Equal(0, transcriber.Calls);
        Assert.Equal(TranscriptionWarningCodes.AudioReferenceUnresolved,
            Assert.Single(result.Warnings).Code);
    }

    /// <summary>
    /// The tempting middle case. Some parts name their audio, some do not, and
    /// the counts happen to line up — so an order-based fallback would look
    /// like it worked. It would be pairing the unnamed parts against a file
    /// list the named ones have already drawn from.
    /// </summary>
    [Fact]
    public async Task A_package_that_names_some_audio_and_not_the_rest_transcribes_nothing()
    {
        var transcriber = new StubTranscriber("anything at all");
        var package = PackageWithParts(
            Part(order: 1, kind: "recording", audioRef: "assets/listening/part1.mp3"),
            Part(order: 2, kind: "recording"));

        var result = await AudioTranscriptionStage.RunAsync(
            package,
            AudioFiles("listening/audio/part1.mp3", "listening/audio/part2.mp3"),
            transcriber, default);

        Assert.Equal(0, transcriber.Calls);
        Assert.Equal(TranscriptionWarningCodes.AudioReferencesMixed,
            Assert.Single(result.Warnings).Code);
    }

    /// <summary>
    /// Positional matching must not depend on the order a ZIP tool happened to
    /// write its central directory in: the same folder re-zipped on another
    /// machine has to produce the same mapping.
    /// </summary>
    [Fact]
    public async Task Positional_matching_sorts_the_audio_rather_than_trusting_archive_order()
    {
        var transcriber = new RecordingTranscriber();

        await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null, recordings: 3),
            AudioFiles(
                "listening/audio/part3.mp3", "listening/audio/part1.mp3",
                "listening/audio/part2.mp3"),
            transcriber, default);

        Assert.Equal(["part1.mp3", "part2.mp3", "part3.mp3"], transcriber.FileNames);
    }

    /// <summary>
    /// <b>Deterministic is not the same as correct.</b> An ordinal sort puts
    /// <c>part10</c> before <c>part2</c> — repeatably, on every re-import, and
    /// wrong every time. Two transcripts then sit under the wrong questions
    /// and every anchorable answer in both parts is reported absent.
    /// </summary>
    [Fact]
    public async Task Positional_matching_orders_part2_before_part10()
    {
        var transcriber = new RecordingTranscriber();

        await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null, recordings: 3),
            AudioFiles(
                "listening/audio/part10.mp3", "listening/audio/part2.mp3",
                "listening/audio/part1.mp3"),
            transcriber, default);

        Assert.Equal(["part1.mp3", "part2.mp3", "part10.mp3"], transcriber.FileNames);
    }

    /// <summary>Leading zeros are a spelling of a number, not a different one.</summary>
    [Fact]
    public async Task Positional_matching_reads_a_zero_padded_number_as_a_number()
    {
        var transcriber = new RecordingTranscriber();

        await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null, recordings: 3),
            AudioFiles(
                "listening/audio/09-part.mp3", "listening/audio/2-part.mp3",
                "listening/audio/10-part.mp3"),
            transcriber, default);

        Assert.Equal(["2-part.mp3", "09-part.mp3", "10-part.mp3"], transcriber.FileNames);
    }

    /// <summary>
    /// <b>A guess the counts agree with is still a guess, and it is shown.</b>
    /// A package whose files are named by content rather than by number gets a
    /// confident wrong mapping from the positional route, and the cost of one
    /// is a whole section of false anchor warnings — which is how a reviewer
    /// learns that these warnings mean nothing and stops reading the real
    /// ones. So the assignment is named, one part per entry, and a reviewer
    /// either recognises it or catches it.
    /// </summary>
    [Fact]
    public async Task The_positional_route_names_the_assignment_it_made()
    {
        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null, recordings: 2),
            AudioFiles("listening/audio/intro.mp3", "listening/audio/lecture.mp3"),
            new RecordingTranscriber(), default);

        var warning = Assert.Single(
            result.Warnings, w => w.Code == TranscriptionWarningCodes.AudioMatchedByOrder);

        // The mapping itself, both halves of both pairs — a reviewer cannot
        // catch an assignment they are not shown.
        Assert.Contains("listening part 1", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("listening/audio/intro.mp3", warning.Message, StringComparison.Ordinal);
        Assert.Contains("listening part 2", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("listening/audio/lecture.mp3", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>And not on a mapping the package stated.</b> A reference route
    /// mapping is proven, not guessed, so there is nothing to judge — and a
    /// warning on a proven mapping is noise that teaches people to click
    /// through warnings, which is the same disease the assignment warning
    /// exists to prevent.
    /// </summary>
    [Fact]
    public async Task The_reference_route_raises_no_assignment_warning()
    {
        var package = PackageWithParts(
            Part(order: 1, kind: "recording", audioRef: "assets/listening/intro.mp3"),
            Part(order: 2, kind: "recording", audioRef: "assets/listening/lecture.mp3"));

        var result = await AudioTranscriptionStage.RunAsync(
            package,
            AudioFiles("listening/audio/intro.mp3", "listening/audio/lecture.mp3"),
            new RecordingTranscriber(), default);

        Assert.Equal("text of intro.mp3", TranscriptAt(result.PackageJson, part: 0));
        Assert.Equal("text of lecture.mp3", TranscriptAt(result.PackageJson, part: 1));
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// One recording and one audio file admits exactly one assignment, so it
    /// is forced rather than guessed. Warning here would put a clearable
    /// warning on every single-part import in the system.
    /// </summary>
    [Fact]
    public async Task A_single_recording_and_a_single_file_is_forced_not_guessed_and_is_not_warned()
    {
        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null, recordings: 1),
            AudioFiles("listening/audio/only.mp3"), new RecordingTranscriber(), default);

        Assert.Equal("text of only.mp3", TranscriptAt(result.PackageJson, part: 0));
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// One unreadable recording is one part without a transcript, not a failed
    /// import. The parse has already been paid for and the next recording may
    /// be perfectly readable.
    /// </summary>
    [Fact]
    public async Task An_audio_file_that_cannot_be_opened_warns_and_leaves_the_rest_alone()
    {
        var transcriber = new RecordingTranscriber();
        var files = new ImportAudioFile[]
        {
            new("listening/audio/part1.mp3", _ => throw new IOException("no such file")),
            new("listening/audio/part2.mp3",
                _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]))),
        };

        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null, recordings: 2), files, transcriber, default);

        Assert.Null(TranscriptAt(result.PackageJson, part: 0));
        Assert.Equal("text of part2.mp3", TranscriptAt(result.PackageJson, part: 1));

        // Two warnings: the by-order assignment this package was given, and
        // the one part that could not be read. The assignment comes first,
        // because a refusal on a part reads differently once you know the part
        // was matched by order rather than by name.
        Assert.Equal(
            [TranscriptionWarningCodes.AudioMatchedByOrder, TranscriptionWarningCodes.Refused],
            result.Warnings.Select(w => w.Code));
    }

    /// <summary>
    /// A warning is read on a CMS screen and copied into a ticket. It names
    /// the part and the category; it never carries a word of what was heard,
    /// nor the answer key, nor the passage.
    /// </summary>
    [Fact]
    public async Task A_refusal_names_the_part_and_never_what_was_heard()
    {
        var result = await AudioTranscriptionStage.RunAsync(
            ListeningPackage(transcript: null), AudioFiles("listening/audio/part1.mp3"),
            new RefusingTranscriber(), default);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("part 1", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("half past six", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/sections/listening/parts/0", warning.Path);
    }

    // ── Doubles ───────────────────────────────────────────────────────────

    private sealed class CountingTranscriber : IAudioTranscriber
    {
        public int Calls { get; private set; }

        public bool IsConfigured => true;

        public Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string fileName, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(TranscriptionResult.Transcribed("unexpected"));
        }
    }

    private sealed class StubTranscriber(string text) : IAudioTranscriber
    {
        public int Calls { get; private set; }

        public bool IsConfigured => true;

        public Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string fileName, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(
                string.IsNullOrWhiteSpace(text)
                    ? new TranscriptionResult(true, text, null)
                    : TranscriptionResult.Transcribed(text));
        }
    }

    /// <summary>Answers with the file's own name, so a mapping can be asserted.</summary>
    private sealed class RecordingTranscriber : IAudioTranscriber
    {
        private readonly List<string> _fileNames = [];

        public IReadOnlyList<string> FileNames => _fileNames;

        public bool IsConfigured => true;

        public Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string fileName, CancellationToken ct)
        {
            _fileNames.Add(fileName);
            return Task.FromResult(TranscriptionResult.Transcribed($"text of {fileName}"));
        }
    }

    private sealed class RefusingTranscriber : IAudioTranscriber
    {
        public bool IsConfigured => true;

        public Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string fileName, CancellationToken ct) =>
            Task.FromResult(
                TranscriptionResult.Refused(TranscriptionRefusalCodes.ProviderRefused));
    }

    /// <summary>
    /// Stands in for <c>UnconfiguredAudioTranscriber</c>, which lives in
    /// Infrastructure and is therefore out of this project's reach. The
    /// property being pinned here is the <b>stage's</b> behaviour when
    /// <see cref="IAudioTranscriber.IsConfigured"/> is false; the real
    /// adapter's own refusal is pinned in
    /// <c>Vni.Ielts.Infrastructure.Tests … AudioTranscriberWiringTests</c>.
    /// </summary>
    private sealed class NotConfiguredTranscriber : IAudioTranscriber
    {
        public bool IsConfigured => false;

        public Task<TranscriptionResult> TranscribeAsync(
            Stream audio, string fileName, CancellationToken ct) =>
            Task.FromResult(TranscriptionResult.Refused(TranscriptionRefusalCodes.NotConfigured));
    }

    // ── Fixtures ──────────────────────────────────────────────────────────

    private static ImportAudioFile[] AudioFiles(params string[] paths) =>
        [.. paths.Select(p => new ImportAudioFile(
            p, _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]))))];

    private static string ListeningPackage(
        string? transcript, int recordings = 1, string? audioRef = null) =>
        PackageWithParts(
            [.. Enumerable.Range(1, recordings)
                .Select(i => Part(
                    order: i,
                    kind: "recording",
                    // Only the first part carries the supplied transcript, so a
                    // package that supplies some and not others is expressible.
                    transcript: i == 1 ? transcript : null,
                    audioRef: audioRef))]);

    private static string ReadingPackage() =>
        PackageWithParts(Part(order: 1, kind: "passage", body: "The hall has a slate roof."));

    private static JsonObject Part(
        int order,
        string kind,
        string? transcript = null,
        string? body = null,
        string? audioRef = null)
    {
        var part = new JsonObject { ["order"] = order, ["kind"] = kind };
        if (transcript is not null) part["transcript"] = transcript;
        if (body is not null) part["body"] = body;
        if (audioRef is not null) part["audio"] = audioRef;
        return part;
    }

    private static string PackageWithParts(params JsonObject[] parts) =>
        new JsonObject
        {
            ["sections"] = new JsonArray(
                new JsonObject
                {
                    ["module"] = parts.Any(p => p["kind"]?.GetValue<string>() == "recording")
                        ? "listening"
                        : "reading",
                    ["parts"] = new JsonArray([.. parts]),
                }),
        }.ToJsonString();

    private static string? TranscriptAt(string packageJson, int part) =>
        JsonNode.Parse(packageJson)?["sections"]?[0]?["parts"]?[part]?["transcript"]
            ?.GetValue<string>();
}
