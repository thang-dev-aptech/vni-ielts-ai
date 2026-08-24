import { request } from '@vni/auth';

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
  /** `draft` · `published` · `unpublished`. A published version is immutable. */
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
