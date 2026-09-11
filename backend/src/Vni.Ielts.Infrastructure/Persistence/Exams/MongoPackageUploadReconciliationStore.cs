using MongoDB.Driver;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

public sealed class MongoPackageUploadReconciliationStore(MongoContext context)
    : IPackageUploadReconciliationStore
{
    public async Task RecordFailureAsync(
        string uploadRef,
        string packageId,
        string actorId,
        string reason,
        DateTimeOffset createdAt,
        CancellationToken ct)
    {
        var doc = new PackageUploadReconciliationDocument
        {
            Id = Guid.NewGuid().ToString("n"),
            UploadRef = uploadRef,
            PackageId = packageId,
            ActorId = actorId,
            Reason = reason,
            CreatedAt = createdAt,
        };

        await context.PackageUploadReconciliation.InsertOneAsync(doc, cancellationToken: ct);
    }
}
