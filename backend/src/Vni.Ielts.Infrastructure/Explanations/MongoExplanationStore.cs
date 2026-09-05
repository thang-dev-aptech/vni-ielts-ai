using MongoDB.Driver;
using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Infrastructure.Persistence;

namespace Vni.Ielts.Infrastructure.Explanations;

internal sealed class MongoPersonalizedExplanationStore(MongoContext context) : IPersonalizedExplanationStore
{
    private IMongoCollection<PersonalizedExplanationDocument> Jobs =>
        context.PersonalizedExplanations;

    public async Task<PersonalizedExplanationJob?> FindByOperationAsync(
        string operationId, CancellationToken ct)
    {
        var doc = await Jobs.Find(j => j.Id == operationId).FirstOrDefaultAsync(ct);
        return doc is null ? null : Map(doc);
    }

    public async Task<PersonalizedExplanationJob?> FindReadyAsync(
        ExamSessionId sessionId, string questionId, string answerHash, CancellationToken ct)
    {
        var doc = await Jobs.Find(j =>
                j.SessionId == sessionId.Value
                && j.QuestionId == questionId
                && j.AnswerHash == answerHash
                && j.State == ExplanationJobState.Ready.ToString())
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : Map(doc);
    }

    public async Task<bool> TryInsertAsync(PersonalizedExplanationJob job, CancellationToken ct)
    {
        try
        {
            await Jobs.InsertOneAsync(Map(job), cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<bool> UpdateAsync(PersonalizedExplanationJob job, CancellationToken ct)
    {
        var result = await Jobs.ReplaceOneAsync(
            j => j.Id == job.OperationId,
            Map(job),
            cancellationToken: ct);

        return result.MatchedCount > 0;
    }

    public async Task<IReadOnlyList<PersonalizedExplanationJob>> ListForSessionAsync(
        ExamSessionId sessionId, CancellationToken ct)
    {
        var docs = await Jobs.Find(j => j.SessionId == sessionId.Value).ToListAsync(ct);
        return docs.Select(Map).ToList();
    }

    private static PersonalizedExplanationJob Map(PersonalizedExplanationDocument d) => new(
        d.Id,
        new ExamSessionId(d.SessionId),
        new ExamVersionId(d.VersionId),
        d.QuestionId,
        d.AnswerHash,
        Enum.TryParse<ExplanationJobState>(d.State, ignoreCase: true, out var state)
            ? state
            : ExplanationJobState.Pending,
        d.Content is null
            ? null
            : new ValidatedExplanation(
                d.Content.CorrectAnswer,
                d.Content.ShortReason,
                d.Content.Evidence,
                d.Content.CommonMistake),
        d.Provider is null
            ? null
            : new ExplanationProviderMetadata(d.Provider, d.Model!, d.PromptVersion!, d.RequestId!),
        d.Attempts,
        d.Error,
        new DateTimeOffset(d.CreatedAt, TimeSpan.Zero),
        d.CompletedAt is { } done ? new DateTimeOffset(done, TimeSpan.Zero) : null);

    private static PersonalizedExplanationDocument Map(PersonalizedExplanationJob job) => new()
    {
        Id = job.OperationId,
        SessionId = job.SessionId.Value,
        VersionId = job.VersionId.Value,
        QuestionId = job.QuestionId,
        AnswerHash = job.AnswerHash,
        State = job.State.ToString(),
        Attempts = job.Attempts,
        Error = job.Error,
        CreatedAt = job.CreatedAt.UtcDateTime,
        CompletedAt = job.CompletedAt?.UtcDateTime,
        Provider = job.Metadata?.Provider,
        Model = job.Metadata?.Model,
        PromptVersion = job.Metadata?.PromptVersion,
        RequestId = job.Metadata?.RequestId,
        Content = job.Content is null
            ? null
            : new ValidatedExplanationDocument
            {
                CorrectAnswer = job.Content.CorrectAnswer,
                ShortReason = job.Content.ShortReason,
                Evidence = job.Content.Evidence.ToList(),
                CommonMistake = job.Content.CommonMistake,
            },
    };
}

internal sealed class MongoCanonicalExplanationCache(MongoContext context) : ICanonicalExplanationCache
{
    private IMongoCollection<CanonicalExplanationDocument> Entries =>
        context.CanonicalExplanations;

    public async Task<StoredCanonicalExplanation?> FindAsync(
        ExamVersionId versionId, string questionId, CancellationToken ct)
    {
        var id = $"{versionId.Value}:{questionId}";
        var doc = await Entries.Find(e => e.Id == id).FirstOrDefaultAsync(ct);
        if (doc?.Content is null || doc.Metadata is null) return null;

        return new StoredCanonicalExplanation(
            versionId,
            questionId,
            new ValidatedExplanation(
                doc.Content.CorrectAnswer,
                doc.Content.ShortReason,
                doc.Content.Evidence,
                doc.Content.CommonMistake),
            new ExplanationProviderMetadata(
                doc.Metadata.Provider,
                doc.Metadata.Model,
                doc.Metadata.PromptVersion,
                doc.Metadata.RequestId));
    }

    public async Task SaveAsync(StoredCanonicalExplanation entry, CancellationToken ct)
    {
        var id = $"{entry.VersionId.Value}:{entry.QuestionId}";
        var doc = new CanonicalExplanationDocument
        {
            Id = id,
            VersionId = entry.VersionId.Value,
            QuestionId = entry.QuestionId,
            Content = new ValidatedExplanationDocument
            {
                CorrectAnswer = entry.Explanation.CorrectAnswer,
                ShortReason = entry.Explanation.ShortReason,
                Evidence = entry.Explanation.Evidence.ToList(),
                CommonMistake = entry.Explanation.CommonMistake,
            },
            Metadata = new ExplanationMetadataDocument
            {
                Provider = entry.Metadata.Provider,
                Model = entry.Metadata.Model,
                PromptVersion = entry.Metadata.PromptVersion,
                RequestId = entry.Metadata.RequestId,
            },
        };

        await Entries.ReplaceOneAsync(
            e => e.Id == id,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }
}

internal sealed class CanonicalExplanationDocument
{
    [MongoDB.Bson.Serialization.Attributes.BsonId]
    public string Id { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("versionId")]
    public string VersionId { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("questionId")]
    public string QuestionId { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("content")]
    public ValidatedExplanationDocument? Content { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("metadata")]
    public ExplanationMetadataDocument? Metadata { get; set; }
}

internal sealed class PersonalizedExplanationDocument
{
    [MongoDB.Bson.Serialization.Attributes.BsonId]
    public string Id { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("versionId")]
    public string VersionId { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("questionId")]
    public string QuestionId { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("answerHash")]
    public string AnswerHash { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("state")]
    public string State { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("attempts")]
    public int Attempts { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("error")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public string? Error { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("completedAt")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public DateTime? CompletedAt { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("provider")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public string? Provider { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("model")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public string? Model { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("promptVersion")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public string? PromptVersion { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("requestId")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public string? RequestId { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("content")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public ValidatedExplanationDocument? Content { get; set; }
}

internal sealed class ValidatedExplanationDocument
{
    [MongoDB.Bson.Serialization.Attributes.BsonElement("correctAnswer")]
    public string CorrectAnswer { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("shortReason")]
    public string ShortReason { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("evidence")]
    public List<string> Evidence { get; set; } = [];

    [MongoDB.Bson.Serialization.Attributes.BsonElement("commonMistake")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public string? CommonMistake { get; set; }
}

internal sealed class ExplanationMetadataDocument
{
    [MongoDB.Bson.Serialization.Attributes.BsonElement("provider")]
    public string Provider { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("model")]
    public string Model { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("promptVersion")]
    public string PromptVersion { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("requestId")]
    public string RequestId { get; set; } = string.Empty;
}
