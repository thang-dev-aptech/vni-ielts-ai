using MongoDB.Bson.Serialization.Attributes;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

/// <summary>
/// One provider extraction run, as stored.
///
/// <para>
/// <b>Every field is an identifier, a version, a count, a timestamp or a
/// hash.</b> There is no field for source text, for a prompt body, for an
/// answer key or for a credential — not "we do not write one", but no column to
/// write it into. A run record is read by operators and support, so the safe
/// version has to be the only version.
/// </para>
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class ExamExtractionRunDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("packageId")]
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Absent for a refused run: no candidate was created.</summary>
    [BsonElement("candidateId")]
    [BsonIgnoreIfNull]
    public string? CandidateId { get; set; }

    [BsonElement("provider")]
    public string Provider { get; set; } = string.Empty;

    [BsonElement("model")]
    public string Model { get; set; } = string.Empty;

    [BsonElement("promptVersion")]
    public string PromptVersion { get; set; } = string.Empty;

    [BsonElement("schemaId")]
    public string SchemaId { get; set; } = string.Empty;

    [BsonElement("contractVersion")]
    public string ContractVersion { get; set; } = string.Empty;

    [BsonElement("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [BsonElement("inputHash")]
    public string InputHash { get; set; } = string.Empty;

    [BsonElement("sourceCount")]
    public int SourceCount { get; set; }

    [BsonElement("inputTokens")]
    public long InputTokens { get; set; }

    [BsonElement("outputTokens")]
    public long OutputTokens { get; set; }

    [BsonElement("requestedAt")]
    public DateTime RequestedAt { get; set; }

    [BsonElement("completedAt")]
    public DateTime CompletedAt { get; set; }

    /// <summary>The stable rejection code, or absent when the run was accepted.</summary>
    [BsonElement("failureCode")]
    [BsonIgnoreIfNull]
    public string? FailureCode { get; set; }
}

internal static class ExamExtractionRunMappers
{
    /// <summary>
    /// <b>Package and request together are the key.</b> A repeat of the same
    /// provider request — a worker restarted mid-flight, a queue redelivery —
    /// then replaces its own record instead of writing a second one that looks
    /// like a second call.
    /// </summary>
    public static string KeyFor(string packageId, string requestId) => $"{packageId}:{requestId}";

    public static ExamExtractionRunDocument ToDocument(this ExamExtractionRunMetadata run) => new()
    {
        Id = KeyFor(run.PackageId, run.RequestId),
        PackageId = run.PackageId,
        CandidateId = run.CandidateId,
        Provider = run.Provider,
        Model = run.Model,
        PromptVersion = run.PromptVersion,
        SchemaId = run.SchemaId,
        ContractVersion = run.ContractVersion,
        RequestId = run.RequestId,
        InputHash = run.InputHash,
        SourceCount = run.SourceCount,
        InputTokens = run.InputTokens,
        OutputTokens = run.OutputTokens,
        RequestedAt = run.RequestedAt.UtcDateTime,
        CompletedAt = run.CompletedAt.UtcDateTime,
        FailureCode = run.FailureCode,
    };

    public static ExamExtractionRunMetadata ToDomain(this ExamExtractionRunDocument document) => new(
        document.PackageId,
        document.CandidateId,
        document.Provider,
        document.Model,
        document.PromptVersion,
        document.SchemaId,
        document.ContractVersion,
        document.RequestId,
        document.InputHash,
        document.SourceCount,
        document.InputTokens,
        document.OutputTokens,
        new DateTimeOffset(DateTime.SpecifyKind(document.RequestedAt, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(document.CompletedAt, DateTimeKind.Utc)),
        document.FailureCode);
}
