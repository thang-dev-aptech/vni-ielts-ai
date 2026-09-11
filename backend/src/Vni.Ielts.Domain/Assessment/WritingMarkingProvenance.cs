namespace Vni.Ielts.Domain.Assessment;

/// <summary>
/// What produced a Writing band, recorded so a later dispute can reproduce it.
///
/// A mismatch between the requested and served model names is flagged, never a
/// reason to drop the band. Whether to change provider on a mismatch is an
/// owner decision. → writing-marking-rubric-v2 §6, `W-Q2`
/// </summary>
public sealed record WritingMarkingProvenance(
    string PromptVersion,
    string ProviderSection,
    string ModelRequested,
    string ModelReported,
    bool ModelMismatch,
    string? RequestId);
