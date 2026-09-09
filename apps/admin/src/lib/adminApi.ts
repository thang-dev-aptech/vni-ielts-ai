import { ApiError, apiBase, authedFetch, request, TRANSPORT_ERROR, type ApiProblem } from '@vni/auth';
import type { ExamDocument } from './examDocument.js';
import type { MediaAsset } from './media.js';

/**
 * The CMS's API.
 *
 * <b>Every write here produces an audit entry on the server, in the same
 * request.</b> That is why they exist at all: an action nobody can trace is
 * one that should not be possible. → `cms-spec.md` ràng buộc 6
 */

export interface AdminModuleSummary {
  module: string;
  questionCount: number;
  durationSeconds: number;
}

export interface AdminExam {
  examVersionId: string;
  definitionId: string;
  versionNumber: number;
  title: string;
  variant: string;
  /**
   * `draft` · `inreview` · `approved` · `published` · `unpublished` — the five
   * `ExamVersionStatus` values, lower-cased, with no separators (`inreview`,
   * not `in-review`). A published version is immutable.
   */
  status: string;
  publishedAt: string | null;
  /** Present when the version was created by a known CMS author. */
  authorId?: string | null;
  createdByName?: string | null;
  reviewNotes?: AdminReviewNote[];
  assets?: AdminExamAsset[];
  modules: AdminModuleSummary[];
}

export interface AdminReviewNote {
  id: string;
  authorId: string;
  authorName: string;
  body: string;
  anchor: string | null;
  at: string;
}

export interface AdminExamAsset {
  ref: string;
  usedAt: string;
  kind: 'audio' | 'image' | 'file';
  fileName: string;
  sizeBytes: number;
  checksum: string | null;
  resolved: boolean;
  mediaId: string | null;
}


export interface AdminPackageEntry {
  proposedDefinitionId: string;
  title: string;
  module: string;
  questionCount: number;
}

export interface AdminPackageFinding {
  stage?: string;
  code?: string;
  pointer?: string;
  message?: string;
}

export interface AdminPackage {
  packageId: string;
  sourceKind: string;
  fileName: string;
  status:
    | 'uploaded'
    | 'scanning'
    | 'validating'
    | 'parsing'
    | 'needs-review'
    | 'ready-to-import'
    | 'imported'
    | 'rejected'
    | 'failed';
  uploadedByName: string;
  findings: AdminPackageFinding[];
  entries: AdminPackageEntry[];
  createdVersionIds: string[];
  createdAt: string;
  updatedAt: string;
}

export interface AdminAcceptedAnswer {
  single: string | null;
  all: string[] | null;
  pairLeft: string | null;
  pairRight: string | null;
}

export interface AdminExamPreviewOption {
  key: string;
  text: string;
}

export interface AdminExamPreviewQuestion {
  id: string;
  order: number;
  type: string;
  prompt: string | null;
  options: AdminExamPreviewOption[];
  answerKey: AdminAcceptedAnswer[] | null;
}

export interface AdminExamPreviewCueCard {
  topic: string;
  bullets: string[];
}

export interface AdminExamPreviewPart {
  id?: string;
  order?: number;
  title: string | null;
  body: string | null;
  transcript: string | null;
  cueCard: AdminExamPreviewCueCard | null;
  questions: AdminExamPreviewQuestion[];
}

export interface AdminExamPreviewSection {
  module: string;
  parts: AdminExamPreviewPart[];
}

export interface AdminExamPreview {
  examVersionId: string;
  title: string;
  sections: AdminExamPreviewSection[];
}

export const MODULE_LABEL: Record<string, string> = {
  reading: 'Đọc',
  listening: 'Nghe',
  writing: 'Viết',
  speaking: 'Nói',
};

/**
 * A row in the account list.
 *
 * <b>`phone` is the identity now and `email` is the optional extra.</b>
 * Registration collects a phone number, so most accounts have no address at
 * all — `email` is nullable, and there is no `emailVerified` beside it any
 * more because nothing sets it. Both fields are still nullable rather than
 * one-or-the-other: an account created before 08/09/2026 has an address and no
 * phone, and the list has to show both kinds of row.
 */
export interface AdminUser {
  userId: string;
  displayName: string;
  email: string | null;
  phone: string | null;
  status: string;
  createdAt: string;
  roleIds: string[];
}

export interface AdminRole {
  roleId: string;
  name: string;
  isSystem: boolean;
  permissions: string[];
}

export const listExams = (accessToken: string) =>
  request<{ exams: AdminExam[] }>('/api/v1/admin/exams', { accessToken });

export const createExam = (
  accessToken: string,
  body: { title: string; variant: 'academic' | 'general' },
) =>
  request<{
    examVersionId: string;
    definitionId: string;
    versionNumber: number;
    status: string;
    authorId?: string | null;
  }>('/api/v1/admin/exams', {
    method: 'POST',
    accessToken,
    body,
    idempotencyKey: key(),
  });

export const deleteExam = (accessToken: string, examVersionId: string) =>
  request<void>(`/api/v1/admin/exams/${examVersionId}`, {
    method: 'DELETE',
    accessToken,
    idempotencyKey: key(),
  });

export const getExam = (accessToken: string, examVersionId: string) =>
  request<AdminExam>(`/api/v1/admin/exams/${examVersionId}`, { accessToken });

export const getExamPreview = (accessToken: string, examVersionId: string) =>
  request<AdminExamPreview>(`/api/v1/admin/exams/${examVersionId}/preview`, { accessToken });

export interface ExamContentFinding {
  stage: string;
  code: string;
  pointer: string | null;
  message: string;
}

export interface ExamContentValidation {
  valid: boolean;
  findings: ExamContentFinding[];
  status?: string;
}

export const getExamContent = (accessToken: string, examVersionId: string) =>
  request<ExamDocument>(`/api/v1/admin/exams/${examVersionId}/content`, { accessToken });

export const saveExamContent = (accessToken: string, examVersionId: string, document: ExamDocument) =>
  request<ExamContentValidation>(`/api/v1/admin/exams/${examVersionId}/content`, {
    method: 'PUT',
    accessToken,
    body: document,
    idempotencyKey: key(),
  });

export const validateExamContent = (
  accessToken: string,
  examVersionId: string,
  document: ExamDocument,
) =>
  request<ExamContentValidation>(`/api/v1/admin/exams/${examVersionId}/validate`, {
    method: 'POST',
    accessToken,
    body: document,
    idempotencyKey: key(),
  });

export const listMedia = (accessToken: string) =>
  request<MediaAsset[]>('/api/v1/admin/media', { accessToken });

/**
 * Multipart upload — INT `request()` still JSON-stringifies bodies, so this
 * goes through `authedFetch` the same way `uploadImportPackage` does.
 */
export const uploadMedia = async (accessToken: string, file: File): Promise<MediaAsset> => {
  const form = new FormData();
  form.append('file', file, file.name);
  const response = await authedFetch(`${apiBase()}/api/v1/admin/media`, accessToken, {
    method: 'POST',
    headers: { 'Idempotency-Key': crypto.randomUUID() },
    body: form,
  });
  return parseJsonResponse<MediaAsset>(response);
};

export const retireMedia = (accessToken: string, mediaId: string) =>
  request<void>(`/api/v1/admin/media/${mediaId}/retire`, {
    method: 'POST',
    accessToken,
    idempotencyKey: crypto.randomUUID(),
  });

export const deleteMedia = (accessToken: string, mediaId: string) =>
  request<void>(`/api/v1/admin/media/${mediaId}`, {
    method: 'DELETE',
    accessToken,
    idempotencyKey: crypto.randomUUID(),
  });

export const listUsers = (
  accessToken: string,
  search: string,
  page: number,
  /**
   * Server-side filters. `hasEmail` replaces the feature branch's
   * `emailVerified` — main has no verification flag (ADR-0018).
   */
  filters: { role?: string; status?: string; hasEmail?: string } = {},
) => {
  const query = new URLSearchParams({ page: String(page) });
  if (search) query.set('search', search);
  if (filters.role) query.set('role', filters.role);
  if (filters.status) query.set('status', filters.status);
  if (filters.hasEmail) query.set('hasEmail', filters.hasEmail);

  return request<{ total: number; page: number; pageSize: number; users: AdminUser[] }>(
    `/api/v1/admin/users?${query.toString()}`,
    { accessToken },
  );
};

export const listRoles = (accessToken: string) =>
  request<{ permissions: string[]; roles: AdminRole[] }>('/api/v1/admin/roles', { accessToken });

export interface AdminRoleRef {
  roleId: string;
  name: string;
}

/**
 * One account, with the roles it holds and every role it could hold.
 *
 * Deliberately not an extension of {@link AdminUser}: the list row carries
 * `roleIds`, the detail carries named roles. A screen that assigns a role has
 * to show a name, and an id the operator cannot read is how the wrong role
 * gets granted.
 */
export interface AdminUserDetail {
  userId: string;
  displayName: string;
  email: string | null;
  phone: string | null;
  status: string;
  createdAt: string;
  roles: AdminRoleRef[];
  availableRoles: AdminRoleRef[];
}

export interface AuditEntry {
  id: string;
  at: string;
  actorEmail: string;
  action: string;
  targetType: string;
  targetId: string;
  targetLabel: string;
  detail: Record<string, string>;
}

export const getUser = (accessToken: string, userId: string) =>
  request<AdminUserDetail>(`/api/v1/admin/users/${userId}`, { accessToken });

export const listAudit = (
  accessToken: string,
  filter: { actor: string; action: string },
  page: number,
) => {
  const query = new URLSearchParams({ page: String(page) });
  if (filter.actor) query.set('actor', filter.actor);
  if (filter.action) query.set('action', filter.action);

  return request<{
    total: number;
    page: number;
    pageSize: number;
    actions: string[];
    entries: AuditEntry[];
  }>(`/api/v1/admin/audit?${query}`, { accessToken });
};

/**
 * A key per press, not per render.
 *
 * These are the operations where a double-click or a flaky connection must not
 * produce two of something — and the key is what tells the server a second
 * arrival is a retry rather than a second decision.
 */
const key = () => crypto.randomUUID();

export const publishExam = (accessToken: string, examVersionId: string) =>
  request<{ status: string }>(`/api/v1/admin/exams/${examVersionId}/publish`, {
    method: 'POST',
    accessToken,
    idempotencyKey: key(),
  });

export const unpublishExam = (accessToken: string, examVersionId: string) =>
  request<{ status: string }>(`/api/v1/admin/exams/${examVersionId}/unpublish`, {
    method: 'POST',
    accessToken,
    idempotencyKey: key(),
  });

// ── Review lifecycle, P-20 ────────────────────────────────────────────────
//
// The three endpoints AdminEndpoints.cs added this session, completing
// Draft → InReview → Approved → Published → Unpublished alongside the
// publish/unpublish pair above. `approveExam`'s 403 carries
// `code: "REVIEWER_IS_AUTHOR"` when the caller authored the version they are
// trying to sign off — `ExamVersion.Approve`'s one non-negotiable rule,
// enforced server-side regardless of what this client offers.

export const submitExamForReview = (accessToken: string, examVersionId: string) =>
  request<{ status: string }>(`/api/v1/admin/exams/${examVersionId}/submit-for-review`, {
    method: 'POST',
    accessToken,
    idempotencyKey: key(),
  });

/** Alias used by QuestionBuilderPage (feature named this `submitExam`). */
export const submitExam = submitExamForReview;

export const approveExam = (accessToken: string, examVersionId: string) =>
  request<{ status: string }>(`/api/v1/admin/exams/${examVersionId}/approve`, {
    method: 'POST',
    accessToken,
    idempotencyKey: key(),
  });

/** `P-20`'s "Trả về kèm lý do" — a blank reason is refused server-side with a 409. */
export const returnExamToDraft = (accessToken: string, examVersionId: string, reason: string) =>
  request<{ status: string }>(`/api/v1/admin/exams/${examVersionId}/return-to-draft`, {
    method: 'POST',
    accessToken,
    body: { reason },
    idempotencyKey: key(),
  });

export const setUserStatus = (accessToken: string, userId: string, suspend: boolean) =>
  request<{ status: string }>(
    `/api/v1/admin/users/${userId}/${suspend ? 'suspend' : 'reinstate'}`,
    { method: 'POST', accessToken, idempotencyKey: key() },
  );

export const setUserRole = (accessToken: string, userId: string, roleId: string, grant: boolean) =>
  request<{ roles: string[] }>(`/api/v1/admin/users/${userId}/roles`, {
    method: 'POST',
    accessToken,
    body: { roleId, grant },
    idempotencyKey: key(),
  });

/**
 * Sets another account's password, and ends every session it has.
 *
 * <b>204, no body — the new password is never read back.</b> The operator
 * typed it and has to hand it over themselves; echoing it would put it in a
 * response the browser caches, the audit detail, and any proxy log in between,
 * for no gain at all.
 *
 * The server refuses two cases whatever this client offers: a caller without
 * `user.reset-password` gets 403, and a caller targeting their own account
 * gets 409 — resetting your own password through the admin door would be a way
 * to change a credential without proving you hold the current one.
 * `UserDetailPage` hides the control in both cases; the refusal is the rule.
 */
export const resetUserPassword = (accessToken: string, userId: string, newPassword: string) =>
  request<void>(`/api/v1/admin/users/${userId}/password`, {
    method: 'POST',
    accessToken,
    body: { newPassword },
    idempotencyKey: key(),
  });

// ── User administration (I2/I3) ───────────────────────────────────────────
//
// Additive helpers for staff create/invite, bulk suspend, profile patch,
// activity tabs, and privacy seams. Do not replace the operator-set password
// path above — that remains the phone-era recovery control on main.

export const createStaff = (
  accessToken: string,
  body: { email: string; displayName: string; roles: string[] },
) =>
  request<{ userId: string; verificationEmailSent: boolean; passwordResetEmailSent: boolean }>(
    '/api/v1/admin/users',
    { method: 'POST', accessToken, body, idempotencyKey: key() },
  );

export const inviteStaff = (
  accessToken: string,
  body: { email: string; displayName: string; roles: string[] },
) =>
  request<{ invitationId: string; emailSent: boolean }>('/api/v1/admin/invitations', {
    method: 'POST',
    accessToken,
    body,
    idempotencyKey: key(),
  });

export const bulkSuspendUsers = (accessToken: string, userIds: string[], reason: string) =>
  request<{ batchId: string; items: { userId: string; outcome: string; detail: string | null }[] }>(
    '/api/v1/admin/users/bulk-suspend',
    { method: 'POST', accessToken, body: { userIds, reason }, idempotencyKey: key() },
  );

export const patchUser = (
  accessToken: string,
  userId: string,
  body: {
    displayName?: string;
    email?: string;
    updateEmail?: boolean;
    phone?: string | null;
    updatePhone?: boolean;
  },
) =>
  request<{
    userId: string;
    displayName: string;
    email: string | null;
    phone: string | null;
  }>(`/api/v1/admin/users/${userId}`, {
    method: 'PATCH',
    accessToken,
    body,
    idempotencyKey: key(),
  });

/**
 * Always 409 `POLICY_NOT_CONFIGURED` on main — email verification was removed
 * (ADR-0018). Kept so the CMS can surface the refusal honestly.
 */
export const resendUserVerification = (accessToken: string, userId: string) =>
  request<{ alreadyVerified: boolean; emailSent: boolean }>(
    `/api/v1/admin/users/${userId}/resend-verification`,
    { method: 'POST', accessToken, idempotencyKey: key() },
  );

/** Sends a password-reset mail and revokes refresh tokens (staff with email). */
export const forceUserPasswordReset = (accessToken: string, userId: string) =>
  request<{ emailSent: boolean }>(`/api/v1/admin/users/${userId}/force-password-reset`, {
    method: 'POST',
    accessToken,
    idempotencyKey: key(),
  });

/** Fail-closed: export retention is not configured; expect 409. */
export const requestUserExport = (accessToken: string, userId: string) =>
  request<unknown>(`/api/v1/admin/users/${userId}/export`, {
    method: 'POST',
    accessToken,
    idempotencyKey: key(),
  });

export const listUserActivity = (accessToken: string, userId: string, page: number) =>
  request<{ total: number; page: number; days: { day: string; kinds: string[] }[] }>(
    `/api/v1/admin/users/${userId}/activity?page=${page}`,
    { accessToken },
  );

export const listUserAudit = (accessToken: string, userId: string, page: number) =>
  request<{ total: number; page: number; entries: AuditEntry[] }>(
    `/api/v1/admin/users/${userId}/audit?page=${page}`,
    { accessToken },
  );

export const listUserExams = (accessToken: string, userId: string, page: number) =>
  request<{
    total: number;
    page: number;
    exams: { examVersionId: string; title: string; status: string }[];
  }>(`/api/v1/admin/users/${userId}/exams?page=${page}`, { accessToken });

export const listUserSittings = (accessToken: string, userId: string, page: number) =>
  request<{
    total: number;
    page: number;
    sittings: { sessionId: string; status: string; mode: string; startedAt: string }[];
  }>(`/api/v1/admin/users/${userId}/sittings?page=${page}`, { accessToken });

export const listUserResults = (accessToken: string, userId: string, page: number) =>
  request<{
    total: number;
    page: number;
    results: unknown[];
    note?: string;
  }>(`/api/v1/admin/users/${userId}/results?page=${page}`, { accessToken });

export const listUserTokens = (accessToken: string, userId: string, page: number) =>
  request<{ total: number; page: number; tokens: unknown[]; note?: string }>(
    `/api/v1/admin/users/${userId}/tokens?page=${page}`,
    { accessToken },
  );

export const listPrivacyRequests = (accessToken: string, userId: string) =>
  request<{
    requests: {
      requestId: string;
      type: string;
      status: string;
      createdAt: string;
      requesterId: string;
    }[];
  }>(`/api/v1/admin/users/${userId}/privacy-requests`, { accessToken });

export const createPrivacyRequest = (
  accessToken: string,
  userId: string,
  type: string,
  reason: string,
) =>
  request<{ requestId: string; status: string }>(
    `/api/v1/admin/users/${userId}/privacy-requests`,
    {
      method: 'POST',
      accessToken,
      body: { type, reason },
      idempotencyKey: key(),
    },
  );

export const approvePrivacyRequest = (accessToken: string, requestId: string) =>
  request<{ requestId: string; status: string }>(
    `/api/v1/admin/privacy-requests/${requestId}/approve`,
    {
      method: 'POST',
      accessToken,
      body: {},
      idempotencyKey: key(),
    },
  );

export const executePrivacyRequest = (accessToken: string, requestId: string) =>
  request<unknown>(`/api/v1/admin/privacy-requests/${requestId}/execute`, {
    method: 'POST',
    accessToken,
    body: {},
    idempotencyKey: key(),
  });

// ── Library: documents and articles, S5 ──────────────────────────────────
//
// Same four-state lifecycle for both — Draft → PendingReview → Published ⇄
// Unpublished, `docs/ux/cms-content-operations.md` §3.2. `submit` checks
// `*.write`; `return`/`publish`/`unpublish` check `*.publish` — read straight
// off `AdminLibraryEndpoints.cs`, which is also where "publish is allowed
// straight from Draft, not just from PendingReview" comes from: there is no
// separate Approved state for this content type.

/** A document as both the learner app and the CMS receive it. */
export interface AdminLibraryDocument {
  id: string;
  slug: string;
  title: string;
  description: string;
  skill: string;
  category: string;
  type: string;
  format: string;
  targetBand: string | null;
  topic: string | null;
  pageCount: number | null;
  size: string;
  fileUrl: string | null;
  isFeatured: boolean;
  isNew: boolean;
  isUpdated: boolean;
  isPopular: boolean;
  access: string;
  /** Always `null` in this slice — a reserved seam, not yet a feature. */
  relatedExamIds: string[] | null;
  /** `draft` · `pending` · `published` · `unpublished`. */
  status: string;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  createdBy: string;
}

/** What the CMS sends to create or replace a document. Status is never part of this — it moves only through a transition endpoint. */
export interface LibraryDocumentInput {
  slug: string;
  title: string;
  description: string;
  skill: string;
  category: string;
  type: string;
  format: string;
  targetBand: string | null;
  topic: string | null;
  pageCount: number | null;
  size: string;
  fileUrl: string | null;
  isFeatured: boolean;
  isNew: boolean;
  isUpdated: boolean;
  isPopular: boolean;
  access: string;
}

export const listDocuments = (accessToken: string) =>
  request<{ items: AdminLibraryDocument[] }>('/api/v1/admin/library/documents', { accessToken });

export const getDocument = (accessToken: string, id: string) =>
  request<AdminLibraryDocument>(`/api/v1/admin/library/documents/${id}`, { accessToken });

export const createDocument = (accessToken: string, input: LibraryDocumentInput) =>
  request<AdminLibraryDocument>('/api/v1/admin/library/documents', {
    method: 'POST',
    accessToken,
    body: input,
    idempotencyKey: key(),
  });

export const updateDocument = (accessToken: string, id: string, input: LibraryDocumentInput) =>
  request<AdminLibraryDocument>(`/api/v1/admin/library/documents/${id}`, {
    method: 'PUT',
    accessToken,
    body: input,
    idempotencyKey: key(),
  });

export const deleteDocument = (accessToken: string, id: string) =>
  request<void>(`/api/v1/admin/library/documents/${id}`, {
    method: 'DELETE',
    accessToken,
    idempotencyKey: key(),
  });

const documentTransition = (verb: 'submit' | 'return' | 'publish' | 'unpublish') =>
  (accessToken: string, id: string) =>
    request<AdminLibraryDocument>(`/api/v1/admin/library/documents/${id}/${verb}`, {
      method: 'POST',
      accessToken,
      idempotencyKey: key(),
    });

export const submitDocument = documentTransition('submit');
export const returnDocument = documentTransition('return');
export const publishDocument = documentTransition('publish');
export const unpublishDocument = documentTransition('unpublish');

/** An article in a listing — everything but the body. */
export interface AdminArticleSummary {
  id: string;
  slug: string;
  title: string;
  excerpt: string;
  category: string;
  readMinutes: number;
  author: string;
  relatedExamIds: string[] | null;
  status: string;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  createdBy: string;
}

/** The whole post — same fields as the summary plus `body`. */
export interface AdminArticle extends AdminArticleSummary {
  body: string[];
}

/** What the CMS sends to create or replace an article. */
export interface ArticleInput {
  slug: string;
  title: string;
  excerpt: string;
  category: string;
  readMinutes: number;
  author: string;
  body: string[];
}

export const listArticles = (accessToken: string) =>
  request<{ items: AdminArticleSummary[] }>('/api/v1/admin/library/articles', { accessToken });

export const getArticle = (accessToken: string, id: string) =>
  request<AdminArticle>(`/api/v1/admin/library/articles/${id}`, { accessToken });

export const createArticle = (accessToken: string, input: ArticleInput) =>
  request<AdminArticle>('/api/v1/admin/library/articles', {
    method: 'POST',
    accessToken,
    body: input,
    idempotencyKey: key(),
  });

export const updateArticle = (accessToken: string, id: string, input: ArticleInput) =>
  request<AdminArticle>(`/api/v1/admin/library/articles/${id}`, {
    method: 'PUT',
    accessToken,
    body: input,
    idempotencyKey: key(),
  });

export const deleteArticle = (accessToken: string, id: string) =>
  request<void>(`/api/v1/admin/library/articles/${id}`, {
    method: 'DELETE',
    accessToken,
    idempotencyKey: key(),
  });

const articleTransition = (verb: 'submit' | 'return' | 'publish' | 'unpublish') =>
  (accessToken: string, id: string) =>
    request<AdminArticle>(`/api/v1/admin/library/articles/${id}/${verb}`, {
      method: 'POST',
      accessToken,
      idempotencyKey: key(),
    });

export const submitArticle = articleTransition('submit');
export const returnArticle = articleTransition('return');
export const publishArticle = articleTransition('publish');
export const unpublishArticle = articleTransition('unpublish');


async function parseJsonResponse<T>(response: Response): Promise<T> {
  if (response.status === 204) return undefined as T;
  const bodyText = await response.text();
  let payload: unknown = null;
  if (bodyText) {
    try {
      payload = JSON.parse(bodyText);
    } catch {
      throw new ApiError({
        title: 'Unexpected response',
        status: response.status,
        detail: `Non-JSON response (HTTP ${response.status})`,
        code: TRANSPORT_ERROR,
      });
    }
  }
  if (!response.ok) {
    const problem = (payload ?? {}) as Partial<ApiProblem>;
    throw new ApiError({
      title: problem.title ?? 'Request failed',
      status: response.status,
      detail: problem.detail ?? `HTTP ${response.status}`,
      code: problem.code ?? 'UNKNOWN',
      ...(problem.errors !== undefined ? { errors: problem.errors } : {}),
    });
  }
  return payload as T;
}

export const deletePackage = (accessToken: string, packageId: string) =>
  request<void>(`/api/v1/admin/packages/${packageId}`, {
    method: 'DELETE',
    accessToken,
    idempotencyKey: key(),
  });

export const listPackages = (accessToken: string) =>
  request<AdminPackage[]>('/api/v1/admin/packages', { accessToken });

export const getPackage = (accessToken: string, packageId: string) =>
  request<AdminPackage>(`/api/v1/admin/packages/${packageId}`, { accessToken });

/** Upload to `/api/v1/admin/packages` (AI/raw package pipeline), not `/import/packages`. */
export const uploadPackage = async (
  accessToken: string,
  file: File,
): Promise<{ packageId: string }> => {
  const form = new FormData();
  form.append('package', file, file.name);
  const response = await authedFetch(`${apiBase()}/api/v1/admin/packages`, accessToken, {
    method: 'POST',
    headers: { 'Idempotency-Key': key() },
    body: form,
  });
  return parseJsonResponse<{ packageId: string }>(response);
};

export const confirmPackage = (accessToken: string, packageId: string) =>
  request<{ packageId: string; createdVersionIds: string[] }>(
    `/api/v1/admin/packages/${packageId}/confirm`,
    {
      method: 'POST',
      accessToken,
      idempotencyKey: key(),
    },
  );

export type ParsedCandidateStatus = 'pending-review' | 'confirmed' | 'rejected';

export type ParsedCandidateClassification =
  | 'reading'
  | 'listening'
  | 'writing'
  | 'speaking'
  | 'unclassified'
  | 'needs-review';

export interface ParsedCandidateProvenance {
  fileName: string;
  page: number | null;
  section: string | null;
  reference: string | null;
}

export interface ParsedCandidateOption {
  key: string;
  text: string;
}

export interface ParsedCandidateAnswerKey {
  accepted: string[];
  matchingRule: string | null;
}

export interface ParsedCandidateQuestion {
  id: string;
  order: number;
  type: string | null;
  prompt: string | null;
  options: ParsedCandidateOption[];
  answerKey: ParsedCandidateAnswerKey | null;
  provenance: ParsedCandidateProvenance;
}

export interface ParsedCandidatePart {
  id: string;
  order: number;
  title: string | null;
  body: string | null;
  provenance: ParsedCandidateProvenance;
  questions: ParsedCandidateQuestion[];
}

export interface ParsedCandidateModule {
  module: string | null;
  classification: ParsedCandidateClassification;
  confidence: number | null;
  provenance: ParsedCandidateProvenance;
  parts: ParsedCandidatePart[];
}

export interface ParsedCandidateCorrection {
  id: string;
  reviewerId: string;
  field: string;
  targetId: string;
  previousValue: string | null;
  newValue: string | null;
  at: string;
}

export interface ParsedCandidateSummary {
  candidateId: string;
  packageId: string;
  title: string | null;
  classification: ParsedCandidateClassification;
  confidence: number | null;
  status: ParsedCandidateStatus;
  version: number;
  draftExamVersionId?: string | null;
  moduleCount: number;
  questionCount: number;
  unresolvedCount: number;
  sources: ParsedCandidateProvenance[];
}

export interface ParsedCandidateDetail extends ParsedCandidateSummary {
  modules: ParsedCandidateModule[];
  corrections: ParsedCandidateCorrection[];
  confirmedBy: string | null;
  confirmedAt: string | null;
  rejectedBy: string | null;
  rejectedAt: string | null;
}

export interface CandidateSectionTimingPayload {
  durationSeconds: number;
  transferTimeSeconds?: number | null | undefined;
}

export interface CandidateSpeakingPartTimingPayload {
  part: number;
  prepSeconds: number;
  responseSeconds: number;
}

export interface CandidateTimingProfilePayload {
  sections: Record<string, CandidateSectionTimingPayload>;
  speakingParts?: CandidateSpeakingPartTimingPayload[] | undefined;
}

export interface CandidateBandBoundaryPayload {
  minRaw: number;
  band: number;
}

export interface CandidateCriterionWeightsPayload {
  task1: number;
  task2: number;
}

export interface CandidateScoringProfilePayload {
  rawToBand?: Record<string, CandidateBandBoundaryPayload[]> | undefined;
  scoringProfileRef?: string | undefined;
  criterionWeights?: CandidateCriterionWeightsPayload | undefined;
}

export interface CandidatePartCompletionPayload {
  partOrder: number;
  kind?: string | undefined;
  taskNumber?: number | undefined;
  partNumber?: number | undefined;
  audioAssetRef?: string | undefined;
  imageAssetRef?: string | undefined;
}

export interface CandidateCompletionPayload {
  variant: 'academic' | 'general';
  timingProfile: CandidateTimingProfilePayload;
  scoringProfile: CandidateScoringProfilePayload;
  partDetails?: CandidatePartCompletionPayload[] | undefined;
}

export interface CandidateDraftResponse {
  examVersionId: string;
  definitionId: string;
  versionNumber: number;
  status: string;
}

export const listPackageCandidates = (accessToken: string, packageId: string) =>
  request<ParsedCandidateSummary[]>(`/api/v1/admin/packages/${packageId}/candidates`, {
    accessToken,
  });

export const getPackageCandidate = (
  accessToken: string,
  packageId: string,
  candidateId: string,
) =>
  request<ParsedCandidateDetail>(
    `/api/v1/admin/packages/${packageId}/candidates/${candidateId}`,
    { accessToken },
  );

export const correctPackageCandidate = (
  accessToken: string,
  packageId: string,
  candidateId: string,
  body: {
    expectedVersion: number;
    title?: string | null;
    updateTitle?: boolean;
    classification?: ParsedCandidateClassification;
    modules?: ParsedCandidateModule[];
  },
) =>
  request<ParsedCandidateDetail>(
    `/api/v1/admin/packages/${packageId}/candidates/${candidateId}/correct`,
    {
      method: 'POST',
      accessToken,
      body,
      idempotencyKey: key(),
    },
  );

export const rejectPackageCandidate = (
  accessToken: string,
  packageId: string,
  candidateId: string,
  expectedVersion: number,
) =>
  request<ParsedCandidateDetail>(
    `/api/v1/admin/packages/${packageId}/candidates/${candidateId}/reject`,
    {
      method: 'POST',
      accessToken,
      body: { expectedVersion },
      idempotencyKey: key(),
    },
  );

export const confirmPackageCandidate = (
  accessToken: string,
  packageId: string,
  candidateId: string,
  expectedVersion: number,
) =>
  request<ParsedCandidateDetail>(
    `/api/v1/admin/packages/${packageId}/candidates/${candidateId}/confirm`,
    {
      method: 'POST',
      accessToken,
      body: { expectedVersion },
      idempotencyKey: key(),
    },
  );

export const createCandidateDraft = (
  accessToken: string,
  packageId: string,
  candidateId: string,
  body: CandidateCompletionPayload,
) =>
  request<CandidateDraftResponse>(
    `/api/v1/admin/packages/${packageId}/candidates/${candidateId}/draft`,
    {
      method: 'POST',
      accessToken,
      body,
      idempotencyKey: key(),
    },
  );

// ── Exam import, S6b ──────────────────────────────────────────────────────
//
// `POST /packages` is multipart, so it cannot go through `request()` — that
// helper always `JSON.stringify`s its body, the same reason the Speaking
// upload in the learner app bypasses it too (see `packages/auth/src/http.ts`'s
// own remarks on `authedFetch`). The other three are plain JSON but share this
// module's response parsing rather than `request()`'s, because a rejection
// from any of the four can carry a `findings` array the caller needs to
// render — `request()`'s `ApiError` has no field for it.

export interface ImportFinding {
  severity: string;
  code: string;
  path: string;
  message: string;
}

export interface ImportWarning {
  id: string;
  category: string;
  path: string;
  message: string;
  resolved: boolean;
  overrideReason: string | null;
}

export interface ImportDraft {
  draftId: string;
  definitionId: string;
  versionNumber: number;
  /** `structuredpackage` · `aiparsedsource` — `ExamImportRoute`, lower-cased. */
  route: string;
  approvalState: string;
  revision: number;
  reviewedBy: string | null;
  presentSkills: string[];
  findings: ImportFinding[];
  warnings: ImportWarning[];
  checklistConfirmed: string[];
  checklistComplete: boolean;
  /** When false, approval does not wait on the six-item specialist checklist. */
  checklistRequired: boolean;
  createdBy?: string | null;
  createdAt?: string | null;
  title?: string | null;
  examVersionId?: string | null;
  unresolvedWarningCount?: number;
}

/**
 * A rejection from the import surface, carrying the findings that explain it.
 *
 * <b>A subclass of `ApiError`, not a new shape.</b> `reasonOf()` and every
 * other `error instanceof ApiError` check elsewhere in the CMS still works
 * unchanged; `findings` is additive, read only by the one screen that needs
 * the per-item, JSON-Pointer-addressed list — an admin whose 200-question
 * package failed needs more than "invalid package".
 */
export class ImportApiError extends ApiError {
  constructor(problem: ApiProblem, readonly findings: ImportFinding[]) {
    super(problem);
    this.name = 'ImportApiError';
  }
}

async function parseImportResponse(response: Response): Promise<ImportDraft> {
  const text = await response.text();
  let payload: unknown = null;

  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      throw new ApiError({
        title: 'Unexpected response',
        status: response.status,
        detail: `Non-JSON response (HTTP ${response.status})`,
        code: TRANSPORT_ERROR,
      });
    }
  }

  if (!response.ok) {
    const problem = (payload ?? {}) as Partial<ApiProblem> & { findings?: ImportFinding[] };
    throw new ImportApiError(
      {
        title: problem.title ?? 'Request failed',
        status: response.status,
        detail: problem.detail ?? `HTTP ${response.status}`,
        code: problem.code ?? 'UNKNOWN',
      },
      problem.findings ?? [],
    );
  }

  return payload as ImportDraft;
}

/**
 * Uploads one exam package ZIP and returns the review draft, or throws
 * `ImportApiError` — a `422 PACKAGE_REJECTED` with the ZIP pipeline's own
 * findings when the package itself is refused (bomb, path traversal, wrong
 * layout — the S6a safety checks), including the well-typed
 * `AI_PARSER_UNAVAILABLE` finding for a raw-document package on a deployment
 * with no AI parser wired in.
 */
export const uploadImportPackage = async (
  accessToken: string,
  file: File,
  options: { definitionId?: string; versionNumber?: number; checklistRequired?: boolean } = {},
): Promise<ImportDraft> => {
  const form = new FormData();
  form.append('file', file);
  if (options.definitionId) form.append('definitionId', options.definitionId);
  if (options.versionNumber) form.append('versionNumber', String(options.versionNumber));
  form.append('checklistRequired', String(options.checklistRequired ?? true));

  const response = await authedFetch(`${apiBase()}/api/v1/admin/import/packages`, accessToken, {
    method: 'POST',
    headers: { 'Idempotency-Key': crypto.randomUUID() },
    body: form,
  });

  return parseImportResponse(response);
};

export const getImportDraft = (accessToken: string, draftId: string) =>
  request<ImportDraft>(`/api/v1/admin/import/packages/${draftId}`, { accessToken });

export const listImportDrafts = (accessToken: string) =>
  request<{ drafts: ImportDraft[] }>('/api/v1/admin/import/packages', { accessToken });

/** `P-19`'s "bỏ qua được nhưng bắt buộc ghi lý do" — a blank reason is refused with a 409. */
export const overrideImportWarning = async (
  accessToken: string,
  draftId: string,
  warningId: string,
  reason: string,
): Promise<ImportDraft> => {
  const response = await authedFetch(
    `${apiBase()}/api/v1/admin/import/packages/${draftId}/warnings/${warningId}/override`,
    accessToken,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify({ reason }),
    },
  );

  return parseImportResponse(response);
};

/**
 * Thay thế toàn bộ tập checklist đã xác nhận — không cộng dồn phía server.
 * Luôn gửi đủ tập hiện tại, không chỉ mục vừa đổi.
 */
export const setImportChecklist = async (
  accessToken: string,
  draftId: string,
  confirmed: string[],
): Promise<ImportDraft> => {
  const response = await authedFetch(
    `${apiBase()}/api/v1/admin/import/packages/${draftId}/checklist`,
    accessToken,
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Idempotency-Key': crypto.randomUUID(),
      },
      body: JSON.stringify({ confirmed }),
    },
  );

  return parseImportResponse(response);
};

export const approveImportDraft = async (
  accessToken: string,
  draftId: string,
): Promise<ImportDraft> => {
  const response = await authedFetch(
    `${apiBase()}/api/v1/admin/import/packages/${draftId}/approve`,
    accessToken,
    { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() } },
  );

  return parseImportResponse(response);
};

export type ContentEnvironmentWire = 'fixture' | 'internal-review' | 'learner-production';

export interface AdminContentSource {
  sourceId: string;
  title: string;
  owner: string | null;
  rootPath: string;
  allowedEnvironments: ContentEnvironmentWire[];
  expiresAt: string | null;
  licenceReference: string | null;
  reviewer: string | null;
  reviewedAt: string | null;
  mayReachLearners: boolean;
  fileCount: number;
  hashedFileCount: number;
  examDefinitionIds: string[];
  examVersionIds: string[];
}

export interface RegisterContentSourcePayload {
  sourceId: string;
  title: string;
  owner?: string | null;
  rootPath: string;
  allowedEnvironments: ContentEnvironmentWire[];
  expiresAt?: string | null;
  files?: Array<{ path: string; sha256?: string | null; sizeBytes?: number | null }>;
  proof?: { reference: string; reviewer: string; reviewedAt: string } | null;
}

export const listContentSources = (accessToken: string) =>
  request<{ note: string; sources: AdminContentSource[] }>('/api/v1/admin/content-sources', {
    accessToken,
  });

export const registerContentSource = (accessToken: string, body: RegisterContentSourcePayload) =>
  request<AdminContentSource>('/api/v1/admin/content-sources', {
    method: 'POST',
    accessToken,
    body,
    idempotencyKey: key(),
  });
