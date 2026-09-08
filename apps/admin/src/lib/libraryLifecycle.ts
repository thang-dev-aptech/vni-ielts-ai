/**
 * The four-state lifecycle shared by Documents and Articles — `Đ8` in
 * `docs/ux/cms-content-operations.md` §3.2.
 *
 * <b>Deliberately not an extension of `lifecycle.ts`.</b> That file's
 * `ExamState`/`TRANSITIONS` model six states with ownership rules for a
 * different content type (exam review), owned by a different task this wave.
 * Folding this in would mean two unrelated shapes sharing one union type —
 * the next edit to either risks the other. This is the minimal equivalent for
 * a simpler lifecycle with no ownership concept at all: every transition here
 * checks a permission only, never "am I the author" (`AdminLibraryEndpoints.cs`
 * has no ownership check on any of the four verbs).
 *
 * <pre>
 * Draft ──► PendingReview ──► Published ⇄ Unpublished
 *   ▲              │
 *   └── return ────┘
 * </pre>
 *
 * Publish is reachable straight from Draft and from Unpublished, not only
 * from PendingReview — there is no "Approved" state for this content type.
 */

export type LibraryStatus = 'draft' | 'pending' | 'published' | 'unpublished';

export const LIBRARY_STATUSES: readonly LibraryStatus[] = [
  'draft',
  'pending',
  'published',
  'unpublished',
];

export interface LibraryStatusFace {
  label: string;
  tone: 'neutral' | 'hold' | 'live' | 'muted';
}

/** No state here is red — a draft or a pending item is not a failure. */
export const LIBRARY_STATUS: Record<LibraryStatus, LibraryStatusFace> = {
  draft: { label: 'Bản nháp', tone: 'neutral' },
  pending: { label: 'Chờ duyệt', tone: 'hold' },
  published: { label: 'Đang xuất bản', tone: 'live' },
  unpublished: { label: 'Đã gỡ', tone: 'muted' },
};

export const canSubmit = (status: LibraryStatus): boolean => status === 'draft';
export const canReturn = (status: LibraryStatus): boolean => status === 'pending';
export const canPublish = (status: LibraryStatus): boolean => status !== 'published';
export const canUnpublish = (status: LibraryStatus): boolean => status === 'published';
/** Learner-visible content is unpublished first, never deleted out from under a learner. */
export const canDelete = (status: LibraryStatus): boolean => status !== 'published';

export type LibraryTransitionId = 'submit' | 'return' | 'publish' | 'unpublish';

export interface LibraryTransitionDef {
  id: LibraryTransitionId;
  label: string;
  tone: 'primary' | 'secondary' | 'danger';
  allowed: (status: LibraryStatus) => boolean;
  /** Which of the two permission suffixes the server checks for this verb. */
  needs: 'write' | 'publish';
}

export const LIBRARY_TRANSITIONS: readonly LibraryTransitionDef[] = [
  { id: 'submit', label: 'Nộp duyệt', tone: 'primary', allowed: canSubmit, needs: 'write' },
  { id: 'return', label: 'Trả về bản nháp', tone: 'secondary', allowed: canReturn, needs: 'publish' },
  { id: 'publish', label: 'Xuất bản', tone: 'primary', allowed: canPublish, needs: 'publish' },
  { id: 'unpublish', label: 'Gỡ xuất bản', tone: 'danger', allowed: canUnpublish, needs: 'publish' },
];

/**
 * Every transition open to this status, given what the operator holds.
 *
 * `can` already carries the `document.*`/`article.*` prefix decision — this
 * function is shape, not which resource it is about.
 */
export function libraryTransitionsFor(
  status: LibraryStatus,
  can: (permission: 'write' | 'publish') => boolean,
): LibraryTransitionDef[] {
  return LIBRARY_TRANSITIONS.filter((t) => t.allowed(status) && can(t.needs));
}
