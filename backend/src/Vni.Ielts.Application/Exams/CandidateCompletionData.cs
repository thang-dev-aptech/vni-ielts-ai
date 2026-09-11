using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Exams;

public sealed record CandidateSectionTiming(
    int DurationSeconds,
    int? TransferTimeSeconds = null);

public sealed record CandidateSpeakingPartTiming(
    int Part,
    int PrepSeconds,
    int ResponseSeconds);

public sealed record CandidateTimingProfile(
    IReadOnlyDictionary<ExamModule, CandidateSectionTiming> Sections,
    IReadOnlyList<CandidateSpeakingPartTiming>? SpeakingParts = null);

public sealed record CandidateBandBoundary(int MinRaw, decimal Band);

public sealed record CandidateCriterionWeights(decimal Task1, decimal Task2);

public sealed record CandidateScoringProfile(
    IReadOnlyDictionary<ExamModule, IReadOnlyList<CandidateBandBoundary>>? RawToBand = null,
    string? ScoringProfileRef = null,
    CandidateCriterionWeights? CriterionWeights = null);

public sealed record CandidatePartCompletion(
    int PartOrder,
    string? Kind = null,
    int? TaskNumber = null,
    int? PartNumber = null,
    string? AudioAssetRef = null,
    string? ImageAssetRef = null);

public sealed record CandidateCompletionData(
    ExamVariant Variant,
    CandidateTimingProfile TimingProfile,
    CandidateScoringProfile ScoringProfile,
    IReadOnlyList<CandidatePartCompletion>? PartDetails = null);
