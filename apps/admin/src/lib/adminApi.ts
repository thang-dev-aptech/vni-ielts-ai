import {
  ApiError,
  apiBase,
  authedFetch,
  request,
  TRANSPORT_ERROR,
  type ApiProblem,
} from '@vni/auth';

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
  cursor?: string,
) => {
  const query = new URLSearchParams();
  if (filter.actor) query.set('actor', filter.actor);
  if (filter.action) query.set('action', filter.action);
  if (cursor) query.set('cursor', cursor);

  return request<{
    actions: string[];
    entries: AuditEntry[];
    nextCursor?: string;
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

const documentTransition =
  (verb: 'submit' | 'return' | 'publish' | 'unpublish') => (accessToken: string, id: string) =>
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

const articleTransition =
  (verb: 'submit' | 'return' | 'publish' | 'unpublish') => (accessToken: string, id: string) =>
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

export interface ImportDraftOption {
  key: string;
  text: string;
}

/** A fraction of the group's image content box — 0..1, top-left origin. */
export interface ImportDraftPosition {
  key: string;
  x: number;
  y: number;
}

/**
 * One shared group the draft carries that is eligible for hotspot placement —
 * a matching/labelling group with an image. The server already filters
 * `draft.groups` to exactly this set (`ExamVersion.ToGroupViews`); a group
 * with no image or the wrong question type is never in this list.
 */
export interface ImportDraftGroup {
  id: string;
  title: string | null;
  instruction: string | null;
  imageKey: string | null;
  text: string | null;
  eachLetterOnce: boolean;
  options: ImportDraftOption[];
  positions: ImportDraftPosition[] | null;
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
  groups: ImportDraftGroup[];
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
  constructor(
    problem: ApiProblem,
    readonly findings: ImportFinding[],
  ) {
    super(problem);
    this.name = 'ImportApiError';
  }
}

async function parseImportResponse<T>(response: Response): Promise<T> {
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

  return payload as T;
}

// ── Import job polling, task 9 of the 2026-09-11 out-of-band import slice ──
//
// A Cambridge parse costs real money and takes minutes, so the upload no
// longer runs inline: `POST /packages` stores the archive, enqueues a job,
// and returns immediately. `Stage`/`State` are read straight off
// `AdminImportEndpoints.ToView` on the server — both stay PascalCase
// (`"Extracting"`, `"Running"`, …) because that endpoint calls `.ToString()`
// on the C# enum rather than lower-casing it the way `route`/`approvalState`
// are on `ImportDraftView`. Getting that casing wrong here silently breaks
// every stage/state comparison — checked against the actual response shape
// in `backend/src/Vni.Ielts.Api/Endpoints/AdminImportEndpoints.cs`, not
// guessed.

/** The seven steps `ImportWorker` reports, in order. */
export type ImportJobStage =
  | 'Extracting'
  | 'Parsing'
  | 'Transcribing'
  | 'Keying'
  | 'Checking'
  | 'Explaining'
  | 'Done';

/** Mirrors `Vni.Ielts.Application.Importing.ImportJobState`. */
export type ImportJobState = 'Pending' | 'Running' | 'Retryable' | 'Failed' | 'Completed';

/** What `POST /packages` answers with now that the work happens elsewhere — `202`, no draft yet. */
export interface ImportAcceptedView {
  operationId: string;
  definitionId: string;
  versionNumber: number;
  stage: ImportJobStage;
  state: ImportJobState;
}

/** `GET /import/jobs/{operationId}` — how far one enqueued import got, and why it stopped if it did. */
export interface ImportJobView {
  operationId: string;
  definitionId: string;
  versionNumber: number;
  stage: ImportJobStage;
  state: ImportJobState;
  attempts: number;
  maxAttempts: number;
  draftId: string | null;
  lastError: string | null;
  createdAt: string;
  nextAttemptAt: string | null;
  completedAt: string | null;
}

/**
 * Uploads one exam package ZIP and returns the accepted job, or throws
 * `ImportApiError` — a `422 PACKAGE_REJECTED` with the ZIP pipeline's own
 * findings when the package itself is refused (bomb, path traversal, wrong
 * layout — the S6a safety checks), including the well-typed
 * `AI_PARSER_UNAVAILABLE` finding for a raw-document package on a deployment
 * with no AI parser wired in. Those checks still happen inline, before
 * anything is persisted (CLAUDE.md rule 3) — only the parse itself moved out
 * of the request.
 *
 * <b>No draft comes back from this call any more.</b> The caller polls
 * `getImportJob` with the returned `operationId` until it reaches a terminal
 * state, then loads the draft by the `draftId` that job carries.
 */
export const uploadImportPackage = async (
  accessToken: string,
  file: File,
  options: { definitionId?: string; versionNumber?: number } = {},
): Promise<ImportAcceptedView> => {
  const form = new FormData();
  form.append('file', file);
  if (options.definitionId) form.append('definitionId', options.definitionId);
  if (options.versionNumber) form.append('versionNumber', String(options.versionNumber));

  const response = await authedFetch(`${apiBase()}/api/v1/admin/import/packages`, accessToken, {
    method: 'POST',
    headers: { 'Idempotency-Key': crypto.randomUUID() },
    body: form,
  });

  return parseImportResponse<ImportAcceptedView>(response);
};

/** Polled while `state` is `Pending` / `Running` / `Retryable`; `Completed` or `Failed` are terminal. */
export const getImportJob = (accessToken: string, operationId: string) =>
  request<ImportJobView>(`/api/v1/admin/import/jobs/${encodeURIComponent(operationId)}`, {
    accessToken,
  });

export const getImportDraft = (accessToken: string, draftId: string) =>
  request<ImportDraft>(`/api/v1/admin/import/packages/${draftId}`, { accessToken });

/**
 * Downloads the empty package skeleton — the exact folder names
 * `ExamPackageArchiveInspector` accepts, so an operator never has to guess
 * one. Guessing wrong sends an answer key to the AI model, so this is a
 * safeguard, not a convenience.
 *
 * Not `request()`: the body is a ZIP, not JSON, so this returns the raw
 * `Blob` for the caller to save.
 */
export const downloadImportTemplate = async (accessToken: string): Promise<Blob> => {
  const response = await authedFetch(`${apiBase()}/api/v1/admin/import/template`, accessToken, {
    method: 'GET',
  });

  if (!response.ok) {
    const text = await response.text();
    let payload: unknown = null;
    if (text) {
      try {
        payload = JSON.parse(text);
      } catch {
        // Not JSON — the generic transport-error problem below covers it.
      }
    }
    const problem = (payload ?? {}) as Partial<ApiProblem>;
    throw new ApiError({
      title: problem.title ?? 'Request failed',
      status: response.status,
      detail: problem.detail ?? `HTTP ${response.status}`,
      code: problem.code ?? TRANSPORT_ERROR,
    });
  }

  return response.blob();
};

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

  return parseImportResponse<ImportDraft>(response);
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

  return parseImportResponse<ImportDraft>(response);
};

/**
 * Persists the full set of hotspot positions for one shared group — a
 * replace, not a patch: every call sends the complete current pin set for
 * that group, mirroring `SetGroupPositionsAsync`'s own "write the identical
 * array into every occurrence" contract. Like every other content edit here,
 * this resets `approvalState`/`checklistConfirmed` on the server, which is
 * why the caller must replace its whole draft with the response rather than
 * patching just the one group in place.
 */
export const setGroupPositions = async (
  accessToken: string,
  draftId: string,
  groupId: string,
  positions: ImportDraftPosition[],
): Promise<ImportDraft> => {
  const response = await authedFetch(
    `${apiBase()}/api/v1/admin/import/packages/${draftId}/groups/${groupId}/positions`,
    accessToken,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify({ positions }),
    },
  );

  return parseImportResponse<ImportDraft>(response);
};

/**
 * `ImportDraft.checklistConfirmed`'s own values — `ImportReviewCategory`
 * members, lower-case with no separators, exactly as the server sends them
 * on the draft view. Matched case-insensitively server-side, but these are
 * the canonical wire spellings.
 */
export type ImportChecklistCategory =
  | 'questions'
  | 'options'
  | 'wordlimits'
  | 'acceptedvariants'
  | 'transcriptandevidence'
  | 'assetmapping';

/**
 * Replaces the full set of confirmed checklist categories — a replace, not a
 * patch, same contract as `setGroupPositions`. Approval refuses with
 * `IMPORT_CHECKLIST_INCOMPLETE` until all six are confirmed.
 */
export const setImportChecklist = async (
  accessToken: string,
  draftId: string,
  confirmed: ImportChecklistCategory[],
): Promise<ImportDraft> => {
  const response = await authedFetch(
    `${apiBase()}/api/v1/admin/import/packages/${draftId}/checklist`,
    accessToken,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify({ confirmed }),
    },
  );

  return parseImportResponse<ImportDraft>(response);
};

/**
 * Fetches one privately staged draft asset (authorized) and hands back a
 * local `blob:` URL — same reasoning as `fetchMediaObjectUrl`: an `<img>`
 * cannot present a bearer token, and before approval this asset exists only
 * in private staging, never at the learner-facing asset route.
 *
 * `reference` is the raw path a group's `imageKey` already carries (e.g.
 * `assets/map.jpg`) — passed through unencoded, since the server's route is a
 * catch-all (`{**reference}`) that expects the path's own slashes.
 */
export const fetchImportDraftAssetObjectUrl = async (
  accessToken: string,
  draftId: string,
  reference: string,
): Promise<string> => {
  const response = await authedFetch(
    `${apiBase()}/api/v1/admin/import/packages/${draftId}/assets/${reference}`,
    accessToken,
    {},
  );
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as Partial<ApiProblem> | null;
    throw new ApiError({
      title: problem?.title ?? 'Không tải được ảnh',
      status: response.status,
      detail: problem?.detail ?? `HTTP ${response.status}`,
      code: problem?.code ?? 'UNKNOWN',
    });
  }
  const blob = await response.blob();
  return URL.createObjectURL(blob);
};

// -- CMS media library --------------------------------------------------------

/**
 * One row of the media library � the server's view, field for field the shape
 * `lib/media.ts` has carried since the screen existed.
 */
/**
 * One version reference from an exam that uses this media asset.
 */
export interface AdminMediaVersionReference {
  versionId: string;
  title: string;
  state: string;
}

/**
 * One row of the media library · the server's view, field for field the shape
 * `lib/media.ts` has carried since the screen existed.
 */
export interface AdminMediaAsset {
  mediaId: string;
  /** `audio` · `image` · `file` · derived by the server from the file's own magic bytes. */
  kind: string;
  fileName: string;
  contentType: string;
  bytes: number;
  durationMs: number | null;
  checksum: string;
  uploadedByName: string;
  uploadedAt: string;
  retired: boolean;
  referencedBy: AdminMediaVersionReference[];
}

export const listMedia = (accessToken: string) =>
  request<{ media: AdminMediaAsset[] }>('/api/v1/admin/media', { accessToken });

export const retireMedia = (accessToken: string, mediaId: string) =>
  request<AdminMediaAsset>(`/api/v1/admin/media/${mediaId}/retire`, {
    method: 'POST',
    accessToken,
    idempotencyKey: key(),
  });

export const deleteMedia = (accessToken: string, mediaId: string) =>
  request<void>(`/api/v1/admin/media/${mediaId}`, {
    method: 'DELETE',
    accessToken,
    idempotencyKey: key(),
  });

/**
 * Upload one file. The server sniffs the type from the file's own bytes and
 * answers with the stored asset; the client's pre-flight verdict is advice to
 * the operator, never the record of truth.
 */
export const uploadMedia = async (
  accessToken: string,
  file: File,
  durationMs: number | null = null,
): Promise<AdminMediaAsset> => {
  const form = new FormData();
  form.append('file', file);
  if (durationMs !== null) form.append('durationMs', String(durationMs));

  const response = await authedFetch(`${apiBase()}/api/v1/admin/media`, accessToken, {
    method: 'POST',
    headers: { 'Idempotency-Key': crypto.randomUUID() },
    body: form,
  });

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as Partial<ApiProblem> | null;
    throw new ApiError({
      title: problem?.title ?? 'Upload failed',
      status: response.status,
      detail: problem?.detail ?? `HTTP ${response.status}`,
      code: problem?.code ?? 'UNKNOWN',
    });
  }

  return (await response.json()) as AdminMediaAsset;
};

/**
 * Fetch one asset's bytes (authorized) and hand back a local `blob:` URL for
 * `<audio>`/`<img>` � an element cannot present a bearer token, so the bytes
 * are fetched here and played from memory.
 */
export const fetchMediaObjectUrl = async (
  accessToken: string,
  mediaId: string,
): Promise<string> => {
  const response = await authedFetch(
    `${apiBase()}/api/v1/admin/media/${mediaId}/content`,
    accessToken,
    {},
  );
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as Partial<ApiProblem> | null;
    throw new ApiError({
      title: problem?.title ?? 'Playback failed',
      status: response.status,
      detail: problem?.detail ?? `HTTP ${response.status}`,
      code: problem?.code ?? 'UNKNOWN',
    });
  }
  const blob = await response.blob();
  return URL.createObjectURL(blob);
};

// ── Evaluation history, package history, and safe runtime configuration ────
// These are projections of the API contracts. Configuration deliberately has
// no credentials, endpoints, connection strings or provider headers.

export interface AdminEvaluationListItem {
  sessionId: string;
  markingId: string;
  module: string;
  taskNumber: number | null;
  rubricVersion: string;
  recomputedBand: number;
  reportedBand: number | null;
  flags: string[];
  isCurrent: boolean;
  version: number;
  markedAt: string;
}

export interface AdminEvaluationPage {
  items: AdminEvaluationListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface AdminEvaluationFilters {
  from?: string;
  to?: string;
  module?: string;
  flagged?: boolean;
  current?: boolean;
  page?: number;
}

export interface AdminEvaluationCriterion {
  criterion: string;
  band: number;
  feedback: string;
  /** Present only after an audited `includeContent` request. */
  evidence?: unknown;
}

export interface AdminEvaluationAttempt {
  id: string;
  taskNumber: number | null;
  provider: string | null;
  model: string | null;
  requestId: string | null;
  startedAt: string;
  finishedAt: string | null;
  outcome: string;
  errorCode: string | null;
  errorMessage: string | null;
  rawOutputTruncated: boolean;
  /** Present only after an audited `includeContent` request. */
  rawOutput?: string;
  markingId: string | null;
  markingVersion: number | null;
}

export interface AdminEvaluationDetail extends AdminEvaluationListItem {
  criteria: AdminEvaluationCriterion[];
  /** Present only after an audited `includeContent` request. */
  ungroundedEvidence?: unknown;
  advisories: string[];
  provenance: Record<string, unknown>;
  supersedesId: string | null;
  supersededById: string | null;
  attempts: AdminEvaluationAttempt[];
  /** Present only after an audited `includeContent` request. */
  learnerSubmission?: Record<string, string | null>;
}

export interface FailedMarkingJob {
  operationId: string;
  sessionId: string;
  module: string;
  rubricVersion: string;
  state: string;
  attempts: number;
  lastError: string | null;
  createdAt: string;
  failedAt: string | null;
  nextAttemptAt: string | null;
  completedAt: string | null;
}

export interface FailedMarkingJobPage {
  items: FailedMarkingJob[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface FailedMarkingJobFilters {
  from?: string;
  to?: string;
  module?: string;
  page?: number;
}

export interface EvaluationRerunResult {
  operationId: string;
  state: string;
  replayed: boolean;
  costConsequence: string;
  pricingStatus: string;
  pricingBlockers: string[];
}

const queryFor = (values: object) => {
  const query = new URLSearchParams();
  for (const [name, value] of Object.entries(values)) {
    if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
      query.set(name, String(value));
    }
  }
  return query.toString();
};

export const listEvaluations = (accessToken: string, filters: AdminEvaluationFilters = {}) =>
  request<AdminEvaluationPage>(`/api/v1/admin/evaluations?${queryFor(filters)}`, { accessToken });

export const getEvaluation = (
  accessToken: string,
  sessionId: string,
  markingId: string,
  includeContent = false,
) =>
  request<AdminEvaluationDetail>(
    `/api/v1/admin/evaluations/${encodeURIComponent(sessionId)}/${encodeURIComponent(markingId)}` +
      `?${queryFor({ includeContent })}`,
    { accessToken },
  );

export const listFailedMarkingJobs = (accessToken: string, filters: FailedMarkingJobFilters = {}) =>
  request<FailedMarkingJobPage>(`/api/v1/admin/evaluations/failed-jobs?${queryFor(filters)}`, {
    accessToken,
  });

export const rerunEvaluation = (accessToken: string, operationId: string) =>
  request<EvaluationRerunResult>(
    `/api/v1/admin/evaluations/failed-jobs/${encodeURIComponent(operationId)}/rerun`,
    { method: 'POST', accessToken, idempotencyKey: crypto.randomUUID() },
  );

export interface PackageImportHistorySummary {
  historyId: string;
  operationId: string | null;
  actorId: string;
  originalFileName: string;
  definitionId: string | null;
  versionNumber: number | null;
  sourceSha256: string | null;
  draftId: string | null;
  stage: string | null;
  result: string;
  findingCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface PackageImportHistoryDetail
  extends Omit<PackageImportHistorySummary, 'findingCount'> {
  findings: ImportFinding[];
}

export interface PackageImportHistoryPage {
  items: PackageImportHistorySummary[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface PackageImportHistoryFilters {
  result?: string;
  stage?: string;
  uploader?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export const listPackageHistory = (
  accessToken: string,
  filters: PackageImportHistoryFilters = {},
) =>
  request<PackageImportHistoryPage>(`/api/v1/admin/import/packages?${queryFor(filters)}`, {
    accessToken,
  });

export const getPackageHistory = (accessToken: string, historyId: string) =>
  request<PackageImportHistoryDetail>(
    `/api/v1/admin/import/package-history/${encodeURIComponent(historyId)}`,
    { accessToken },
  );

export interface AdminRuntimeConfiguration {
  ai: {
    skills: Array<{
      skill: string;
      status: string;
      provider: string | null;
      model: string | null;
      version: string | null;
      fallback: { provider: string; status: string; model: string | null } | null;
    }>;
  };
  writing: {
    rubricVersion: string | null;
    task1Weight: number | null;
    task2Weight: number | null;
    feedbackLanguage: string;
    criterionGranularity: string;
  };
  importArchive: {
    maxEntries: number;
    maxTotalUncompressedBytes: number;
    maxEntryUncompressedBytes: number;
    maxCompressionRatio: number;
    maxArchiveBytes: number;
    extractionTimeoutSeconds: number;
  };
  tokenPricing: { status: string; blockers: string[] };
}

export const getRuntimeConfiguration = (accessToken: string) =>
  request<AdminRuntimeConfiguration>('/api/v1/admin/config', { accessToken });
