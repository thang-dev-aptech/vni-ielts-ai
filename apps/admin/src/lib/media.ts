/**
 * Media: what the CMS accepts, what it may do with it, and what it must refuse.
 *
 * <b>Why this file is more than a list of MIME types.</b> An exam is not only
 * text. A Listening section is a passage of audio, a Labelling question is a
 * diagram, and both reach the learner through an `assetRef` in the exam
 * content. Which means media has two properties nothing else in the CMS has:
 * an exam can be *complete* and still be broken because a file behind it does
 * not resolve, and replacing a file changes what a published exam plays
 * without changing a single character of the exam.
 *
 * Both of those are encoded here rather than in a screen, because both have to
 * hold on every screen.
 *
 * <b>The checks in this file are for the operator, not for safety.</b> Sniffing
 * a magic number in the browser tells someone their .mp3 is really a .docx
 * before they wait for an upload — useful, and worth doing. It is not the
 * boundary: an uploaded file is untrusted input and the server validates it
 * again from scratch, the same way the ZIP pipeline does.
 * → docs/security/zip-ingestion-security.md
 */

export type MediaKind = 'audio' | 'image' | 'file';

export interface MediaAsset {
  mediaId: string;
  kind: MediaKind;
  /**
   * Display only, and never a storage key.
   *
   * The same rule the package format states for `assetRef`: a client-supplied
   * filename must not influence where a file lands, or the name becomes a way
   * to write somewhere it should not.
   */
  fileName: string;
  contentType: string;
  bytes: number;
  /** Audio only, and null when the browser could not read it. */
  durationMs: number | null;
  /** SHA-256 of the content. What makes "the same file" answerable. */
  checksum: string;
  uploadedByName: string;
  uploadedAt: string;
  /** Withdrawn from the picker. Still resolves for everything already using it. */
  retired: boolean;
}

/** One `assetRef` inside a version, and what it points at. */
export interface VersionAsset {
  /** The reference as it appears in the exam content. */
  ref: string;
  /** Null when nothing in the library answers to this reference. */
  mediaId: string | null;
  /** Where in the exam it is used, for a human. */
  usedAt: string;
  kind: MediaKind;
}

export const KIND_LABEL: Record<MediaKind, string> = {
  audio: 'Âm thanh',
  image: 'Hình ảnh',
  file: 'Tệp',
};

/**
 * Size ceilings.
 *
 * A Listening part is about half an hour of speech; at a sane bitrate that is
 * comfortably inside 50 MB, and anything far past it is a wav somebody
 * exported by accident. These are the numbers an operator sees *before*
 * choosing a file — the refusal message afterwards still names the category
 * and not the threshold, which is the rule the ZIP pipeline already follows.
 */
export const MAX_BYTES: Record<MediaKind, number> = {
  audio: 50 * 1024 * 1024,
  image: 5 * 1024 * 1024,
  file: 20 * 1024 * 1024,
};

/**
 * Magic numbers, not extensions.
 *
 * A file called `part-1.mp3` says nothing about what is inside it. These are
 * the openings of the container formats a Listening section can actually use.
 */
const SIGNATURES: { kind: MediaKind; contentType: string; at: number; bytes: number[] }[] = [
  { kind: 'audio', contentType: 'audio/mpeg', at: 0, bytes: [0x49, 0x44, 0x33] }, // ID3
  { kind: 'audio', contentType: 'audio/mpeg', at: 0, bytes: [0xff, 0xfb] }, // MPEG frame sync
  { kind: 'audio', contentType: 'audio/mpeg', at: 0, bytes: [0xff, 0xf3] },
  { kind: 'audio', contentType: 'audio/mp4', at: 4, bytes: [0x66, 0x74, 0x79, 0x70] }, // ftyp
  { kind: 'audio', contentType: 'audio/wav', at: 0, bytes: [0x52, 0x49, 0x46, 0x46] }, // RIFF
  { kind: 'audio', contentType: 'audio/ogg', at: 0, bytes: [0x4f, 0x67, 0x67, 0x53] }, // OggS
  { kind: 'image', contentType: 'image/png', at: 0, bytes: [0x89, 0x50, 0x4e, 0x47] },
  { kind: 'image', contentType: 'image/jpeg', at: 0, bytes: [0xff, 0xd8, 0xff] },
  { kind: 'image', contentType: 'image/webp', at: 8, bytes: [0x57, 0x45, 0x42, 0x50] },
  { kind: 'file', contentType: 'application/pdf', at: 0, bytes: [0x25, 0x50, 0x44, 0x46] },
];

export interface Sniffed {
  kind: MediaKind;
  contentType: string;
}

/** What the bytes actually are, or null if nothing recognises them. */
export function sniff(head: Uint8Array): Sniffed | null {
  for (const signature of SIGNATURES) {
    const matches = signature.bytes.every((b, i) => head[signature.at + i] === b);
    if (matches) return { kind: signature.kind, contentType: signature.contentType };
  }
  return null;
}

export type RejectionCode = 'UNRECOGNISED_FORMAT' | 'TOO_LARGE' | 'EMPTY_FILE';

export const REJECTION: Record<RejectionCode, string> = {
  UNRECOGNISED_FORMAT: 'Định dạng tệp không nằm trong danh sách chấp nhận.',
  TOO_LARGE: 'Tệp vượt hạn mức dung lượng cho loại này.',
  EMPTY_FILE: 'Tệp rỗng.',
};

/** The operator-facing check. The server runs its own, from scratch. */
export function inspect(head: Uint8Array, bytes: number): Sniffed | RejectionCode {
  if (bytes === 0) return 'EMPTY_FILE';
  const kind = sniff(head);
  if (kind === null) return 'UNRECOGNISED_FORMAT';
  if (bytes > MAX_BYTES[kind.kind]) return 'TOO_LARGE';
  return kind;
}

/* ── What may be done to a media asset ──────────────────────────────────── */

export type AssetState = 'free' | 'in-use' | 'locked' | 'retired';

export const ASSET_STATE: Record<AssetState, { label: string; hint: string }> = {
  free: {
    label: 'Chưa dùng',
    hint: 'Chưa có đề nào tham chiếu tới tệp này. Xoá được.',
  },
  'in-use': {
    label: 'Đang dùng',
    hint: 'Có bản nháp đang tham chiếu. Gỡ khỏi bộ chọn được, xoá thì không.',
  },
  locked: {
    label: 'Khoá — đề đã xuất bản',
    hint: 'Một version đã xuất bản đang dùng tệp này. Nội dung tệp là bất biến.',
  },
  retired: {
    label: 'Đã gỡ khỏi bộ chọn',
    hint: 'Không chọn được cho đề mới. Đề đang dùng vẫn phát bình thường.',
  },
};

/** Something that references media, reduced to what the rules need. */
export interface ReferencingVersion {
  versionId: string;
  title: string;
  state: string;
  assets: VersionAsset[];
}

export function usedBy(asset: MediaAsset, versions: ReferencingVersion[]): ReferencingVersion[] {
  return versions.filter((v) => v.assets.some((a) => a.mediaId === asset.mediaId));
}

/**
 * <b>The rule that protects a published exam from the back door.</b>
 *
 * A published `ExamVersion` is immutable, and the whole scoring history depends
 * on that. Media referenced by one is therefore immutable too — otherwise the
 * reference stays identical, the file behind it changes, and every candidate
 * hears different audio while the version number says nothing happened. There
 * is no "thay tệp": a new recording is a new asset and a new version.
 */
export function assetState(asset: MediaAsset, versions: ReferencingVersion[]): AssetState {
  const users = usedBy(asset, versions);
  if (users.some((v) => v.state === 'published' || v.state === 'unpublished')) return 'locked';
  if (asset.retired) return 'retired';
  if (users.length > 0) return 'in-use';
  return 'free';
}

/** Deleting is for something nothing has ever shipped with. */
export function mayDelete(asset: MediaAsset, versions: ReferencingVersion[]): boolean {
  return usedBy(asset, versions).length === 0;
}

/** Retiring hides it from the picker and touches nothing already using it. */
export function mayRetire(asset: MediaAsset, versions: ReferencingVersion[]): boolean {
  return !asset.retired && assetState(asset, versions) !== 'locked';
}

/* ── What an exam is missing ────────────────────────────────────────────── */

/**
 * References in this version that resolve to nothing.
 *
 * <b>This is the failure the whole media surface exists to prevent.</b> An exam
 * can be complete, reviewed and approved, and still put a candidate in front of
 * a Listening part with no sound. The ZIP pipeline already refuses a package
 * whose assets do not resolve; in-place authoring needs the same gate, and it
 * needs it before publication rather than during an attempt.
 */
export function missingAssets(version: { assets: VersionAsset[] }): VersionAsset[] {
  return version.assets.filter((a) => a.mediaId === null);
}

/**
 * Why this version cannot go to learners yet, in sentences an operator can act
 * on. Empty means nothing is standing in the way.
 */
export function publishBlockers(version: { assets: VersionAsset[] }): string[] {
  const missing = missingAssets(version);
  if (missing.length === 0) return [];

  return [
    missing.length === 1
      ? `Thiếu 1 tệp media: ${missing[0]?.usedAt}. Học viên sẽ gặp một phần không phát được.`
      : `Thiếu ${missing.length} tệp media. Học viên sẽ gặp các phần không phát được.`,
  ];
}

/* ── Formatting ─────────────────────────────────────────────────────────── */

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export function formatDuration(ms: number | null): string {
  if (ms === null) return '—';
  const total = Math.round(ms / 1000);
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

/** SHA-256, hex. What makes "is this the same file" answerable. */
export async function checksumOf(bytes: ArrayBuffer): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', bytes);
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('');
}

/**
 * How long the audio runs, read by the browser that will play it.
 *
 * Resolves to null rather than rejecting: a duration the browser cannot
 * determine is a fact about the file worth showing, not an error worth
 * blocking an upload over. The server probes it again anyway.
 */
export function probeDuration(url: string): Promise<number | null> {
  return new Promise((resolve) => {
    const audio = new Audio();
    const done = (value: number | null) => {
      audio.onloadedmetadata = null;
      audio.onerror = null;
      resolve(value);
    };

    audio.onloadedmetadata = () =>
      done(Number.isFinite(audio.duration) ? Math.round(audio.duration * 1000) : null);
    audio.onerror = () => done(null);
    audio.src = url;
  });
}
