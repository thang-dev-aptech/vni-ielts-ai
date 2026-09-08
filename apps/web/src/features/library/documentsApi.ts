import { request } from '../../lib/api.js';
import type { DocumentBand, DocumentSkill, DocumentType, LibraryDocument } from './documents.js';

/**
 * `GET /api/v1/library/documents` and `GET /api/v1/library/documents/{id}`.
 *
 * <b>Public, anonymous, published rows only.</b> `backend/.../LibraryEndpoints.cs`
 * names the group "anonymous, read-only, published-content-only" — a draft
 * cannot reach a learner because the handler behind it has no way to ask the
 * store for one. No access token travels with either call.
 *
 * <b>Response shape is cast, not re-typed.</b> `LibraryDocumentView` on the
 * wire is camelCase and is a strict superset of `LibraryDocument` — it carries
 * a few CMS-only fields (`status`, `createdBy`, `relatedExamIds`, `createdAt`)
 * that a learner screen never reads. Declaring a second, narrower interface
 * for the same record would be the "two competing definitions" this slice was
 * told to avoid, so the wire view is read directly as `LibraryDocument`.
 */

export interface LibraryDocumentFilter {
  skill?: DocumentSkill;
  type?: DocumentType;
  band?: DocumentBand;
  /** Free-text query — title, description, category, topic, format. */
  q?: string;
}

export interface LibraryRequestOptions {
  signal?: AbortSignal;
}

function query(filter?: LibraryDocumentFilter): string {
  if (!filter) return '';
  const params = new URLSearchParams();
  if (filter.skill) params.set('skill', filter.skill);
  if (filter.type) params.set('type', filter.type);
  if (filter.band) params.set('band', filter.band);
  if (filter.q) params.set('q', filter.q);
  const qs = params.toString();
  return qs ? `?${qs}` : '';
}

export async function listDocuments(
  filter?: LibraryDocumentFilter,
  options?: LibraryRequestOptions,
): Promise<LibraryDocument[]> {
  const result = await request<{ items: LibraryDocument[] }>(
    `/api/v1/library/documents${query(filter)}`,
    { signal: options?.signal },
  );
  // Defensive, not merely typed: `request()` casts the payload rather than
  // validating it, so a malformed or unexpected response reads as "nothing
  // published" instead of throwing `.filter is not a function` deep inside a
  // render.
  return result.items ?? [];
}

/** 404s — via `ApiError` — for an unknown id or a draft the learner may not see. */
export function getDocument(
  id: string,
  options?: LibraryRequestOptions,
): Promise<LibraryDocument> {
  return request<LibraryDocument>(`/api/v1/library/documents/${encodeURIComponent(id)}`, {
    signal: options?.signal,
  });
}
