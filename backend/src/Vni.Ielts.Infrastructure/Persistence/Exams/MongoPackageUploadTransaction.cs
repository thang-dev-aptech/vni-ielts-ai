using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

internal sealed class MongoPackageUploadTransaction(MongoContext context) : IPackageUploadTransaction
{
    public async Task CommitUploadAsync(
        ExamPackage package,
        string? actorId,
        string? idempotencyKey,
        string? claimToken,
        int statusCode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);

        using var session = await context.Database.Client.StartSessionAsync(cancellationToken: ct);
        session.StartTransaction();
        try
        {
            // Do not replace an existing package row unconditionally during retry.
            // InsertOneAsync fails with duplicate key if the row already exists.
            await context.ExamPackages.InsertOneAsync(session, package.ToDocument(), cancellationToken: ct);

            if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
                !string.IsNullOrWhiteSpace(claimToken) &&
                !string.IsNullOrWhiteSpace(actorId))
            {
                var docId = $"{actorId}:{idempotencyKey}";
                var filter = Builders<PackageUploadIdempotencyDocument>.Filter.And(
                    Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.Id, docId),
                    Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.ActorId, actorId),
                    Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.IdempotencyKey, idempotencyKey),
                    Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.ClaimToken, claimToken),
                    Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.Sha256, package.Sha256),
                    Builders<PackageUploadIdempotencyDocument>.Filter.Eq(d => d.PackageId, package.Id));

                var update = Builders<PackageUploadIdempotencyDocument>.Update
                    .Set(d => d.State, "completed")
                    .Set(d => d.Status, package.Status.ToString().ToLowerInvariant())
                    .Set(d => d.StatusCode, statusCode);

                var result = await context.PackageUploadIdempotency.UpdateOneAsync(session, filter, update, cancellationToken: ct);
                if (result.MatchedCount == 0)
                {
                    await session.AbortTransactionAsync(ct);
                    throw new InvalidOperationException("Idempotency claim was lost or reclaimed concurrently.");
                }
            }

            await session.CommitTransactionAsync(ct);
        }
        catch
        {
            if (session.IsInTransaction)
                await session.AbortTransactionAsync(ct);
            throw;
        }
    }
}
