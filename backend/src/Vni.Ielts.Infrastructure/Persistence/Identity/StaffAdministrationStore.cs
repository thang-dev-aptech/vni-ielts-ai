using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Infrastructure.Persistence.Identity;

[BsonIgnoreExtraElements]
internal sealed class StaffInvitationDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("roleIds")]
    public List<string> RoleIds { get; set; } = [];

    [BsonElement("invitedBy")]
    public string InvitedBy { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [BsonElement("tokenHash")]
    public string TokenHash { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = nameof(StaffInvitationStatus.Pending);
}

[BsonIgnoreExtraElements]
internal sealed class PrivacyRequestDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("subjectId")]
    public string SubjectId { get; set; } = string.Empty;

    [BsonElement("type")]
    public string Type { get; set; } = string.Empty;

    [BsonElement("reason")]
    public string Reason { get; set; } = string.Empty;

    [BsonElement("requesterId")]
    public string RequesterId { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = nameof(PrivacyRequestStatus.PendingReview);

    [BsonElement("approverId")]
    [BsonIgnoreIfNull]
    public string? ApproverId { get; set; }

    [BsonElement("approvedAt")]
    [BsonIgnoreIfNull]
    public DateTime? ApprovedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class PersonalDataExportDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("subjectId")]
    public string SubjectId { get; set; } = string.Empty;

    [BsonElement("requestedBy")]
    public string RequestedBy { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "pending";

    [BsonElement("objectKey")]
    [BsonIgnoreIfNull]
    public string? ObjectKey { get; set; }

    [BsonElement("expiresAt")]
    [BsonIgnoreIfNull]
    public DateTime? ExpiresAt { get; set; }

    [BsonElement("failureCode")]
    [BsonIgnoreIfNull]
    public string? FailureCode { get; set; }
}

internal sealed class MongoStaffInvitationRepository(MongoContext ctx) : IStaffInvitationRepository
{
    public async Task<StaffInvitation?> FindByIdAsync(string id, CancellationToken ct)
    {
        var doc = await ctx.StaffInvitations.Find(i => i.Id == id).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<StaffInvitation?> FindLiveByEmailAsync(Email email, DateTimeOffset now, CancellationToken ct)
    {
        var doc = await ctx.StaffInvitations.Find(i =>
                i.Email == email.Value
                && i.Status == nameof(StaffInvitationStatus.Pending)
                && i.ExpiresAt > now.UtcDateTime)
            .FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<StaffInvitation?> FindByTokenHashAsync(string tokenHash, CancellationToken ct)
    {
        var doc = await ctx.StaffInvitations.Find(i => i.TokenHash == tokenHash).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<(IReadOnlyList<StaffInvitation> Invitations, long Total)> ListPendingAsync(
        int skip, int take, DateTimeOffset now, CancellationToken ct)
    {
        var filter = Builders<StaffInvitationDocument>.Filter.And(
            Builders<StaffInvitationDocument>.Filter.Eq(i => i.Status, nameof(StaffInvitationStatus.Pending)),
            Builders<StaffInvitationDocument>.Filter.Gt(i => i.ExpiresAt, now.UtcDateTime));
        var total = await ctx.StaffInvitations.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs = await ctx.StaffInvitations
            .Find(filter)
            .SortByDescending(i => i.CreatedAt)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(ct);
        return ([.. docs.Select(d => d.ToDomain())], total);
    }

    public async Task AddAsync(StaffInvitation invitation, CancellationToken ct)
    {
        try
        {
            await ctx.StaffInvitations.InsertOneAsync(invitation.ToDocument(), cancellationToken: ct);
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new DuplicateLiveInvitationException(invitation.Email.Value, e);
        }
    }

    public Task SaveAsync(StaffInvitation invitation, CancellationToken ct) =>
        ctx.StaffInvitations.ReplaceOneAsync(i => i.Id == invitation.Id, invitation.ToDocument(), cancellationToken: ct);

    public Task ExpireStalePendingByEmailAsync(Email email, DateTimeOffset now, CancellationToken ct) =>
        ctx.StaffInvitations.UpdateManyAsync(
            Builders<StaffInvitationDocument>.Filter.And(
                Builders<StaffInvitationDocument>.Filter.Eq(i => i.Email, email.Value),
                Builders<StaffInvitationDocument>.Filter.Eq(i => i.Status, nameof(StaffInvitationStatus.Pending)),
                Builders<StaffInvitationDocument>.Filter.Lte(i => i.ExpiresAt, now.UtcDateTime)),
            Builders<StaffInvitationDocument>.Update.Set(i => i.Status, nameof(StaffInvitationStatus.Expired)),
            cancellationToken: ct);
}

internal sealed class MongoPrivacyRequestRepository(MongoContext ctx) : IPrivacyRequestRepository
{
    public async Task<PrivacyRequest?> FindByIdAsync(string id, CancellationToken ct)
    {
        var doc = await ctx.PrivacyRequests.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }

    public async Task<IReadOnlyList<PrivacyRequest>> ListForSubjectAsync(UserId subjectId, CancellationToken ct)
    {
        var docs = await ctx.PrivacyRequests
            .Find(r => r.SubjectId == subjectId.Value)
            .SortByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return [.. docs.Select(d => d.ToDomain())];
    }

    public Task AddAsync(PrivacyRequest request, CancellationToken ct) =>
        ctx.PrivacyRequests.InsertOneAsync(request.ToDocument(), cancellationToken: ct);

    public Task SaveAsync(PrivacyRequest request, CancellationToken ct) =>
        ctx.PrivacyRequests.ReplaceOneAsync(r => r.Id == request.Id, request.ToDocument(), cancellationToken: ct);
}

internal sealed class MongoPersonalDataExportStore(MongoContext ctx) : IPersonalDataExportStore
{
    public Task SaveAsync(PersonalDataExportJob job, CancellationToken ct) =>
        ctx.PersonalDataExports.ReplaceOneAsync(
            j => j.Id == job.Id,
            job.ToDocument(),
            new ReplaceOptions { IsUpsert = true },
            ct);

    public async Task<PersonalDataExportJob?> FindAsync(string id, CancellationToken ct)
    {
        var doc = await ctx.PersonalDataExports.Find(j => j.Id == id).FirstOrDefaultAsync(ct);
        return doc?.ToDomain();
    }
}

internal static class StaffAdministrationMappers
{
    public static StaffInvitationDocument ToDocument(this StaffInvitation invitation) => new()
    {
        Id = invitation.Id,
        Email = invitation.Email.Value,
        RoleIds = [.. invitation.RoleIds.Select(r => r.Value)],
        InvitedBy = invitation.InvitedBy.Value,
        CreatedAt = invitation.CreatedAt.UtcDateTime,
        ExpiresAt = invitation.ExpiresAt.UtcDateTime,
        TokenHash = invitation.TokenHash,
        Status = invitation.Status.ToString(),
    };

    public static StaffInvitation ToDomain(this StaffInvitationDocument doc) =>
        StaffInvitation.Rehydrate(
            doc.Id,
            Email.Create(doc.Email),
            [.. doc.RoleIds.Select(id => new RoleId(id))],
            new UserId(doc.InvitedBy),
            new DateTimeOffset(DateTime.SpecifyKind(doc.CreatedAt, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(doc.ExpiresAt, DateTimeKind.Utc)),
            doc.TokenHash,
            Enum.Parse<StaffInvitationStatus>(doc.Status));

    public static PrivacyRequestDocument ToDocument(this PrivacyRequest request) => new()
    {
        Id = request.Id,
        SubjectId = request.SubjectId.Value,
        Type = request.Type.ToString(),
        Reason = request.Reason,
        RequesterId = request.RequesterId.Value,
        CreatedAt = request.CreatedAt.UtcDateTime,
        Status = request.Status.ToString(),
        ApproverId = request.ApproverId?.Value,
        ApprovedAt = request.ApprovedAt?.UtcDateTime,
    };

    public static PrivacyRequest ToDomain(this PrivacyRequestDocument doc) =>
        PrivacyRequest.Rehydrate(
            doc.Id,
            new UserId(doc.SubjectId),
            Enum.Parse<PrivacyRequestType>(doc.Type),
            doc.Reason,
            new UserId(doc.RequesterId),
            new DateTimeOffset(DateTime.SpecifyKind(doc.CreatedAt, DateTimeKind.Utc)),
            Enum.Parse<PrivacyRequestStatus>(doc.Status),
            doc.ApproverId is null ? null : new UserId(doc.ApproverId),
            doc.ApprovedAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(doc.ApprovedAt.Value, DateTimeKind.Utc)));

    public static PersonalDataExportDocument ToDocument(this PersonalDataExportJob job) => new()
    {
        Id = job.Id,
        SubjectId = job.SubjectId.Value,
        RequestedBy = job.RequestedBy.Value,
        CreatedAt = job.CreatedAt.UtcDateTime,
        Status = job.Status,
        ObjectKey = job.ObjectKey,
        ExpiresAt = job.ExpiresAt?.UtcDateTime,
        FailureCode = job.FailureCode,
    };

    public static PersonalDataExportJob ToDomain(this PersonalDataExportDocument doc) =>
        new(
            doc.Id,
            new UserId(doc.SubjectId),
            new UserId(doc.RequestedBy),
            new DateTimeOffset(DateTime.SpecifyKind(doc.CreatedAt, DateTimeKind.Utc)),
            doc.Status,
            doc.ObjectKey,
            doc.ExpiresAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(doc.ExpiresAt.Value, DateTimeKind.Utc)),
            doc.FailureCode);
}
