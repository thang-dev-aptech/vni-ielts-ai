using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Infrastructure.Content.Import;

/// <summary>
/// Raised by <see cref="UnconfiguredExamSourceParser"/>. Caught by
/// <see cref="ExamPackageImportPipeline"/> and turned into a blocking
/// finding rather than a 500 — refusing an AI-parsed upload is an expected,
/// well-typed outcome on a deployment that has not wired a parser, not a bug.
/// </summary>
public sealed class ExamSourceParsingUnavailableException(string message) : Exception(message);

/// <summary>
/// The null implementation of <see cref="IExamSourceParser"/> — a configured
/// seam with no live provider behind it, per CLAUDE.md's `G-11`, until AI
/// parsing of raw exam source documents is wired into the hosted API.
///
/// <b>Why this is the default rather than the real thing.</b> The operator
/// CLI (<c>Vni.Ielts.ExamImporter</c>) already proves a real
/// <see cref="ProviderNeutralExamSourceParser"/> backed by
/// <c>OpenAiStructuredExamClient</c> works — but it does so with a
/// repository-relative template and schema path, an operator picking
/// <c>--rights-cleared</c> and a key document by hand, and a ten-minute
/// timeout tuned for a person watching a terminal. Reproducing that safely
/// for an unattended HTTP caller is a larger piece of work than S6b's front
/// door (CLAUDE.md: "adapters are Phase 7 work") and is deliberately left for
/// a dedicated slice rather than wired in half-considered here.
///
/// <b>Only the AI-parsed route reaches this type.</b> A package that already
/// contains one ready <c>exam.json</c> — the structured route — never touches
/// a parser at all, so uploads of that shape work fully today with no AI
/// provider configured anywhere in this deployment.
/// </summary>
public sealed class UnconfiguredExamSourceParser : IExamSourceParser
{
    public Task<ParsedExamPackage> ParseAsync(ExtractedImportSource source, CancellationToken ct) =>
        throw new ExamSourceParsingUnavailableException(
            "AI-assisted parsing of raw exam source documents (docx/pdf/txt) is not wired into this "
            + "deployment. Upload a package that already contains a single ready exam.json instead, "
            + "or produce one with the operator CLI (backend/tools/Vni.Ielts.ExamImporter) and upload that.");
}
