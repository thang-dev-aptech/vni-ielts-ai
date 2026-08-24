using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Vni.Ielts.Infrastructure.Persistence.Identity;

/// <summary>
/// Persistence models.
///
/// <b>These are the only types in the system allowed to carry
/// <c>[BsonId]</c> or any other driver attribute.</b> The domain entities they
/// map to carry none — that separation is CLAUDE.md rule 7, enforced by the
/// architecture tests, and it is what reduces the MongoDB to PostgreSQL
/// migration to rewriting this directory.
///
/// They are <c>internal</c> so nothing outside Infrastructure can accidentally
/// depend on their shape.
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class UserDocument
{
    /// <summary>
    /// A string, not an <c>ObjectId</c>. The domain generates its own
    /// identifiers, so letting Mongo assign one would create two ids for one
    /// entity — and <c>ObjectId</c> would not survive the move to Postgres.
    /// </summary>
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("emailVerified")]
    public bool EmailVerified { get; set; }

    [BsonElement("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Normalised to `+84…`. Absent until the learner adds one.</summary>
    [BsonElement("phone")]
    [BsonIgnoreIfNull]
    public string? Phone { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "Active";

    /// <summary>
    /// Stored as UTC. Mongo persists BSON dates as UTC milliseconds, and the
    /// domain works exclusively in <c>DateTimeOffset</c> — the mapper converts
    /// at the boundary so no offset is silently lost.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Role ids only, never embedded role documents. Cross-module references
    /// are by id — embedding a Role here would duplicate it across every user
    /// and make a permission change a fan-out write.
    /// </summary>
    [BsonElement("roleIds")]
    public List<string> RoleIds { get; set; } = [];
}

[BsonIgnoreExtraElements]
internal sealed class UserIdentityDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("provider")]
    public string Provider { get; set; } = string.Empty;

    [BsonElement("providerUserId")]
    public string ProviderUserId { get; set; } = string.Empty;

    /// <summary>Argon2id, encoded with its own parameters. Null for social identities.</summary>
    [BsonElement("passwordHash")]
    [BsonIgnoreIfNull]
    public string? PasswordHash { get; set; }

    [BsonElement("linkedAt")]
    public DateTime LinkedAt { get; set; }
}

[BsonIgnoreExtraElements]
internal sealed class RoleDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("isSystem")]
    public bool IsSystem { get; set; }

    [BsonElement("permissions")]
    public List<string> Permissions { get; set; } = [];
}

/// <summary>
/// A refresh token, stored hashed.
///
/// <b>Family and reuse detection.</b> Every rotation keeps the same
/// <c>FamilyId</c>. Redeeming a token marks it used; redeeming an already-used
/// token means two parties hold the same token, one of them stole it, and
/// there is no way to tell which — so the entire family is revoked. Rotation
/// without this leaves a stolen token quietly valid until expiry. → threat T3
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class RefreshTokenDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 of the token, never the token itself. A database dump must not
    /// hand out working sessions. Argon2id is unnecessary here — the token is
    /// 256 bits of entropy from a CSPRNG, not a guessable human password, so
    /// there is nothing for a slow hash to defend against.
    /// </summary>
    [BsonElement("tokenHash")]
    public string TokenHash { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("familyId")]
    public string FamilyId { get; set; } = string.Empty;

    [BsonElement("usedAt")]
    [BsonIgnoreIfNull]
    public DateTime? UsedAt { get; set; }

    [BsonElement("revokedAt")]
    [BsonIgnoreIfNull]
    public DateTime? RevokedAt { get; set; }

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The browser or app that asked for this token, verbatim.
    ///
    /// <para>
    /// Stored so the account owner can recognise their own sessions — "Chrome
    /// trên macOS" is only derivable from this string. It is parsed into a
    /// label when the list is read rather than at write time, so improving the
    /// parser does not require a migration.
    /// </para>
    ///
    /// <para>
    /// <b>No IP address sits beside it, deliberately.</b> An IP would not help
    /// anyone recognise a device — it changes with every network — and it is
    /// exactly the field that turns a session list into location history.
    /// → PDPL, <c>B-2</c>
    /// </para>
    /// </summary>
    [BsonElement("userAgent")]
    [BsonIgnoreIfNull]
    public string? UserAgent { get; set; }
}
