using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

[BsonIgnoreExtraElements]
internal sealed class PackageUploadIdempotencyDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("actorId")]
    public string ActorId { get; set; } = string.Empty;

    [BsonElement("idempotencyKey")]
    public string IdempotencyKey { get; set; } = string.Empty;

    [BsonElement("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [BsonElement("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [BsonElement("state")]
    public string State { get; set; } = "pending";

    [BsonElement("status")]
    [BsonIgnoreIfNull]
    public string? Status { get; set; }

    [BsonElement("statusCode")]
    [BsonIgnoreIfNull]
    public int? StatusCode { get; set; }

    [BsonElement("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [BsonElement("leaseExpiresAt")]
    public DateTimeOffset LeaseExpiresAt { get; set; }

    [BsonElement("claimToken")]
    [BsonIgnoreIfNull]
    public string? ClaimToken { get; set; }
}

internal sealed class MongoPackageUploadIdempotencyStore(MongoContext context) : IPackageUploadIdempotencyStore
{
    private static PackageUploadIdempotencyRecord ToRecord(PackageUploadIdempotencyDocument doc) =>
        new(
            doc.ActorId,
            doc.IdempotencyKey,
            doc.Sha256,
            doc.PackageId,
            doc.Status,
            doc.StatusCode,
            doc.CreatedAt,
            string.Equals(doc.State, "completed", StringComparison.OrdinalIgnoreCase)
                ? PackageUploadClaimState.Completed
                : PackageUploadClaimState.Pending,
            doc.LeaseExpiresAt,
            doc.ClaimToken);

    public async Task<PackageUploadIdempotencyRecord?> FindAsync(
        string actorId, string idempotencyKey, CancellationToken ct)
    {
        var docId = $"{actorId}:{idempotencyKey}";
        var doc = await context.PackageUploadIdempotency
            .Find(Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.Id, docId))
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : ToRecord(doc);
    }

    public async Task<PackageUploadClaimResult> ClaimOrGetAsync(
        string actorId,
        string idempotencyKey,
        string sha256,
        string candidatePackageId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken ct)
    {
        var docId = $"{actorId}:{idempotencyKey}";
        var claimToken = Guid.NewGuid().ToString("n");
        var newDoc = new PackageUploadIdempotencyDocument
        {
            Id = docId,
            ActorId = actorId,
            IdempotencyKey = idempotencyKey,
            Sha256 = sha256,
            PackageId = candidatePackageId,
            State = "pending",
            Status = null,
            StatusCode = null,
            CreatedAt = now,
            LeaseExpiresAt = now + leaseDuration,
            ClaimToken = claimToken,
        };

        try
        {
            await context.PackageUploadIdempotency.InsertOneAsync(newDoc, cancellationToken: ct);
            return new PackageUploadClaimResult(
                PackageUploadClaimStatus.NewClaim,
                ToRecord(newDoc));
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Concurrent caller or existing record
        }

        var existing = await context.PackageUploadIdempotency
            .Find(Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.Id, docId))
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            return new PackageUploadClaimResult(
                PackageUploadClaimStatus.PendingWait,
                ToRecord(newDoc));
        }

        if (!string.Equals(existing.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            return new PackageUploadClaimResult(
                PackageUploadClaimStatus.ConflictMismatch,
                ToRecord(existing));
        }

        if (string.Equals(existing.State, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageUploadClaimResult(
                PackageUploadClaimStatus.AlreadyCompleted,
                ToRecord(existing));
        }

        // Stale pending claim: attempt to recover lease
        if (existing.LeaseExpiresAt <= now)
        {
            var reclaimToken = Guid.NewGuid().ToString("n");
            var filter = Builders<PackageUploadIdempotencyDocument>.Filter.And(
                Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.Id, docId),
                Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.State, "pending"),
                Builders<PackageUploadIdempotencyDocument>.Filter.Lte(d => d.LeaseExpiresAt, now));

            var update = Builders<PackageUploadIdempotencyDocument>.Update
                .Set(d => d.LeaseExpiresAt, now + leaseDuration)
                .Set(d => d.Sha256, sha256)
                .Set(d => d.PackageId, candidatePackageId)
                .Set(d => d.ClaimToken, reclaimToken);

            var updated = await context.PackageUploadIdempotency.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<PackageUploadIdempotencyDocument> { ReturnDocument = ReturnDocument.After },
                ct);

            if (updated is not null)
            {
                return new PackageUploadClaimResult(
                    PackageUploadClaimStatus.NewClaim,
                    ToRecord(updated));
            }
        }

        return new PackageUploadClaimResult(
            PackageUploadClaimStatus.PendingWait,
            ToRecord(existing));
    }

    public async Task MarkCompletedAsync(
        string actorId,
        string idempotencyKey,
        string claimToken,
        string sha256,
        string packageId,
        string status,
        int statusCode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var docId = $"{actorId}:{idempotencyKey}";
        var filter = Builders<PackageUploadIdempotencyDocument>.Filter.And(
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.Id, docId),
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.ActorId, actorId),
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.IdempotencyKey, idempotencyKey),
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.ClaimToken, claimToken),
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.Sha256, sha256),
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.PackageId, packageId),
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.State, "pending"));

        var update = Builders<PackageUploadIdempotencyDocument>.Update
            .Set(d => d.State, "completed")
            .Set(d => d.Status, status)
            .Set(d => d.StatusCode, statusCode);

        await context.PackageUploadIdempotency.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task ReleaseClaimAsync(
        string actorId,
        string idempotencyKey,
        string claimToken,
        string sha256,
        string packageId,
        CancellationToken ct)
    {
        var docId = $"{actorId}:{idempotencyKey}";
        var filter = Builders<PackageUploadIdempotencyDocument>.Filter.And(
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.Id, docId),
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.ActorId, actorId),
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.IdempotencyKey, idempotencyKey),
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.ClaimToken, claimToken),
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.Sha256, sha256),
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.PackageId, packageId),
            Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.State, "pending"));

        await context.PackageUploadIdempotency.DeleteOneAsync(filter, ct);
    }

    public async Task SaveAsync(PackageUploadIdempotencyRecord record, CancellationToken ct)
    {
        var doc = new PackageUploadIdempotencyDocument
        {
            Id = $"{record.ActorId}:{record.IdempotencyKey}",
            ActorId = record.ActorId,
            IdempotencyKey = record.IdempotencyKey,
            Sha256 = record.Sha256,
            PackageId = record.PackageId,
            State = record.State == PackageUploadClaimState.Pending ? "pending" : "completed",
            Status = record.Status,
            StatusCode = record.StatusCode,
            CreatedAt = record.CreatedAt,
            LeaseExpiresAt = record.LeaseExpiresAt ?? record.CreatedAt.AddMinutes(5),
            ClaimToken = record.ClaimToken,
        };

        try
        {
            await context.PackageUploadIdempotency.InsertOneAsync(doc, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Already written concurrently - preserve initial record
        }
    }
}
