namespace Vni.Ielts.Application.Identity;

/// <summary>
/// Technical safety bounds for CMS user operations. These are not business
/// quotas; they exist so a single request cannot walk the whole user collection.
/// </summary>
public sealed class AdminUserOperationOptions
{
    public const string SectionName = "Admin";

    /// <summary>Maximum distinct user ids accepted by one bulk-suspend request.</summary>
    public int BulkUserOperationMax { get; set; } = 50;

    /// <summary>How long a staff invitation stays redeemable. Technical default, not a product SLA.</summary>
    public int InvitationLifetimeHours { get; set; } = 72;
}

/// <summary>
/// PDPL seams. Null values mean the corresponding write is refused rather
/// than inventing a retention or erasure policy. → G-11, M-55
/// </summary>
public sealed class PrivacyOptions
{
    public const string SectionName = "Privacy";

    /// <summary>
    /// Hours an assembled personal-data export may stay in object storage.
    /// Null: request and download return <c>POLICY_NOT_CONFIGURED</c>.
    /// </summary>
    public int? ExportRetentionHours { get; set; }

    /// <summary>
    /// Configured erasure mode once the owner answers M-55.
    /// Null: execute returns <c>POLICY_NOT_CONFIGURED</c> and does not delete.
    /// </summary>
    public string? ErasureMode { get; set; }
}
