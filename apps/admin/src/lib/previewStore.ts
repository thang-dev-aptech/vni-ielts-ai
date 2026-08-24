/**
 * Preview content for the review flow.
 *
 * <b>Read this before using anything below.</b> These are not exams. Phase 1
 * builds the lifecycle — roles, ownership, the six states, the three queue
 * screens — and the server does not yet carry any of them: `ExamVersionStatus`
 * has three values, there is no `createdBy`, and no endpoint accepts a submit
 * or a review. There is also no exam content to move through the flow, which
 * is the reason the product owner asked for the surface first.
 *
 * So the queue screens run on this store, in the browser, and say so on every
 * screen. The alternative — wiring them to `/admin/exams` and quietly filling
 * the missing fields with defaults — would produce a CMS that looks finished
 * and lies about which half is real. When the endpoints land, the screens swap
 * `useWorkflow()` for the API and the rest of the code does not move: the
 * shapes here are the shapes the document specifies.
 *
 * <b>What is genuinely finished</b> is the part that does not depend on a
 * server: the state machine in `lifecycle.ts`, permission and ownership
 * gating, the consequence copy, focus and keyboard behaviour, and the audit
 * line each action would write.
 *
 * → docs/ux/cms-content-operations.md §4.3
 */

import { useCallback, useSyncExternalStore } from 'react';
import type { ExamState, Transition } from './lifecycle.js';
import type { MediaAsset, VersionAsset } from './media.js';

const KEY = 'vni.cms.preview.v2';

export interface ReviewNote {
  id: string;
  authorName: string;
  at: string;
  body: string;
  /** Which question the note is pinned to, when it is pinned to one. */
  anchor: string | null;
}

export interface PreviewModule {
  module: string;
  questionCount: number;
}

/**
 * Authorship, resolved late.
 *
 * A seeded row cannot know the address of whoever signs in, so rows the
 * preview wants the current operator to own carry `self: true` and take their
 * name at read time. It keeps "đề của tôi" meaningful for any account.
 */
export type PreviewAuthor = { self: true } | { self: false; name: string };

export interface PreviewVersion {
  versionId: string;
  definitionId: string;
  title: string;
  variant: 'academic' | 'general';
  versionNumber: number;
  state: ExamState;
  modules: PreviewModule[];
  author: PreviewAuthor;
  createdAt: string;
  submittedAt: string | null;
  reviewedAt: string | null;
  reviewedByName: string | null;
  publishedAt: string | null;
  notes: ReviewNote[];
  topic: string;
  difficultyAuthored: string;
  /** Every `assetRef` the content carries, resolved or not. */
  assets: VersionAsset[];
}

export interface PreviewAudit {
  id: string;
  at: string;
  actorEmail: string;
  action: string;
  targetLabel: string;
  detail: string;
}

interface PreviewState {
  versions: PreviewVersion[];
  audit: PreviewAudit[];
  media: MediaAsset[];
}

const now = () => new Date().toISOString();
const daysAgo = (n: number) => new Date(Date.now() - n * 86_400_000).toISOString();
const id = () => crypto.randomUUID();

/**
 * The seed.
 *
 * Six versions, one per state, so every branch of the flow has something to
 * act on the moment the screen opens. Titles are deliberately marked as
 * samples — an operator must never mistake one of these for content a learner
 * could receive.
 */
function seed(): PreviewState {
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

  return {
    media,
    versions: [
      {
        versionId: id(),
        definitionId: 'sample-reading-024',
        title: 'Đề mẫu · Reading Practice Test 024',
        variant: 'academic',
        versionNumber: 1,
        state: 'draft',
        modules: [{ module: 'reading', questionCount: 40 }],
        author: { self: true },
        createdAt: daysAgo(2),
        submittedAt: null,
        reviewedAt: null,
        reviewedByName: null,
        publishedAt: null,
        notes: [],
        topic: 'Môi trường',
        difficultyAuthored: '6.5',
        assets: [
          {
            ref: 'media/' + diagram,
            mediaId: diagram,
            usedAt: 'Passage 2 · câu 18 (labelling)',
            kind: 'image',
          },
        ],
      },
      {
        versionId: id(),
        definitionId: 'sample-listening-011',
        title: 'Đề mẫu · Listening Practice Test 011',
        variant: 'academic',
        versionNumber: 2,
        state: 'in-review',
        modules: [{ module: 'listening', questionCount: 40 }],
        author: { self: true },
        createdAt: daysAgo(6),
        submittedAt: daysAgo(3),
        reviewedAt: null,
        reviewedByName: null,
        publishedAt: null,
        notes: [],
        topic: 'Giáo dục',
        difficultyAuthored: '6.0',
        /* Deliberately incomplete: Part 2 references audio nothing answers to.
           This is the failure the media surface exists to catch, and catching
           it at review is the whole point of having a review. */
        assets: [
          {
            ref: 'assets/listening-011-part-1.m4a',
            mediaId: null,
            usedAt: 'Section 1 · Part 1',
            kind: 'audio',
          },
          {
            ref: 'assets/listening-011-part-2.m4a',
            mediaId: null,
            usedAt: 'Section 1 · Part 2',
            kind: 'audio',
          },
        ],
      },
      {
        versionId: id(),
        definitionId: 'sample-writing-007',
        title: 'Đề mẫu · Writing Task 2 · Đề 007',
        variant: 'academic',
        versionNumber: 1,
        state: 'in-review',
        modules: [{ module: 'writing', questionCount: 2 }],
        author: { self: false, name: 'Trần B' },
        createdAt: daysAgo(9),
        submittedAt: daysAgo(6),
        reviewedAt: null,
        reviewedByName: null,
        publishedAt: null,
        notes: [],
        topic: 'Công nghệ',
        difficultyAuthored: '7.0',
        assets: [],
      },
      {
        versionId: id(),
        definitionId: 'sample-reading-019',
        title: 'Đề mẫu · Reading Practice Test 019',
        variant: 'general',
        versionNumber: 3,
        state: 'returned',
        modules: [{ module: 'reading', questionCount: 40 }],
        author: { self: true },
        createdAt: daysAgo(14),
        submittedAt: daysAgo(9),
        reviewedAt: daysAgo(8),
        reviewedByName: 'Trưởng chuyên môn (mẫu)',
        publishedAt: null,
        notes: [
          {
            id: id(),
            authorName: 'Trưởng chuyên môn (mẫu)',
            at: daysAgo(8),
            body: 'Câu 12 và câu 13 cùng đáp án B — kiểm tra lại đáp án chuẩn.',
            anchor: 'Câu 12',
          },
          {
            id: id(),
            authorName: 'Trưởng chuyên môn (mẫu)',
            at: daysAgo(8),
            body: 'Passage 3 dài hơn mức của General Training. Cân nhắc rút bớt.',
            anchor: 'Passage 3',
          },
        ],
        topic: 'Sức khoẻ',
        difficultyAuthored: '5.5',
        assets: [],
      },
      {
        versionId: id(),
        definitionId: 'sample-listening-004',
        title: 'Đề mẫu · Listening Practice Test 004',
        variant: 'academic',
        versionNumber: 1,
        state: 'approved',
        modules: [{ module: 'listening', questionCount: 40 }],
        author: { self: false, name: 'Trần B' },
        createdAt: daysAgo(20),
        submittedAt: daysAgo(12),
        reviewedAt: daysAgo(10),
        reviewedByName: 'Trưởng chuyên môn (mẫu)',
        publishedAt: null,
        notes: [],
        topic: 'Du lịch',
        difficultyAuthored: '6.0',
        assets: [
          { ref: 'media/' + audio3, mediaId: audio3, usedAt: 'Section 1 · Part 1', kind: 'audio' },
        ],
      },
      {
        versionId: id(),
        definitionId: 'sample-full-002',
        title: 'Đề mẫu · Full Test 002',
        variant: 'academic',
        versionNumber: 4,
        state: 'published',
        modules: [
          { module: 'reading', questionCount: 40 },
          { module: 'listening', questionCount: 40 },
          { module: 'writing', questionCount: 2 },
          { module: 'speaking', questionCount: 3 },
        ],
        author: { self: false, name: 'Trần B' },
        createdAt: daysAgo(40),
        submittedAt: daysAgo(35),
        reviewedAt: daysAgo(33),
        reviewedByName: 'Trưởng chuyên môn (mẫu)',
        publishedAt: daysAgo(30),
        notes: [],
        topic: 'Tổng hợp',
        difficultyAuthored: '6.5',
        assets: [
          { ref: 'media/' + audio1, mediaId: audio1, usedAt: 'Listening · Part 1', kind: 'audio' },
          { ref: 'media/' + audio2, mediaId: audio2, usedAt: 'Listening · Part 2', kind: 'audio' },
        ],
      },
    ],
    audit: [],
  };
}

function read(): PreviewState {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw === null) return seed();
    const parsed = JSON.parse(raw) as PreviewState;
    // A shape that does not match is preview data from an older build. Reseed
    // rather than render half a screen.
    if (
      !Array.isArray(parsed.versions) ||
      !Array.isArray(parsed.audit) ||
      !Array.isArray(parsed.media)
    )
      return seed();
    return parsed;
  } catch {
    return seed();
  }
}

function write(state: PreviewState) {
  try {
    localStorage.setItem(KEY, JSON.stringify(state));
  } catch {
    // Private windows and full quotas both land here. The screens keep
    // working from memory for this tab; only persistence is lost.
  }
}

/**
 * Applied when a transition fires, mirroring the fields the server will set.
 *
 * Exported for the tests: this function is the whole of what "approving"
 * means to a record, and it is worth pinning down before an endpoint exists to
 * disagree with it.
 */
export function advance(
  version: PreviewVersion,
  transition: Transition,
  actorName: string,
  note: string,
): PreviewVersion {
  const stamped: PreviewVersion = { ...version, state: transition.to };

  if (transition.id === 'submit') stamped.submittedAt = now();
  if (transition.id === 'withdraw') stamped.submittedAt = null;

  if (transition.id === 'approve' || transition.id === 'return') {
    stamped.reviewedAt = now();
    stamped.reviewedByName = actorName;
  }

  if (transition.id === 'unapprove') {
    stamped.reviewedAt = null;
    stamped.reviewedByName = null;
  }

  if (transition.id === 'publish') stamped.publishedAt = now();

  if (note.trim() !== '') {
    stamped.notes = [
      ...version.notes,
      { id: id(), authorName: actorName, at: now(), body: note.trim(), anchor: null },
    ];
  }

  return stamped;
}

export interface Workflow {
  versions: PreviewVersion[];
  audit: PreviewAudit[];
  media: MediaAsset[];
  apply: (versionId: string, transition: Transition, note: string) => void;
  addMedia: (asset: MediaAsset) => void;
  retireMedia: (mediaId: string) => void;
  deleteMedia: (mediaId: string) => void;
  reset: () => void;
}

/**
 * Uploaded bytes, for this tab only.
 *
 * <b>Not in the store, and not in `localStorage`.</b> A Listening part is
 * megabytes; putting one in web storage fails on the quota and would be the
 * wrong thing to do if it succeeded. What survives a reload is the metadata —
 * name, size, duration, checksum — which is exactly what the server will hold
 * once the upload endpoint exists. The playable URL lives here until the tab
 * closes, and a row whose URL has gone says so rather than offering a player
 * that does nothing.
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
  objectUrls.set(mediaId, url);
  try {
    localStorage.setItem(UPLOADED_KEY, JSON.stringify([...new Set([...uploadedIds(), mediaId])]));
  } catch {
    /* Private window or full quota. Playback still works this session. */
  }
}

export function objectUrlFor(mediaId: string): string | null {
  return objectUrls.get(mediaId) ?? null;
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
 *
 * The three queue screens and the detail screen are four views of the same
 * six rows, and `useState` in each would give each its own copy: approve
 * something on the detail screen, navigate back, and the queue still shows it
 * waiting until React happens to remount. Subscribing to one module-level
 * value is both smaller than a provider and harder to get wrong.
 */
let current: PreviewState | null = null;
const listeners = new Set<() => void>();

function snapshot(): PreviewState {
  current ??= read();
  return current;
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => void listeners.delete(listener);
}

function commit(next: PreviewState) {
  current = next;
  write(next);
  for (const listener of listeners) listener();
}

/** The preview's read and write side. */
export function useWorkflow(actorName: string, actorEmail: string): Workflow {
  const state = useSyncExternalStore(subscribe, snapshot, snapshot);

  const apply = useCallback(
    (versionId: string, transition: Transition, note: string) => {
      const base = snapshot();
      const target = base.versions.find((v) => v.versionId === versionId);
      if (target === undefined) return;

      commit({
        ...base,
        versions: base.versions.map((v) =>
          v.versionId === versionId ? advance(v, transition, actorName, note) : v,
        ),
        audit: [
          {
            id: id(),
            at: now(),
            actorEmail,
            action: transition.audit,
            targetLabel: target.title,
            detail: `${target.state} → ${transition.to}`,
          },
          ...base.audit,
        ],
      });
    },
    [actorEmail, actorName],
  );

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

  return {
    versions: state.versions,
    audit: state.audit,
    media: state.media,
    apply,
    addMedia,
    retireMedia,
    deleteMedia,
    reset,
  };
}

/** Whether the signed-in operator authored this version. → `exam.*.own` */
export function ownedByMe(version: PreviewVersion): boolean {
  return version.author.self;
}

export function authorName(version: PreviewVersion, meName: string): string {
  return version.author.self ? meName : version.author.name;
}

/** How long something has been waiting, in words an operator can act on. */
export function waitingFor(since: string | null): string {
  if (since === null) return '—';
  const days = Math.floor((Date.now() - new Date(since).getTime()) / 86_400_000);
  if (days >= 1) return `${days} ngày`;
  const hours = Math.floor((Date.now() - new Date(since).getTime()) / 3_600_000);
  if (hours >= 1) return `${hours} giờ`;
  return 'vừa xong';
}
