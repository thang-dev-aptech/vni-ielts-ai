import { request } from '../../lib/api.js';
import type { Article, ArticleCategory, ArticleSummary } from './articles.js';

/**
 * `GET /api/v1/library/articles` and `GET /api/v1/library/articles/{slug}`.
 *
 * <b>Public, anonymous, published rows only</b> — same group, same guarantee
 * as `documentsApi.ts`. A draft slug answers 404 rather than confirming it
 * exists (`backend/.../LibraryEndpoints.cs`), which is indistinguishable from
 * an unknown one on purpose.
 *
 * <b>Response shape is cast, not re-typed</b> — `ArticleSummaryView` /
 * `ArticleView` on the wire are camelCase supersets of `ArticleSummary` /
 * `Article`; the extra CMS fields (`status`, `createdBy`, …) are read and
 * ignored rather than given a second type definition.
 */

export interface LibraryRequestOptions {
  signal?: AbortSignal;
}

export async function listArticles(
  category?: ArticleCategory,
  options?: LibraryRequestOptions,
): Promise<ArticleSummary[]> {
  const qs = category ? `?category=${encodeURIComponent(category)}` : '';
  const result = await request<{ items: ArticleSummary[] }>(`/api/v1/library/articles${qs}`, {
    signal: options?.signal,
  });
  // Defensive, not merely typed — see `documentsApi.ts#listDocuments`.
  return result.items ?? [];
}

/** 404s — via `ApiError` — for an unknown slug or a draft the learner may not see. */
export function getArticleBySlug(
  slug: string,
  options?: LibraryRequestOptions,
): Promise<Article> {
  return request<Article>(`/api/v1/library/articles/${encodeURIComponent(slug)}`, {
    signal: options?.signal,
  });
}
