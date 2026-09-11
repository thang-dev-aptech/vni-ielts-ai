using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

internal static class ParsedCandidateMappers
{
    private static DateTime Utc(DateTimeOffset value) => value.UtcDateTime;

    private static DateTimeOffset Offset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static ParsedExamCandidateDocument ToDocument(this ParsedExamCandidate candidate) => new()
    {
        Id = MongoParsedExamCandidateRepository.KeyFor(candidate.PackageId, candidate.Id),
        CandidateId = candidate.Id,
        PackageId = candidate.PackageId,
        Title = candidate.Title,
        Classification = candidate.Classification.ToString(),
        Confidence = candidate.Confidence,
        Status = candidate.Status.ToString(),
        Version = candidate.Version,
        Sources = [.. candidate.Sources.Select(ToDocument)],
        Modules = [.. candidate.Modules.Select(ToDocument)],
        Corrections = [.. candidate.Corrections.Select(c => new CandidateCorrectionDocument
        {
            Id = c.Id,
            ReviewerId = c.ReviewerId.Value,
            Field = c.Field.ToString(),
            TargetId = c.TargetId,
            PreviousValue = c.PreviousValue,
            NewValue = c.NewValue,
            At = Utc(c.At),
        })],
        ConfirmedBy = candidate.ConfirmedBy?.Value,
        ConfirmedAt = candidate.ConfirmedAt is { } confirmedAt ? Utc(confirmedAt) : null,
        RejectedBy = candidate.RejectedBy?.Value,
        RejectedAt = candidate.RejectedAt is { } rejectedAt ? Utc(rejectedAt) : null,
        DraftExamVersionId = candidate.DraftExamVersionId,
    };

    public static ParsedExamCandidate ToDomain(this ParsedExamCandidateDocument document) =>
        ParsedExamCandidate.Rehydrate(
            document.CandidateId,
            document.PackageId,
            document.Title,
            Enum.Parse<ParsedExamClassification>(document.Classification),
            document.Confidence,
            [.. document.Modules.Select(ToDomain)],
            [.. document.Sources.Select(ToDomain)],
            Enum.Parse<ParsedCandidateStatus>(document.Status),
            document.Corrections.Select(c => new CandidateCorrection(
                c.Id,
                new UserId(c.ReviewerId),
                Enum.Parse<CandidateCorrectionField>(c.Field),
                c.TargetId,
                c.PreviousValue,
                c.NewValue,
                Offset(c.At))),
            document.ConfirmedBy is { } confirmedBy ? new UserId(confirmedBy) : null,
            document.ConfirmedAt is { } confirmedAt ? Offset(confirmedAt) : null,
            document.RejectedBy is { } rejectedBy ? new UserId(rejectedBy) : null,
            document.RejectedAt is { } rejectedAt ? Offset(rejectedAt) : null,
            document.Version,
            document.DraftExamVersionId);

    private static ParsedSourceDocument ToDocument(ParsedSourceProvenance source) => new()
    {
        FileName = source.FileName,
        Page = source.Page,
        Section = source.Section,
        Reference = source.Reference,
    };

    private static ParsedSourceProvenance ToDomain(ParsedSourceDocument source) =>
        new(source.FileName, source.Page, source.Section, source.Reference);

    private static ParsedModuleDocument ToDocument(ParsedModuleCandidate module) => new()
    {
        Module = module.Module?.ToString(),
        Classification = module.Classification.ToString(),
        Confidence = module.Confidence,
        Parts = [.. module.Parts.Select(ToDocument)],
        Provenance = ToDocument(module.Provenance),
    };

    private static ParsedModuleCandidate ToDomain(ParsedModuleDocument module) => new(
        module.Module is { } value ? Enum.Parse<ExamModule>(value) : null,
        Enum.Parse<ParsedExamClassification>(module.Classification),
        module.Confidence,
        [.. module.Parts.Select(ToDomain)],
        ToDomain(module.Provenance));

    private static ParsedPartDocument ToDocument(ParsedPartCandidate part) => new()
    {
        Id = part.Id,
        Order = part.Order,
        Title = part.Title,
        Body = part.Body,
        Questions = [.. part.Questions.Select(ToDocument)],
        Provenance = ToDocument(part.Provenance),
    };

    private static ParsedPartCandidate ToDomain(ParsedPartDocument part) => new(
        part.Id,
        part.Order,
        part.Title,
        part.Body,
        [.. part.Questions.Select(ToDomain)],
        ToDomain(part.Provenance));

    private static ParsedQuestionDocument ToDocument(ParsedQuestionCandidate question) => new()
    {
        Id = question.Id,
        Order = question.Order,
        Type = question.Type?.ToString(),
        Prompt = question.Prompt,
        Options = [.. question.Options.Select(o => new ParsedOptionDocument { Key = o.Key, Text = o.Text })],
        AnswerKey = question.AnswerKey is { } key
            ? new ParsedAnswerKeyDocument { Accepted = [.. key.Accepted], MatchingRule = key.MatchingRule }
            : null,
        Provenance = ToDocument(question.Provenance),
    };

    private static ParsedQuestionCandidate ToDomain(ParsedQuestionDocument question) => new(
        question.Id,
        question.Order,
        question.Type is { } type ? Enum.Parse<QuestionType>(type) : null,
        question.Prompt,
        [.. question.Options.Select(o => new ParsedQuestionOptionCandidate(o.Key, o.Text))],
        question.AnswerKey is { } key
            ? new ParsedAnswerKeyCandidate([.. key.Accepted], key.MatchingRule)
            : null,
        ToDomain(question.Provenance));
}
