import { ApiError, apiBase, authedFetch, request, TRANSPORT_ERROR, type ApiProblem } from '@vni/auth';

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
  modules: AdminModuleSummary[];
}

export interface AdminUser {
  userId: string;
  displayName: string;
  email: string;
  emailVerified: boolean;
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

export const listUsers = (accessToken: string, search: string, page: number) =>
  request<{ total: number; page: number; pageSize: number; users: AdminUser[] }>(
    `/api/v1/admin/users?page=${page}${search ? `&search=${encodeURIComponent(search)}` : ''}`,
    { accessToken },
  );

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
  email: string;
  emailVerified: boolean;
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
  options: { definitionId?: string; versionNumber?: number } = {},
): Promise<ImportDraft> => {
  const form = new FormData();
  form.append('file', file);
  if (options.definitionId) form.append('definitionId', options.definitionId);
  if (options.versionNumber) form.append('versionNumber', String(options.versionNumber));

  const response = await authedFetch(`${apiBase()}/api/v1/admin/import/packages`, accessToken, {
    method: 'POST',
    headers: { 'Idempotency-Key': crypto.randomUUID() },
    body: form,
  });

  return parseImportResponse(response);
};

export const getImportDraft = (accessToken: string, draftId: string) =>
  request<ImportDraft>(`/api/v1/admin/import/packages/${draftId}`, { accessToken });

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
