namespace Vni.Ielts.Infrastructure.Assessment;

/// <summary>
/// Controls whether Writing AI marking is active and which provider serves it.
///
/// <b>Feature-flagged, not always-on.</b> Wiring an evaluator without an
/// explicit enable switch would turn marking on the moment a key appears in
/// environment configuration — before rubric provenance is settled, before a
/// CTIA filing, before anyone has reviewed a calibration set.
/// </summary>
public sealed class WritingMarkingOptions
{
    /// <summary>
    /// Whether the Writing evaluator port is wired. <b>Default false.</b>
    /// Typing true is the record that somebody decided marking may run.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Primary provider section: <c>OpenAi</c> or <c>Gemini</c>. No default.
    /// </summary>
    public string? PrimaryProvider { get; set; }

    /// <summary>
    /// Optional fallback when the primary fails with a transient error.
    /// Must differ from <see cref="PrimaryProvider"/> when set.
    /// </summary>
    public string? FallbackProvider { get; set; }

    /// <summary>
    /// Stable prompt identifier stamped on every evaluation for reproducibility.
    /// </summary>
    public string? PromptVersion { get; set; }

    /// <summary>
    /// Path to the rubric artifact JSON, relative to the repository root or
    /// absolute. When unset, the default fixture is used in Development.
    /// </summary>
    public string? RubricArtifactPath { get; set; }

    /// <summary>Expected SHA-256 of the artifact bytes. Unset skips verification.</summary>
    public string? RubricContentHash { get; set; }

    /// <summary>Maximum attempts <b>per provider</b> before moving to the fallback.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Per-call timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
