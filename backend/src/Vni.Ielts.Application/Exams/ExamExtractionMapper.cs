using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Exams;

/// <summary>
/// One chunk of one document, and the provenance this product knows about it.
/// </summary>
public sealed record ExamExtractionChunkBinding(string ChunkId, int? Page, string? Section);

/// <summary>
/// The mapping between an opaque provider-facing source identifier and the
/// archive entry it stands for. Lives on this side of the boundary; never sent.
/// </summary>
public sealed record ExamExtractionSourceBinding(
    string SourceId,
    string EntryPath,
    string Sha256,
    IReadOnlyList<ExamExtractionChunkBinding> Chunks);

/// <summary>
/// Turns a provenance reference in a provider's answer into provenance this
/// product can stand behind.
///
/// <para>
/// <b>Server-side provenance wins over the model's.</b> When a proposal names a
/// chunk, the page and section come from what the extractor recorded for that
/// chunk — not from what the model said about it. A model that quotes a real
/// chunk id and an invented page number would otherwise write a plausible
/// citation nobody can follow, which is worse than no citation. When no chunk
/// is named, a page number is accepted only if the document actually has that
/// page.
/// </para>
/// </summary>
public sealed class ExamExtractionSourceIndex
{
    private readonly Dictionary<string, ExamExtractionSourceBinding> _bySourceId;

    public ExamExtractionSourceIndex(IReadOnlyList<ExamExtractionSourceBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        if (bindings.Count == 0)
            throw new ArgumentException("At least one source binding is required.", nameof(bindings));

        _bySourceId = bindings.ToDictionary(binding => binding.SourceId, StringComparer.Ordinal);
    }

    /// <summary>One provenance per supplied document, for the candidate's source list.</summary>
    public IReadOnlyList<ParsedSourceProvenance> DocumentProvenances =>
        [.. _bySourceId.Values
            .OrderBy(binding => binding.SourceId, StringComparer.Ordinal)
            .Select(binding => new ParsedSourceProvenance(binding.EntryPath, null, null, binding.Sha256))];

    public ParsedSourceProvenance Resolve(ExamExtractionProvenanceDto? provenance, string where)
    {
        if (provenance?.SourceId is not { Length: > 0 } sourceId)
        {
            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.MissingField,
                $"{where} carries no source provenance, so nothing it proposes could be traced back "
                + "to the uploaded document.");
        }

        if (!_bySourceId.TryGetValue(sourceId, out var binding))
        {
            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.DanglingSource,
                $"{where} references a source identifier this request never issued.");
        }

        if (provenance.ChunkId is { Length: > 0 } chunkId)
        {
            var chunk = binding.Chunks.FirstOrDefault(c =>
                string.Equals(c.ChunkId, chunkId, StringComparison.Ordinal));

            if (chunk is null)
            {
                throw new ExamExtractionRejectedException(
                    ExamExtractionRejection.DanglingChunk,
                    $"{where} references a chunk identifier this request never issued for that source.");
            }

            return new ParsedSourceProvenance(binding.EntryPath, chunk.Page, chunk.Section, binding.Sha256);
        }

        if (provenance.Page is { } page)
        {
            if (page <= 0 || !binding.Chunks.Any(c => c.Page == page))
            {
                throw new ExamExtractionRejectedException(
                    ExamExtractionRejection.DanglingPage,
                    $"{where} cites a page the referenced document does not have.");
            }

            var section = binding.Chunks.First(c => c.Page == page).Section;
            return new ParsedSourceProvenance(binding.EntryPath, page, section, binding.Sha256);
        }

        return new ParsedSourceProvenance(binding.EntryPath, null, null, binding.Sha256);
    }
}

/// <summary>
/// The server-side half of requirement <c>A-6</c> for raw-package parsing:
/// everything a provider answered, re-checked in application code and then
/// mapped onto a review candidate.
///
/// ── What this layer is for ────────────────────────────────────────────────
///
/// <b>Schema validation happens before this and is not enough.</b> A schema can
/// say "sourceId is a string of at most 64 characters"; it cannot say "that
/// string is one of the four identifiers this request issued". Referential
/// integrity, option/answer-key coherence and the candidate's own invariants
/// are relationships between values, and relationships are what a model gets
/// plausibly wrong. → <c>docs/ai/output-contracts.md</c>
///
/// <b>Package identity is not negotiable and not asked for.</b> The contract
/// has no package field, so a proposal cannot claim to belong to a different
/// upload; the identifiers come from the caller.
/// </summary>
public static class ExamExtractionMapper
{
    /// <summary>
    /// The question types whose answer is a key from a printed bank.
    ///
    /// <b>An answer key for one of these without options is incoherent</b>, not
    /// merely incomplete: the stored answer would be the letter <c>B</c> with
    /// nothing to check <c>B</c> against, and a reviewer would have to find the
    /// bank in the source by hand to know whether the key is even wrong.
    /// </summary>
    private static readonly QuestionType[] KeyedTypes =
        [QuestionType.MultipleChoice, QuestionType.MultipleSelect, QuestionType.Matching];

    public static ParsedExamCandidate ToCandidate(
        ExamExtractionDto extraction,
        string candidateId,
        string packageId,
        ExamExtractionSourceIndex sources)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(sources);
        if (string.IsNullOrWhiteSpace(candidateId))
            throw new ArgumentException("A candidate identifier is required.", nameof(candidateId));
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("A package identifier is required.", nameof(packageId));

        if (!string.Equals(extraction.ContractVersion, ExamExtractionContract.Version, StringComparison.Ordinal))
        {
            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.ContractVersion,
                "The response does not declare the extraction contract version this server validates.");
        }

        var classification = Classification(extraction.Classification, "The extraction");
        var confidence = Confidence(extraction.Confidence, "The extraction");
        var modules = (extraction.Modules ?? []).Select((module, index) =>
            Module(module, index, sources)).ToArray();

        try
        {
            return ParsedExamCandidate.Create(
                candidateId,
                packageId,
                Trimmed(extraction.Title),
                classification,
                confidence,
                modules,
                sources.DocumentProvenances);
        }
        catch (Exception e) when (e is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            /*
             * <b>The domain's own invariants are part of the validation, not a
             * layer behind it.</b> Unique positive ordering, module/classification
             * agreement and confidence bounds are already expressed on
             * ParsedExamCandidate; re-implementing them here would create a second
             * copy to drift. What this does add is a stable code, so a refusal
             * from a provider looks the same to an operator whichever check
             * caught it.
             */
            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.CandidateInvariant,
                $"The extraction does not satisfy the review candidate's invariants: {e.Message}");
        }
    }

    private static ParsedModuleCandidate Module(
        ExamExtractionModuleDto? module, int index, ExamExtractionSourceIndex sources)
    {
        var where = $"Module {index + 1}";
        if (module is null)
            throw Missing(where);

        var classification = Classification(module.Classification, where);
        var examModule = module.Module is { Length: > 0 } name
            ? ParseName<ExamModule>(name, where, "module")
            : (ExamModule?)null;

        return new ParsedModuleCandidate(
            examModule,
            classification,
            Confidence(module.Confidence, where),
            [.. (module.Parts ?? []).Select((part, partIndex) => Part(part, where, partIndex, sources))],
            sources.Resolve(module.Provenance, where));
    }

    private static ParsedPartCandidate Part(
        ExamExtractionPartDto? part, string moduleWhere, int index, ExamExtractionSourceIndex sources)
    {
        var where = $"{moduleWhere} part {index + 1}";
        if (part is null)
            throw Missing(where);
        if (part.Id is not { Length: > 0 } id || string.IsNullOrWhiteSpace(id))
            throw Missing($"{where} identifier");
        if (part.Order is not { } order)
            throw Missing($"{where} order");

        return new ParsedPartCandidate(
            id,
            order,
            Trimmed(part.Title),
            part.Body,
            [.. (part.Questions ?? []).Select((question, questionIndex) =>
                Question(question, where, questionIndex, sources))],
            sources.Resolve(part.Provenance, where));
    }

    private static ParsedQuestionCandidate Question(
        ExamExtractionQuestionDto? question, string partWhere, int index, ExamExtractionSourceIndex sources)
    {
        var where = $"{partWhere} question {index + 1}";
        if (question is null)
            throw Missing(where);
        if (question.Id is not { Length: > 0 } id || string.IsNullOrWhiteSpace(id))
            throw Missing($"{where} identifier");
        if (question.Order is not { } order)
            throw Missing($"{where} order");

        var type = question.Type is { Length: > 0 } name
            ? ParseName<QuestionType>(name, where, "question type")
            : (QuestionType?)null;

        var options = Options(question.Options, where);
        var answerKey = AnswerKey(question.AnswerKey, options, type, where);

        return new ParsedQuestionCandidate(
            id,
            order,
            type,
            question.Prompt,
            options,
            answerKey,
            sources.Resolve(question.Provenance, where));
    }

    private static IReadOnlyList<ParsedQuestionOptionCandidate> Options(
        IReadOnlyList<ExamExtractionOptionDto>? options, string where)
    {
        if (options is null || options.Count == 0) return [];

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mapped = new List<ParsedQuestionOptionCandidate>(options.Count);

        foreach (var option in options)
        {
            if (option?.Key is not { Length: > 0 } key || string.IsNullOrWhiteSpace(key))
                throw Missing($"{where} option key");
            if (option.Text is not { Length: > 0 } text || string.IsNullOrWhiteSpace(text))
                throw Missing($"{where} option text");

            if (!keys.Add(key.Trim()))
            {
                throw new ExamExtractionRejectedException(
                    ExamExtractionRejection.DuplicateOptionKey,
                    $"{where} lists the option key '{key.Trim()}' more than once, so an answer naming "
                    + "it would be ambiguous.");
            }

            mapped.Add(new ParsedQuestionOptionCandidate(key.Trim(), text));
        }

        return mapped;
    }

    private static ParsedAnswerKeyCandidate? AnswerKey(
        ExamExtractionAnswerKeyDto? answerKey,
        IReadOnlyList<ParsedQuestionOptionCandidate> options,
        QuestionType? type,
        string where)
    {
        if (answerKey is null) return null;

        if (answerKey.Accepted is not { Count: > 0 } accepted)
            throw Missing($"{where} accepted answers");

        foreach (var value in accepted)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw Missing($"{where} accepted answer value");
        }

        if (options.Count > 0)
        {
            /*
             * <b>The fabricated-key catch.</b> When a question carries a bank,
             * its answer is a key from that bank. An accepted value that is not
             * one of the keys is either a solved answer the model worked out
             * (rule 3 of the prompt forbids it) or a key from a bank that was
             * captured wrongly — and both mark learners incorrectly while
             * looking entirely well-formed.
             */
            foreach (var value in accepted)
            {
                if (!options.Any(option =>
                    string.Equals(option.Key, value.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ExamExtractionRejectedException(
                        ExamExtractionRejection.AnswerKeyNotAnOption,
                        $"{where} accepts an answer that is not one of the option keys it lists.");
                }
            }
        }
        else if (type is { } resolved && KeyedTypes.Contains(resolved))
        {
            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.AnswerKeyWithoutOptions,
                $"{where} is a {resolved} question with an answer key but no options, so the key "
                + "cannot be checked against anything.");
        }

        return new ParsedAnswerKeyCandidate([.. accepted.Select(value => value.Trim())], answerKey.MatchingRule);
    }

    private static ParsedExamClassification Classification(string? value, string where)
    {
        if (value is not { Length: > 0 } name)
            throw Missing($"{where} classification");

        return ParseName<ParsedExamClassification>(name, where, "classification");
    }

    private static decimal? Confidence(decimal? value, string where)
    {
        if (value is not { } confidence) return null;

        if (confidence is < 0 or > 1)
        {
            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.ConfidenceRange,
                $"{where} reports a confidence outside 0 to 1.");
        }

        return confidence;
    }

    /// <summary>
    /// Parses an enum member <b>by name only</b>.
    ///
    /// <para>
    /// <b><see cref="Enum.TryParse{T}(string, out T)"/> alone would accept
    /// <c>"7"</c>.</b> It parses the underlying numeric representation as
    /// happily as the name, so a provider answering <c>"classification": "9"</c>
    /// would produce an out-of-range enum value that every later
    /// <c>switch</c> treats as a default. Checking against the declared names
    /// is what makes the closed set actually closed.
    /// </para>
    /// </summary>
    private static T ParseName<T>(string value, string where, string what) where T : struct, Enum
    {
        foreach (var name in Enum.GetNames<T>())
        {
            if (string.Equals(name, value, StringComparison.Ordinal)) return Enum.Parse<T>(name);
        }

        throw new ExamExtractionRejectedException(
            ExamExtractionRejection.InvalidEnum,
            $"{where} declares a {what} that is not part of the contract's closed set.");
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ExamExtractionRejectedException Missing(string what) =>
        new(ExamExtractionRejection.MissingField, $"{what} is absent from the extraction.");
}
