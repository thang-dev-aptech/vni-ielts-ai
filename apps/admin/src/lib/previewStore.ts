/**
 * The media library's browser-only store.
 *
 * <b>What this file used to be, and what it narrowed to.</b> It used to carry
 * two unrelated things behind one `useWorkflow()` hook: a six-state exam
 * review simulation (`versions`, `apply`, `advance`, `TRANSITIONS`-driven
 * mutation) standing in for endpoints that did not exist yet, and a set of
 * media-asset fixtures for `MediaLibraryPage`'s "which exam uses this file"
 * demo. The endpoints now exist — `submit-for-review`, `approve`,
 * `return-to-draft` are real, `ReviewQueuePage`, `PendingPublishPage` and
 * `ExamDetailPage` read and write them directly — so the lifecycle half is
 * retired along with `WorkflowDetailPage` and `MyExamsPage`, the two screens
 * that only ever read it.
 *
 * <b>What is left has a narrower, still-real reason to exist.</b> There is no
 * `media.read`/`media.upload`/`media.retire` permission in
 * `PermissionKeys.All` at all — the media library has no server endpoint to
 * cut over to, not a half-built one. `MediaLibraryPage` keeps running on this
 * store, with its own on-screen notice saying so, until a media API exists.
 *
 * <b>What is genuinely finished</b> is the part that never depended on a
 * server: magic-byte sniffing, the checksum, the size ceilings, and the
 * asset-lock rule (`media.ts`) that a published exam's media is immutable.
 */

import { useCallback, useSyncExternalStore } from 'react';
import type { MediaAsset, ReferencingVersion } from './media.js';

const KEY = 'vni.cms.preview.media.v1';

const daysAgo = (n: number) => new Date(Date.now() - n * 86_400_000).toISOString();
const id = () => crypto.randomUUID();

interface MediaLibraryState {
  media: MediaAsset[];
  /** Fixture rows for the "đang dùng ở đâu" / lock demo — display only. */
  demoExams: ReferencingVersion[];
}

/**
 * The seed.
 *
 * Four media files and three fixture exam rows — enough to show every asset
 * state (`free`, `in-use`, `locked`, `retired`) the media rules define.
 */
function seed(): MediaLibraryState {
  const audio1 = id();
  const audio2 = id();
  const audio3 = id();
  const diagram = id();

  const media: MediaAsset[] = [
    {
      mediaId: audio1,
      kind: 'audio',
      fileName: 'full-test-002-listening-part-1.m4a',
      contentType: 'audio/mp4',
      bytes: 8_412_160,
      durationMs: 1_732_000,
      checksum: 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
      uploadedByName: 'Trần B',
      uploadedAt: daysAgo(42),
      retired: false,
    },
    {
      mediaId: audio2,
      kind: 'audio',
      fileName: 'full-test-002-listening-part-2.m4a',
      contentType: 'audio/mp4',
      bytes: 7_903_744,
      durationMs: 1_610_000,
      checksum: '9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08',
      uploadedByName: 'Trần B',
      uploadedAt: daysAgo(42),
      retired: false,
    },
    {
      mediaId: audio3,
      kind: 'audio',
      fileName: 'listening-004-part-1.m4a',
      contentType: 'audio/mp4',
      bytes: 6_291_456,
      durationMs: 1_455_000,
      checksum: '2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae',
      uploadedByName: 'Trần B',
      uploadedAt: daysAgo(21),
      retired: false,
    },
    {
      mediaId: diagram,
      kind: 'image',
      fileName: 'reading-024-diagram.png',
      contentType: 'image/png',
      bytes: 214_016,
      durationMs: null,
      checksum: 'fcde2b2edba56bf408601fb721fe9b5c338d10ee429ea04fae5511b68fbf8fb9',
      uploadedByName: 'Nguyễn A',
      uploadedAt: daysAgo(2),
      retired: false,
    },
  ];

  const demoExams: ReferencingVersion[] = [
    {
      versionId: 'demo-reading-024',
      title: 'Đề mẫu · Reading Practice Test 024',
      state: 'draft',
      assets: [
        { ref: 'media/' + diagram, mediaId: diagram, usedAt: 'Passage 2 · câu 18 (labelling)', kind: 'image' },
      ],
    },
    {
      versionId: 'demo-listening-004',
      title: 'Đề mẫu · Listening Practice Test 004',
      state: 'draft',
      assets: [
        { ref: 'media/' + audio3, mediaId: audio3, usedAt: 'Section 1 · Part 1', kind: 'audio' },
      ],
    },
    {
      versionId: 'demo-full-002',
      title: 'Đề mẫu · Full Test 002',
      state: 'published',
      assets: [
        { ref: 'media/' + audio1, mediaId: audio1, usedAt: 'Listening · Part 1', kind: 'audio' },
        { ref: 'media/' + audio2, mediaId: audio2, usedAt: 'Listening · Part 2', kind: 'audio' },
      ],
    },
  ];

  return { media, demoExams };
}

function read(): MediaLibraryState {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw === null) return seed();
    const parsed = JSON.parse(raw) as MediaLibraryState;
    // A shape that does not match is preview data from an older build. Reseed
    // rather than render half a screen.
    if (!Array.isArray(parsed.media) || !Array.isArray(parsed.demoExams)) return seed();
    return parsed;
  } catch {
    return seed();
  }
}

function write(state: MediaLibraryState) {
  try {
    localStorage.setItem(KEY, JSON.stringify(state));
  } catch {
    // Private windows and full quotas both land here. The screen keeps
    // working from memory for this tab; only persistence is lost.
  }
}

/**
 * Uploaded bytes, for this tab only.
 *
 * <b>Not in the store, and not in `localStorage`.</b> A Listening part is
 * megabytes; putting one in web storage fails on the quota and would be the
 * wrong thing to do if it succeeded. What survives a reload is the metadata —
 * name, size, duration, checksum — which is exactly what a real media
 * endpoint would hold once one exists. The playable URL lives here until the
 * tab closes, and a row whose URL has gone says so rather than offering a
 * player that does nothing.
 */
const objectUrls = new Map<string, string>();

const UPLOADED_KEY = 'vni.cms.preview.uploaded';

function uploadedIds(): string[] {
  try {
    const raw = localStorage.getItem(UPLOADED_KEY);
    const parsed = raw === null ? [] : (JSON.parse(raw) as unknown);
    return Array.isArray(parsed) ? (parsed as string[]) : [];
  } catch {
    return [];
  }
}

export function rememberObjectUrl(mediaId: string, url: string) {
  if (!url.startsWith('blob:')) {
    throw new TypeError('Media previews accept only browser-created blob URLs.');
  }
  objectUrls.set(mediaId, url);
  try {
    localStorage.setItem(UPLOADED_KEY, JSON.stringify([...new Set([...uploadedIds(), mediaId])]));
  } catch {
    /* Private window or full quota. Playback still works this session. */
  }
}

export function objectUrlFor(mediaId: string): string | null {
  const url = objectUrls.get(mediaId);
  return url?.startsWith('blob:') === true ? url : null;
}

/**
 * Whether a real file was ever chosen for this asset in this browser.
 *
 * <b>The distinction the screen has to draw.</b> A seeded row never had bytes
 * behind it and never will; an uploaded row had them until the tab reloaded.
 * Both show no player, and telling an operator the same sentence about each
 * would be wrong about one of them.
 */
export function uploadedHere(mediaId: string): boolean {
  return objectUrls.has(mediaId) || uploadedIds().includes(mediaId);
}

/*
 * One store for the whole app, not one per component.
 */
let current: MediaLibraryState | null = null;
const listeners = new Set<() => void>();

function snapshot(): MediaLibraryState {
  current ??= read();
  return current;
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => void listeners.delete(listener);
}

function commit(next: MediaLibraryState) {
  current = next;
  write(next);
  for (const listener of listeners) listener();
}

export interface MediaLibrary {
  media: MediaAsset[];
  demoExams: ReferencingVersion[];
  addMedia: (asset: MediaAsset) => void;
  retireMedia: (mediaId: string) => void;
  deleteMedia: (mediaId: string) => void;
  reset: () => void;
}

/** The preview media library's read and write side. */
export function useMediaLibrary(): MediaLibrary {
  const state = useSyncExternalStore(subscribe, snapshot, snapshot);

  const addMedia = useCallback((asset: MediaAsset) => {
    const base = snapshot();
    commit({ ...base, media: [asset, ...base.media] });
  }, []);

  /*
   * Retiring hides an asset from the picker. Deleting removes it outright, and
   * is refused for anything a version has ever referenced — the check lives in
   * `media.ts` so the screen and the store cannot disagree about it.
   */
  const retireMedia = useCallback((mediaId: string) => {
    const base = snapshot();
    commit({
      ...base,
      media: base.media.map((m) => (m.mediaId === mediaId ? { ...m, retired: true } : m)),
    });
  }, []);

  const deleteMedia = useCallback((mediaId: string) => {
    const base = snapshot();
    commit({ ...base, media: base.media.filter((m) => m.mediaId !== mediaId) });
    objectUrls.delete(mediaId);
  }, []);

  const reset = useCallback(() => commit(seed()), []);

  return { media: state.media, demoExams: state.demoExams, addMedia, retireMedia, deleteMedia, reset };
}
