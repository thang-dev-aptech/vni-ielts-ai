import { describe, expect, it } from 'vitest';
import {
  MAX_BYTES,
  assetState,
  formatBytes,
  formatDuration,
  inspect,
  mayDelete,
  mayRetire,
  missingAssets,
  publishBlockers,
  sniff,
  usedBy,
  type MediaAsset,
  type ReferencingVersion,
  type VersionAsset,
} from '../lib/media.js';

/**
 * The media rules, pinned down.
 *
 * Two of these are not preferences. A file behind a published exam cannot be
 * replaced or deleted, because the reference stays the same while the sound
 * changes — and an exam whose audio does not resolve must not reach a
 * candidate. Both are cheap to break by accident later, so both are tests.
 */

const head = (...bytes: number[]) => {
  const buffer = new Uint8Array(16);
  bytes.forEach((b, i) => (buffer[i] = b));
  return buffer;
};

const at = (offset: number, ...bytes: number[]) => {
  const buffer = new Uint8Array(16);
  bytes.forEach((b, i) => (buffer[offset + i] = b));
  return buffer;
};

function asset(overrides: Partial<MediaAsset> = {}): MediaAsset {
  return {
    mediaId: 'm1',
    kind: 'audio',
    fileName: 'part-1.m4a',
    contentType: 'audio/mp4',
    bytes: 1024,
    durationMs: 60_000,
    checksum: 'abc',
    uploadedByName: 'Trần B',
    uploadedAt: '2026-08-01T00:00:00.000Z',
    retired: false,
    ...overrides,
  };
}

function version(state: string, mediaId: string | null = 'm1'): ReferencingVersion {
  const assets: VersionAsset[] =
    mediaId === null
      ? [{ ref: 'assets/missing.m4a', mediaId: null, usedAt: 'Section 1 · Part 1', kind: 'audio' }]
      : [{ ref: `media/${mediaId}`, mediaId, usedAt: 'Section 1 · Part 1', kind: 'audio' }];
  return { versionId: 'v-' + state, title: 'Đề mẫu', state, assets };
}

describe('sniffing the bytes rather than the extension', () => {
  it('recognises the audio containers a Listening part can use', () => {
    expect(sniff(head(0x49, 0x44, 0x33))?.contentType).toBe('audio/mpeg'); // ID3
    expect(sniff(head(0xff, 0xfb))?.contentType).toBe('audio/mpeg'); // frame sync
    expect(sniff(at(4, 0x66, 0x74, 0x79, 0x70))?.contentType).toBe('audio/mp4'); // ftyp
    expect(sniff(head(0x52, 0x49, 0x46, 0x46))?.contentType).toBe('audio/wav');
    expect(sniff(head(0x4f, 0x67, 0x67, 0x53))?.contentType).toBe('audio/ogg');
  });

  it('recognises images and pdf', () => {
    expect(sniff(head(0x89, 0x50, 0x4e, 0x47))?.kind).toBe('image');
    expect(sniff(head(0xff, 0xd8, 0xff))?.kind).toBe('image');
    expect(sniff(head(0x25, 0x50, 0x44, 0x46))?.kind).toBe('file');
  });

  it('refuses a zip container renamed to .mp3', () => {
    expect(sniff(head(0x50, 0x4b, 0x03, 0x04))).toBeNull();
  });

  it('refuses plain text', () => {
    expect(sniff(head(0x68, 0x65, 0x6c, 0x6c, 0x6f))).toBeNull();
  });
});

describe('inspect', () => {
  it('names the category, and the caller decides what to say', () => {
    expect(inspect(head(0x49, 0x44, 0x33), 0)).toBe('EMPTY_FILE');
    expect(inspect(head(0x50, 0x4b), 100)).toBe('UNRECOGNISED_FORMAT');
    expect(inspect(head(0x49, 0x44, 0x33), MAX_BYTES.audio + 1)).toBe('TOO_LARGE');
  });

  it('holds an image to the image ceiling, not the audio one', () => {
    const bytes = MAX_BYTES.image + 1;
    expect(bytes).toBeLessThan(MAX_BYTES.audio);
    expect(inspect(head(0x89, 0x50, 0x4e, 0x47), bytes)).toBe('TOO_LARGE');
  });

  it('accepts a well-formed audio file inside the ceiling', () => {
    expect(inspect(head(0x49, 0x44, 0x33), 5_000_000)).toEqual({
      kind: 'audio',
      contentType: 'audio/mpeg',
    });
  });
});

describe('what may be done to a file', () => {
  it('locks a file a published version depends on', () => {
    expect(assetState(asset(), [version('published')])).toBe('locked');
  });

  it('keeps it locked after the version is taken down, because results still point at it', () => {
    expect(assetState(asset(), [version('unpublished')])).toBe('locked');
  });

  it('calls it in-use while only drafts reference it', () => {
    expect(assetState(asset(), [version('draft')])).toBe('in-use');
  });

  it('calls it free when nothing references it', () => {
    expect(assetState(asset(), [])).toBe('free');
  });

  it('never deletes a file any version has ever referenced', () => {
    expect(mayDelete(asset(), [version('draft')])).toBe(false);
    expect(mayDelete(asset(), [version('published')])).toBe(false);
    expect(mayDelete(asset(), [])).toBe(true);
  });

  it('refuses to retire a locked file, and refuses to retire one twice', () => {
    expect(mayRetire(asset(), [version('published')])).toBe(false);
    expect(mayRetire(asset({ retired: true }), [])).toBe(false);
    expect(mayRetire(asset(), [version('draft')])).toBe(true);
  });

  it('lists every version standing on a file', () => {
    expect(
      usedBy(asset(), [version('draft'), version('published'), version('x', null)]),
    ).toHaveLength(2);
  });
});

describe('an exam that cannot be sat', () => {
  it('finds the reference that resolves to nothing', () => {
    expect(missingAssets(version('in-review', null))).toHaveLength(1);
  });

  it('names where the missing file was meant to play', () => {
    expect(publishBlockers(version('approved', null))[0]).toContain('Section 1 · Part 1');
  });

  it('stands aside once every reference resolves', () => {
    expect(publishBlockers(version('approved'))).toEqual([]);
  });

  it('counts rather than lists when several are missing', () => {
    const broken: { assets: VersionAsset[] } = {
      assets: [
        { ref: 'a', mediaId: null, usedAt: 'Part 1', kind: 'audio' },
        { ref: 'b', mediaId: null, usedAt: 'Part 2', kind: 'audio' },
      ],
    };
    expect(publishBlockers(broken)[0]).toContain('2');
  });
});

describe('formatting', () => {
  it('reads sizes the way an operator says them', () => {
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(2048)).toBe('2 KB');
    expect(formatBytes(8_412_160)).toBe('8.0 MB');
  });

  it('reads a duration as minutes and seconds, and admits when it does not know', () => {
    expect(formatDuration(1_732_000)).toBe('28:52');
    expect(formatDuration(null)).toBe('—');
  });
});
