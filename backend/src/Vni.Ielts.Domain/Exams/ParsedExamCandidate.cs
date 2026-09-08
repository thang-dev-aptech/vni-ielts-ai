using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Exams;

public enum ParsedExamClassification
{
    Reading,
    Listening,
    Writing,
    Speaking,
    Unclassified,
    NeedsReview,
}

public enum ParsedCandidateStatus
{
    PendingReview,
    Confirmed,
    Rejected,
}

public enum PackageImportStatus
{
    Uploaded,
    Scanning,
    Validating,
    Parsing,
    NeedsReview,
    ReadyToImport,
    Imported,
    Rejected,
    Failed,
}

public enum CandidateCorrectionField
{
    Classification,
    Title,
    Module,
    Part,
    Question,
    Options,
    AnswerKey,
}

public sealed record ParsedSourceProvenance(
    string FileName,
    int? Page,
    string? Section,
    string? Reference);

public sealed record ParsedQuestionOptionCandidate(string Key, string Text);

public sealed record ParsedAnswerKeyCandidate(
    IReadOnlyList<string> Accepted,
    string? MatchingRule);

public sealed record ParsedQuestionCandidate(
    string Id,
    int Order,
    QuestionType? Type,
    string? Prompt,
    IReadOnlyList<ParsedQuestionOptionCandidate> Options,
    ParsedAnswerKeyCandidate? AnswerKey,
    ParsedSourceProvenance Provenance);

public sealed record ParsedPartCandidate(
    string Id,
    int Order,
    string? Title,
    string? Body,
    IReadOnlyList<ParsedQuestionCandidate> Questions,
    ParsedSourceProvenance Provenance);

public sealed record ParsedModuleCandidate(
    ExamModule? Module,
    ParsedExamClassification Classification,
    decimal? Confidence,
    IReadOnlyList<ParsedPartCandidate> Parts,
    ParsedSourceProvenance Provenance);

public sealed record CandidateCorrection(
    string Id,
    UserId ReviewerId,
    CandidateCorrectionField Field,
    string TargetId,
    string? PreviousValue,
    string? NewValue,
    DateTimeOffset At);

/// <summary>
/// Untrusted, reviewable output from PDF/content parsing. It is not an ExamVersion
/// and cannot be published or sat until a staff member explicitly confirms it.
/// </summary>
public sealed class ParsedExamCandidate
{
    private readonly List<CandidateCorrection> _corrections;

    private ParsedExamCandidate(
        string id,
        string packageId,
        string? title,
        ParsedExamClassification classification,
        decimal? confidence,
        IReadOnlyList<ParsedModuleCandidate> modules,
        IReadOnlyList<ParsedSourceProvenance> sources,
        ParsedCandidateStatus status,
        IEnumerable<CandidateCorrection> corrections,
        UserId? confirmedBy,
        DateTimeOffset? confirmedAt,
        UserId? rejectedBy,
        DateTimeOffset? rejectedAt,
        int version,
        string? draftExamVersionId = null)
    {
        Id = id;
        PackageId = packageId;
        Title = title;
        Classification = classification;
        Confidence = confidence;
        Modules = modules;
        Sources = sources;
        Status = status;
        Validate(id, packageId, title, classification, confidence, modules, sources, status, corrections,
            confirmedBy, confirmedAt, rejectedBy, rejectedAt);
        _corrections = [.. corrections];
        ConfirmedBy = confirmedBy;
        ConfirmedAt = confirmedAt;
        RejectedBy = rejectedBy;
        RejectedAt = rejectedAt;
        DraftExamVersionId = draftExamVersionId;
        if (version < 0) throw new ArgumentOutOfRangeException(nameof(version));
        Version = version;
    }

    public string Id { get; }
    public string PackageId { get; }
    public string? Title { get; private set; }
    public ParsedExamClassification Classification { get; private set; }
    public decimal? Confidence { get; private set; }
    public IReadOnlyList<ParsedModuleCandidate> Modules { get; private set; }
    public IReadOnlyList<ParsedSourceProvenance> Sources { get; }
    public ParsedCandidateStatus Status { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<CandidateCorrection> Corrections => _corrections;
    public UserId? ConfirmedBy { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public UserId? RejectedBy { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public string? DraftExamVersionId { get; private set; }

    public static ParsedExamCandidate Create(
        string id,
        string packageId,
        string? title,
        ParsedExamClassification classification,
        decimal? confidence,
        IReadOnlyList<ParsedModuleCandidate> modules,
        IReadOnlyList<ParsedSourceProvenance> sources) =>
        new(id, packageId, title, classification, confidence, modules, sources,
            ParsedCandidateStatus.PendingReview, [], null, null, null, null, 0, null);

    public static ParsedExamCandidate Rehydrate(
        string id,
        string packageId,
        string? title,
        ParsedExamClassification classification,
        decimal? confidence,
        IReadOnlyList<ParsedModuleCandidate> modules,
        IReadOnlyList<ParsedSourceProvenance> sources,
        ParsedCandidateStatus status,
        IEnumerable<CandidateCorrection> corrections,
        UserId? confirmedBy,
        DateTimeOffset? confirmedAt,
        UserId? rejectedBy,
        DateTimeOffset? rejectedAt,
        int version,
        string? draftExamVersionId = null) =>
        new(id, packageId, title, classification, confidence, modules, sources,
            status, corrections, confirmedBy, confirmedAt, rejectedBy, rejectedAt, version, draftExamVersionId);

    public void MarkDraftCreated(string draftExamVersionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftExamVersionId);
        if (Status != ParsedCandidateStatus.Confirmed)
            throw new InvalidOperationException("Only confirmed candidates can be marked as drafted.");
        DraftExamVersionId = draftExamVersionId;
        Version++;
    }

    public void CorrectClassification(
        UserId reviewerId,
        ParsedExamClassification classification,
        DateTimeOffset at)
    {
        RequirePendingReview();
        AddCorrection(reviewerId, CandidateCorrectionField.Classification, Id,
            Classification.ToString(), classification.ToString(), at);
        Classification = classification;
        Confidence = null;
        Version++;
    }

    public void CorrectTitle(UserId reviewerId, string? title, DateTimeOffset at)
    {
        RequirePendingReview();
        AddCorrection(reviewerId, CandidateCorrectionField.Title, Id, Title, title, at);
        Title = title;
        Version++;
    }

    public void ReplaceModules(
        UserId reviewerId,
        IReadOnlyList<ParsedModuleCandidate> modules,
        DateTimeOffset at)
    {
        RequirePendingReview();
        AddCorrection(
            reviewerId,
            CandidateCorrectionField.Module,
            Id,
            Modules.Count.ToString(),
            modules.Count.ToString(),
            at);
        Modules = modules;
        Version++;
    }

    public void Correct(
        UserId reviewerId,
        string? newTitle,
        bool updateTitle,
        ParsedExamClassification? newClassification,
        IReadOnlyList<ParsedModuleCandidate>? newModules,
        DateTimeOffset at)
    {
        RequirePendingReview();
        var changed = false;
        if (updateTitle && newTitle != Title)
        {
            AddCorrection(reviewerId, CandidateCorrectionField.Title, Id, Title, newTitle, at);
            Title = newTitle;
            changed = true;
        }
        if (newClassification.HasValue && newClassification.Value != Classification)
        {
            AddCorrection(reviewerId, CandidateCorrectionField.Classification, Id,
                Classification.ToString(), newClassification.Value.ToString(), at);
            Classification = newClassification.Value;
            Confidence = null;
            changed = true;
        }
        if (newModules is not null)
        {
            AddCorrection(
                reviewerId,
                CandidateCorrectionField.Module,
                Id,
                Modules.Count.ToString(),
                newModules.Count.ToString(),
                at);
            Modules = newModules;
            changed = true;
        }
        if (changed)
        {
            Version++;
        }
    }

    public void Confirm(UserId reviewerId, DateTimeOffset at)
    {
        if (Status == ParsedCandidateStatus.Confirmed) return;
        RequirePendingReview();
        if (Classification is ParsedExamClassification.Unclassified or ParsedExamClassification.NeedsReview)
            throw new InvalidOperationException("A candidate must have a resolved classification before confirmation.");
        if (Modules.Count == 0 || Modules.Any(m => m.Parts.Count == 0))
            throw new InvalidOperationException("A candidate must contain at least one module with a part.");
        Status = ParsedCandidateStatus.Confirmed;
        Version++;
        ConfirmedBy = reviewerId;
        ConfirmedAt = at;
    }

    public void Reject(UserId reviewerId, DateTimeOffset at)
    {
        RequirePendingReview();
        Status = ParsedCandidateStatus.Rejected;
        Version++;
        RejectedBy = reviewerId;
        RejectedAt = at;
    }

    private static void Validate(
        string id, string packageId, string? title, ParsedExamClassification classification,
        decimal? confidence, IReadOnlyList<ParsedModuleCandidate> modules,
        IReadOnlyList<ParsedSourceProvenance> sources, ParsedCandidateStatus status,
        IEnumerable<CandidateCorrection> corrections, UserId? confirmedBy, DateTimeOffset? confirmedAt,
        UserId? rejectedBy, DateTimeOffset? rejectedAt)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Candidate and package identifiers are required.");
        if (confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(corrections);
        if (status == ParsedCandidateStatus.Confirmed && (confirmedBy is null || confirmedAt is null))
            throw new ArgumentException("Confirmed candidates require confirmation metadata.");
        if (status == ParsedCandidateStatus.Rejected && (rejectedBy is null || rejectedAt is null))
            throw new ArgumentException("Rejected candidates require rejection metadata.");
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.FileName) || source.Page is <= 0)
                throw new ArgumentException("Source file names are required and page numbers must be positive.");
        }
        foreach (var module in modules)
        {
            if (module.Confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(modules));
            if (module.Module is null && module.Classification is not (ParsedExamClassification.Unclassified or ParsedExamClassification.NeedsReview))
                throw new ArgumentException("Resolved module classifications require a module.");
            if (module.Module is not null && module.Classification is not ParsedExamClassification.NeedsReview && module.Classification != module.Module switch
                { ExamModule.Reading => ParsedExamClassification.Reading, ExamModule.Listening => ParsedExamClassification.Listening,
                  ExamModule.Writing => ParsedExamClassification.Writing, ExamModule.Speaking => ParsedExamClassification.Speaking,
                  _ => ParsedExamClassification.NeedsReview })
                throw new ArgumentException("Module and classification must agree.");
            ValidateParts(module.Parts);
        }
    }

    private static void ValidateParts(IReadOnlyList<ParsedPartCandidate> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var orders = new HashSet<int>();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part.Id) || part.Order <= 0 || !orders.Add(part.Order))
                throw new ArgumentException("Part identifiers and unique positive ordering are required.");
            ValidateQuestions(part.Questions);
        }
    }

    private static void ValidateQuestions(IReadOnlyList<ParsedQuestionCandidate> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var orders = new HashSet<int>();
        foreach (var question in questions)
            if (string.IsNullOrWhiteSpace(question.Id) || question.Order <= 0 || !orders.Add(question.Order))
                throw new ArgumentException("Question identifiers and unique positive ordering are required.");
    }

    private void AddCorrection(UserId reviewerId, CandidateCorrectionField field, string targetId,
        string? previous, string? next, DateTimeOffset at) =>
        _corrections.Add(new CandidateCorrection(Guid.NewGuid().ToString("n"), reviewerId, field, targetId, previous, next, at));

    private void RequirePendingReview()
    {
        if (Status != ParsedCandidateStatus.PendingReview)
            throw new InvalidOperationException($"Candidate cannot be changed while it is {Status}.");
    }
}

public sealed record PackageFinding(string Stage, string Code, string? Pointer, string Message);
