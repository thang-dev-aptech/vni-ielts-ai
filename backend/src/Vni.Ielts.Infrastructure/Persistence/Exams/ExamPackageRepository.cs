using MongoDB.Driver;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

internal sealed class MongoExamPackageRepository(MongoContext context) : IExamPackageRepository
{
    public async Task<ExamPackage?> FindAsync(string packageId, CancellationToken ct)
    {
        var document = await context.ExamPackages
            .Find(package => package.Id == packageId)
            .FirstOrDefaultAsync(ct);

        return document?.ToDomain();
    }

    public async Task SaveAsync(ExamPackage package, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);

        var document = package.ToDocument();
        await context.ExamPackages.ReplaceOneAsync(
            stored => stored.Id == document.Id,
            document,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }

    public async Task<IReadOnlyList<ExamPackage>> ListByStatusAsync(
        PackageImportStatus status, CancellationToken ct)
    {
        var documents = await context.ExamPackages
            .Find(package => package.Status == status.ToString())
            .ToListAsync(ct);

        return [.. documents.Select(d => d.ToDomain())];
    }

    public async Task<IReadOnlyList<ExamPackage>> ListAllAsync(CancellationToken ct)
    {
        var documents = await context.ExamPackages
            .Find(FilterDefinition<ExamPackageDocument>.Empty)
            .SortByDescending(package => package.CreatedAt)
            .ToListAsync(ct);

        return [.. documents.Select(d => d.ToDomain())];
    }

    public async Task<IReadOnlyList<ExamPackage>> ListUnpurgedTerminalAsync(CancellationToken ct)
    {
        var terminal = new[]
        {
            PackageImportStatus.Imported.ToString(),
            PackageImportStatus.Rejected.ToString(),
            PackageImportStatus.Failed.ToString(),
        };

        var filter = Builders<ExamPackageDocument>.Filter.And(
            Builders<ExamPackageDocument>.Filter.In(p => p.Status, terminal),
            Builders<ExamPackageDocument>.Filter.Eq(p => p.UploadPurged, false));

        var documents = await context.ExamPackages.Find(filter).ToListAsync(ct);
        return [.. documents.Select(d => d.ToDomain())];
    }

    public Task DeleteAsync(string packageId, CancellationToken ct) =>
        context.ExamPackages.DeleteOneAsync(package => package.Id == packageId, ct);
}
