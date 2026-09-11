using System.Text.Json.Nodes;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// Gives every Listening recording in a parsed package a <c>transcript</c>,
/// unless the package already brought one.
///
/// ── Why this stage exists, measured ───────────────────────────────────────
///
/// <see cref="PassageAnchorCheck"/> selects a part's text by its declared
/// <c>kind</c>: a <c>passage</c> anchors in its <c>body</c>, a
/// <c>recording</c> in its <c>transcript</c>. <b>0 of 24 Listening parts in
/// this repository's Cambridge packages carry a transcript</b>, so the
/// strongest deterministic check in the whole pipeline skips every Listening
/// part and protects nothing there. This stage produces the text those parts
/// lack, and it runs at <see cref="ImportJobStage.Transcribing"/> — between
/// parsing and keying — so the anchor check has something to search by the
/// time it runs.
///
/// ── The three rules, and the failure each one prevents ────────────────────
///
/// <b><c>IP-08</c> — a supplied transcript wins, and no model is called.</b>
/// The same principle the answer key already follows: where an authoritative
/// source exists, a model does not guess. Cambridge books ship transcripts,
/// and a supplied one is both free and better than a generated one.
///
/// <b><c>IP-09</c> — when the recordings cannot be matched to the parts with
/// certainty, transcribe nothing and say so.</b> Guessing which recording
/// belongs to which part is the shifted-answer-key failure wearing different
/// clothes: it produces a well-formed package in which every transcript sits
/// against the wrong questions, and it surfaces downstream as a wave of false
/// anchor warnings rather than as a mismatch anybody can read. Half a package
/// transcribed correctly is not worth the other half transcribed wrongly, so
/// the refusal is all-or-nothing for the whole package.
///
/// <b><c>IP-09</c> — a refusal never becomes an empty transcript.</b> Absent
/// and empty are different states. <c>""</c> reads as "this recording says
/// nothing", and the anchor check would then report every anchorable answer
/// in that part as absent. This class writes <c>transcript</c> only on a
/// success carrying non-blank text.
///
/// ── What this is not ──────────────────────────────────────────────────────
///
/// <b>It transcribes published exam audio, never a learner's speech.</b> See
/// <see cref="IAudioTranscriber"/>'s own remarks: the Speaking-marking seam
/// (<see cref="Vni.Ielts.Application.Assessment.ITranscriptSource"/> /
/// <c>NoTranscriptSource</c>) is a different port under a different
/// deferral (<c>P-02</c>) and is untouched by any of this.
/// </summary>
public static class AudioTranscriptionStage
{
    /// <summary>
    /// Runs the stage over one parsed package.
    /// </summary>
    /// <param name="packageJson">The parsed package. Never mutated in place.</param>
    /// <param name="audio">
    /// Every <see cref="PackageEntryRole.Audio"/> file in the package. Order is
    /// the caller's; <see cref="RunAsync"/> sorts it before any positional
    /// matching, so the outcome does not depend on the order a ZIP tool
    /// happened to write the central directory in.
    /// </param>
    public static async Task<AudioTranscriptionResult> RunAsync(
        string packageJson,
        IReadOnlyList<ImportAudioFile> audio,
        IAudioTranscriber transcriber,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(packageJson);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(transcriber);

        /*
         * <b>Unconfigured is a no-op, not a failure, and not even a
         * warning.</b> A deployment with no transcription provider is the
         * supported default (`G-11`), and today it is every deployment. A
         * warning on every Listening part of every import would be noise a
         * reviewer learns to clear without reading — which is how a real
         * warning gets cleared without being read too.
         */
        if (!transcriber.IsConfigured) return Unchanged(packageJson);

        var package = JsonNode.Parse(packageJson)?.AsObject()
            ?? throw new ArgumentException(
                "The package did not parse as an object.", nameof(packageJson));

        var recordings = Recordings(package).ToArray();
        if (recordings.Length == 0) return Unchanged(packageJson);

        var supplied = recordings.Count(r => HasTranscript(r.Part));
        var needing = recordings.Where(r => !HasTranscript(r.Part)).ToArray();
        if (needing.Length == 0) return new AudioTranscriptionResult(packageJson, [], supplied, 0);

        var match = Match(needing, audio);
        if (match.Refusal is { } refusal)
            return new AudioTranscriptionResult(packageJson, [refusal], supplied, 0);

        var warnings = new List<TranscriptionWarning>();
        var transcribed = 0;

        foreach (var (recording, file) in match.Pairs)
        {
            ct.ThrowIfCancellationRequested();

            var outcome = await TranscribeOneAsync(transcriber, file, ct);

            if (outcome is { IsSuccess: true, Text: { } text } && !string.IsNullOrWhiteSpace(text))
            {
                recording.Part["transcript"] = text;
                transcribed++;
                continue;
            }

            /*
             * Nothing is written. Not `""`, not a placeholder sentence —
             * see the type's own remarks and `IP-09`. The part stays in the
             * state it was already in: no transcript, which
             * `PassageAnchorCheck` skips rather than failing.
             */
            warnings.Add(RefusalWarning(recording, outcome.RefusalCode));
        }

        return new AudioTranscriptionResult(
            transcribed > 0 ? package.ToJsonString() : packageJson,
            warnings,
            supplied,
            transcribed);
    }

    private static AudioTranscriptionResult Unchanged(string packageJson) =>
        new(packageJson, [], 0, 0);

    /// <summary>
    /// One call, with the file opened and closed around it and every
    /// foreseeable failure turned into a refusal rather than an exception.
    ///
    /// <b>An unreadable file, a provider that throws, a socket that dies: all
    /// of them are this one part having no transcript.</b> None of them is a
    /// reason to abandon an import that has already paid for a parse, and the
    /// next part's recording may be perfectly readable. A caller's own
    /// cancellation is the one thing that is <i>not</i> swallowed: it is not a
    /// verdict on the package.
    /// </summary>
    private static async Task<TranscriptionResult> TranscribeOneAsync(
        IAudioTranscriber transcriber, ImportAudioFile file, CancellationToken ct)
    {
        try
        {
            await using var stream = await file.OpenAsync(ct);
            return await transcriber.TranscribeAsync(stream, file.FileName, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            return TranscriptionResult.Refused(TranscriptionRefusalCodes.AudioUnreadable);
        }
        catch (UnauthorizedAccessException)
        {
            return TranscriptionResult.Refused(TranscriptionRefusalCodes.AudioUnreadable);
        }
        catch (Exception)
        {
            /*
             * The provider's own exception type is an Infrastructure concern
             * and must not be named here. Its message is not carried either:
             * a provider that echoes its request back would put the recording
             * in a review warning an administrator reads. The code says the
             * category; the adapter's own log says the rest.
             */
            return TranscriptionResult.Refused(TranscriptionRefusalCodes.ProviderRefused);
        }
    }

    /// <summary>
    /// Which recording belongs to which part, or the single warning that says
    /// nobody can tell.
    ///
    /// ── Two routes, and a refusal wherever they disagree ──────────────────
    ///
    /// <b>The reference route</b> applies when <b>every</b> part needing text
    /// names its own <c>audio</c> asset and every one of those names resolves
    /// to exactly one file. Then the package has stated the mapping and
    /// nothing is being guessed.
    ///
    /// <b>The positional route</b> applies when <b>no</b> part names one. Then
    /// the only available mapping is order against order, and the count is the
    /// only evidence it is right — so it is required to be exact. <c>4</c>
    /// parts and <c>3</c> recordings, or <c>4</c> and <c>5</c>, means the
    /// package is not what this code thinks it is, and the honest answer is to
    /// transcribe nothing.
    ///
    /// <b>Everything else is a refusal</b>, including the mixed case where
    /// some parts name their audio and some do not. That is the tempting one:
    /// it looks like it could be resolved by using references where they exist
    /// and order for the rest. It cannot — the unreferenced parts' order runs
    /// against a file list that the referenced parts have already claimed
    /// entries from, and the result would be a confident mapping built on an
    /// assumption nothing in the package supports. → <c>IP-09</c>
    /// </summary>
    private static (IReadOnlyList<(Recording Recording, ImportAudioFile File)> Pairs,
        TranscriptionWarning? Refusal) Match(
        IReadOnlyList<Recording> needing, IReadOnlyList<ImportAudioFile> audio)
    {
        var referenced = needing.Where(r => AudioReference(r.Part) is not null).ToArray();

        if (referenced.Length == needing.Count)
        {
            var pairs = new List<(Recording, ImportAudioFile)>(needing.Count);
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var recording in needing)
            {
                var reference = AudioReference(recording.Part)!;
                var candidates = audio
                    .Where(f => Matches(f, reference))
                    .Where(f => !claimed.Contains(f.RelativePath))
                    .ToArray();

                if (candidates.Length != 1)
                {
                    // The count, never the reference itself — an asset path is
                    // package content and this message reaches a CMS screen.
                    return ([], new TranscriptionWarning(
                        TranscriptionWarningCodes.AudioReferenceUnresolved,
                        recording.Path,
                        $"{Describe(recording)} names its own audio file, and that name matches "
                        + $"{candidates.Length} of the {audio.Count} audio files in this package. "
                        + "Nothing was transcribed for any part: a recording matched to the wrong "
                        + "part puts a transcript against the wrong questions, which is the same "
                        + "failure as a shifted answer key and shows up as a wave of false "
                        + "warnings rather than as a mismatch anybody can read. Correct the "
                        + "part's audio reference, or supply the transcripts in the package."));
                }

                claimed.Add(candidates[0].RelativePath);
                pairs.Add((recording, candidates[0]));
            }

            return (pairs, null);
        }

        if (referenced.Length > 0)
        {
            return ([], new TranscriptionWarning(
                TranscriptionWarningCodes.AudioReferencesMixed,
                referenced[0].Path,
                $"{referenced.Length} of the {needing.Count} recordings needing a transcript name "
                + "their own audio file and the rest do not. Nothing was transcribed: matching the "
                + "unnamed ones by order against a file list the named ones have already drawn "
                + "from is a guess, and a transcript against the wrong questions is the same "
                + "failure as a shifted answer key. Name every recording's audio, or name none of "
                + "them and supply exactly one audio file per recording."));
        }

        /*
         * Sorted, not taken in the caller's order. The caller's order is the
         * ZIP central directory's, which is whatever tool wrote the archive —
         * and a positional mapping that depends on that is a mapping that
         * changes when somebody re-zips the same folder on a different
         * machine. An ordinal sort of the relative path is at least stable and
         * is what an administrator looking at the folder would predict.
         */
        var ordered = audio.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray();

        if (ordered.Length != needing.Count)
        {
            return ([], new TranscriptionWarning(
                TranscriptionWarningCodes.AudioCountMismatch,
                needing[0].Path,
                $"{needing.Count} recording(s) in this package have no transcript and the package "
                + $"holds {ordered.Length} audio file(s). Nothing was transcribed: with the counts "
                + "unequal there is no way to tell which recording belongs to which part, and a "
                + "transcript against the wrong questions is the same failure as a shifted answer "
                + "key — it shows up as a wave of false warnings rather than as a mismatch "
                + "anybody can read. Supply one audio file per recording, name each part's own "
                + "audio, or supply the transcripts in the package."));
        }

        return ([.. needing.Zip(ordered)], null);
    }

    /// <summary>
    /// Whether one audio file is the one an <c>audio</c> asset reference
    /// names.
    ///
    /// <b>The reference and the archive path are written in two different
    /// coordinate systems, on purpose.</b> An <c>assetRef</c> in
    /// <c>exam.schema.json</c> is <c>assets/…</c>; a
    /// <see cref="PackageLayout"/> path is
    /// <c>&lt;skill&gt;/&lt;role&gt;/…</c>. So an exact path match is tried
    /// first, for the package that happens to use one naming, and the file
    /// name alone second. Anything that matches two files is treated as
    /// matching none by the caller — an ambiguous name is not a mapping.
    /// </summary>
    private static bool Matches(ImportAudioFile file, string reference) =>
        string.Equals(file.RelativePath, reference, StringComparison.OrdinalIgnoreCase)
        || string.Equals(file.FileName, Path.GetFileName(reference), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A part's own <c>audio</c> asset reference, or null when it names none.
    /// </summary>
    private static string? AudioReference(JsonObject part)
    {
        var value = part["audio"] is JsonValue node && node.TryGetValue<string>(out var s) ? s : null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool HasTranscript(JsonObject part) =>
        part["transcript"] is JsonValue node
        && node.TryGetValue<string>(out var text)
        && !string.IsNullOrWhiteSpace(text);

    private static TranscriptionWarning RefusalWarning(Recording recording, string? code) =>
        new(TranscriptionWarningCodes.Refused,
            recording.Path,
            $"{Describe(recording)} was left with no transcript: the transcription was refused "
            + $"({code ?? TranscriptionRefusalCodes.ProviderRefused}). It is deliberately left "
            + "absent rather than empty — an empty transcript would read as 'this recording says "
            + "nothing' and would report every answer in this part as missing from it. Supply the "
            + "transcript in the package, or re-run the import once the refusal is resolved.");

    /// <summary>
    /// How a warning names a part. <b>The part number and the skill, never a
    /// word of its content.</b> A transcript, a passage and an answer key are
    /// exam content; a warning is read on a CMS screen and copied into a
    /// ticket.
    /// </summary>
    private static string Describe(Recording recording) =>
        $"{recording.Module} part {recording.Number}";

    /// <param name="Number">
    /// The part's declared <c>order</c> when it has one, else its position in
    /// the section, one-based. An administrator counts parts from one.
    /// </param>
    private sealed record Recording(JsonObject Part, string Path, string Module, int Number);

    /// <summary>
    /// Every part that <b>is</b> a recording, in the package's own order.
    ///
    /// <b>Selected by the declared <c>kind</c>, exactly as
    /// <see cref="PassageAnchorCheck"/> selects it</b> — never by which fields
    /// happen to be populated, and never by the section's module. Those two
    /// agree today, and a Listening section whose parts were parsed as
    /// <c>passage</c> is a parse failure this stage must not paper over by
    /// transcribing into a field the anchor check will not read. A Reading
    /// part is a <c>passage</c> and is therefore never sent to a transcriber:
    /// it has no audio, and the call would be paid for nothing.
    /// </summary>
    private static IEnumerable<Recording> Recordings(JsonObject package)
    {
        foreach (var section in package["sections"]?.AsArray() ?? [])
        {
            var module = section?["module"]?.GetValue<string>() ?? "unknown";
            var partIndex = -1;

            foreach (var part in section?["parts"]?.AsArray() ?? [])
            {
                partIndex++;
                if (part is not JsonObject p) continue;
                if (p["kind"]?.GetValue<string>() != "recording") continue;

                var number = p["order"] is JsonValue order && order.TryGetValue<int>(out var n)
                    ? n
                    : partIndex + 1;

                yield return new Recording(p, $"/sections/{module}/parts/{partIndex}", module, number);
            }
        }
    }
}
