namespace Vni.Ielts.Domain.Identity;

/// <summary>
/// The permission keys shipped in the initial seed.
///
/// <b>This is not an enum, and authorisation checks are not restricted to it.</b>
/// Permissions are seeded data compared as strings, so a new one can be added
/// without a deployment — requirement C-13 states explicitly that the example
/// keys are not final. These constants exist only so the seeder and the tests
/// stop typoing them.
///
/// Three separations below are security decisions rather than taxonomy taste:
///
/// <list type="bullet">
/// <item><b><c>exam.publish</c> is separate from <c>exam.update</c></b> — the
/// person who imports content and the person who ships it to learners need not
/// be the same account. Direct mitigation of threat T20; merging them means one
/// compromised editor account can push content to every candidate.</item>
///
/// <item><b><c>learner-content.read</c> is separate from <c>evaluation.read</c></b>
/// — reading a band and its metadata is one thing; reading a learner's essay or
/// listening to their recording is personal-data processing under PDPL. Merged,
/// everyone debugging a score can read learner work.</item>
///
/// <item><b><c>evaluation.rerun</c> stands alone</b> — it is a permission that
/// spends real money on a provider call.</item>
/// </list>
///
/// → docs/ux/cms-spec.md § 1
/// </summary>
public static class PermissionKeys
{
    // exam.read/update/delete are ownership-scoped rather than global keys —
    // `Đ5`'s `<resource>.<action>[.<scope>]` convention. There is no bare
    // `exam.read` etc.: every caller either sees their own content or
    // everyone's, and a coarser key would let "can read" quietly mean "can
    // read anything." → docs/ux/cms-content-operations.md §4.1
    public const string ExamReadOwn = "exam.read.own";
    public const string ExamReadAny = "exam.read.any";
    public const string ExamCreate = "exam.create";
    public const string ExamUpdateOwn = "exam.update.own";
    public const string ExamUpdateAny = "exam.update.any";
    public const string ExamDeleteOwn = "exam.delete.own";
    public const string ExamDeleteAny = "exam.delete.any";

    /// <summary>Submit for review, and withdraw a submission — same permission, opposite direction.</summary>
    public const string ExamSubmit = "exam.submit";

    /// <summary>Approve, return, or unapprove a submission under review.</summary>
    public const string ExamReview = "exam.review";

    /// <summary>See a draft as a learner would, before it is publishable.</summary>
    public const string ExamPreview = "exam.preview";

    public const string ExamPublish = "exam.publish";
    public const string ExamUnpublish = "exam.unpublish";

    public const string PackageUpload = "package.upload";

    /// <summary>
    /// Shared inbox: list and open any package in the CMS review queue.
    /// Not ownership-scoped — holders see every package, not only their uploads.
    /// Confirming a package into Drafts requires <see cref="PackageConfirm"/>
    /// (or candidate confirm via <see cref="ExamReview"/>), not this key alone.
    /// </summary>
    public const string PackageRead = "package.read";

    /// <summary>
    /// Shared review: confirm a ReadyToImport ZIP package into Draft exam
    /// versions. <c>AuthorId</c> on those drafts is the confirmer, not the
    /// uploader — intentionally separate from <see cref="PackageUpload"/>.
    /// </summary>
    public const string PackageConfirm = "package.confirm";

    public const string PackageDelete = "package.delete";

    /// <summary>
    /// Register a content-source rights grant in the CMS.
    ///
    /// <b>Narrower than package/exam authority on purpose.</b> Deciding that
    /// third-party material may reach learners is a legal act, not an
    /// authoring one — only <c>admin</c> is seeded with it.
    /// </summary>
    public const string ContentRightsManage = "content-rights.manage";

    public const string MediaUpload = "media.upload";
    public const string MediaRead = "media.read";
    public const string MediaRetire = "media.retire";

    public const string EvaluationRead = "evaluation.read";
    public const string EvaluationRerun = "evaluation.rerun";
    public const string EvaluationOverride = "evaluation.override";

    public const string LearnerContentRead = "learner-content.read";

    public const string UserRead = "user.read";
    public const string UserUpdate = "user.update";
    public const string UserSuspend = "user.suspend";
    public const string UserDelete = "user.delete";
    public const string UserExport = "user.export";
    public const string TokenRead = "token.read";

    /// <summary>
    /// Set another account's password.
    ///
    /// <para>
    /// <b>Its own key, not part of <see cref="UserUpdate"/>, because it is the
    /// one administrative act that hands over an identity.</b> Whoever holds
    /// this can sign in as anybody — including another operator — and from that
    /// point the audit log records the wrong person. Splitting it out means a
    /// support role can edit a profile without also being able to become the
    /// person whose profile it is.
    /// </para>
    ///
    /// Kept from main (ADR-0018) while CMS roles fold to exam-author /
    /// academic-lead / admin.
    /// </summary>
    public const string UserResetPassword = "user.reset-password";

    public const string RoleRead = "role.read";
    public const string RoleAssign = "role.assign";
    public const string RoleManage = "role.manage";

    public const string ConfigRead = "config.read";
    public const string ConfigUpdate = "config.update";

    public const string AuditRead = "audit.read";

    /// <summary>
    /// The two libraries (<c>P-22</c>). Write and publish are split for the
    /// same reason as <c>exam.update</c> / <c>exam.publish</c>. Kept from main
    /// while Articles/Documents endpoints still exist on this branch.
    /// </summary>
    public const string DocumentWrite = "document.write";
    public const string DocumentPublish = "document.publish";
    public const string ArticleWrite = "article.write";
    public const string ArticlePublish = "article.publish";

    /// <summary>
    /// Every key, in the order the CMS's permission matrix renders its columns.
    ///
    /// <b>Derived here, not restated in the client.</b> The matrix needs a
    /// column for a permission no role holds yet, and a second list in
    /// TypeScript is a second thing to forget when a key is added — the column
    /// would simply not appear, and nobody would notice until someone tried to
    /// grant it.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        ExamReadOwn, ExamReadAny, ExamCreate, ExamUpdateOwn, ExamUpdateAny,
        ExamDeleteOwn, ExamDeleteAny, ExamSubmit, ExamReview, ExamPreview,
        ExamPublish, ExamUnpublish,
        PackageUpload, PackageRead, PackageConfirm, PackageDelete,
        ContentRightsManage,
        MediaUpload, MediaRead, MediaRetire,
        EvaluationRead, EvaluationRerun, EvaluationOverride,
        LearnerContentRead,
        UserRead, UserUpdate, UserSuspend, UserDelete, UserExport, TokenRead,
        UserResetPassword,
        RoleRead, RoleAssign, RoleManage,
        ConfigRead, ConfigUpdate,
        AuditRead,
        DocumentWrite, DocumentPublish, ArticleWrite, ArticlePublish,
    ];
}

/// <summary>
/// The seeded roles. <c>M-11</c> settled that the first release has no
/// class-management teacher role (<c>M-11a</c> stays out of scope), but a
/// content-authoring one is in scope as <see cref="ExamAuthor"/> (<c>M-11b</c>).
/// <c>ContentEditor</c> and <c>Support</c> folded into <see cref="Admin"/>
/// (<c>C-25</c>) — their permissions moved to <c>Admin</c>'s seed list, not
/// dropped, so un-folding either back into its own seeded role later is a
/// data change, not a redesign.
/// </summary>
public static class SystemRoles
{
    public const string Learner = "learner";
    public const string ExamAuthor = "exam-author";
    public const string AcademicLead = "academic-lead";
    public const string Admin = "admin";
}
