import 'fake-indexeddb/auto';

import { IDBFactory } from 'fake-indexeddb';
import { afterEach, beforeEach, expect, it } from 'vitest';
import {
  forgetDraft,
  loadDraft,
  rememberDraft,
  resetDraftConnection,
} from './recordingDraft.js';

/**
 * Against a real IndexedDB implementation, not a stub of one. Same reason as
 * `patchJournal.test.ts`: the open / upgrade / transaction path is what fails
 * in private windows, and mocking `IDBObjectStore.put` would hide it.
 *
 * Bytes are stored as `number[]` so structured clone never invents a foreign
 * ArrayBuffer / Blob stand-in that FileReader cannot read back.
 */

beforeEach(() => {
  resetDraftConnection();
  // eslint-disable-next-line no-global-assign -- test isolation
  indexedDB = new IDBFactory();
});

afterEach(() => {
  resetDraftConnection();
});

it('round-trips a blob for one session/question pair', async () => {
  const blob = new Blob(['spoken'], { type: 'audio/webm' });

  await rememberDraft({
    sessionId: 'sit-1',
    questionId: 's-part-2',
    blob,
    mimeType: 'audio/webm',
    savedAt: 1_700_000_000_000,
  });

  const held = await loadDraft('sit-1', 's-part-2');
  expect(held).not.toBeNull();
  expect(held!.mimeType).toBe('audio/webm');
  expect(held!.blob).toBeTruthy();
  expect(held!.blob.size).toBe(blob.size);
  expect(held!.questionId).toBe('s-part-2');
});

it('replaces an earlier draft for the same slot', async () => {
  await rememberDraft({
    sessionId: 'sit-1',
    questionId: 's-part-2',
    blob: new Blob(['first'], { type: 'audio/webm' }),
    mimeType: 'audio/webm',
    savedAt: 1,
  });
  await rememberDraft({
    sessionId: 'sit-1',
    questionId: 's-part-2',
    blob: new Blob(['second-answer'], { type: 'audio/webm' }),
    mimeType: 'audio/webm',
    savedAt: 2,
  });

  const held = await loadDraft('sit-1', 's-part-2');
  expect(held!.blob.size).toBe(new Blob(['second-answer']).size);
  expect(held!.savedAt).toBe(2);
});

it('forgets a draft once the server has accepted it', async () => {
  await rememberDraft({
    sessionId: 'sit-1',
    questionId: 's-part-2',
    blob: new Blob(['spoken'], { type: 'audio/webm' }),
    mimeType: 'audio/webm',
    savedAt: 1,
  });

  await forgetDraft('sit-1', 's-part-2');
  expect(await loadDraft('sit-1', 's-part-2')).toBeNull();
});

it('keeps drafts for other questions when one is forgotten', async () => {
  await rememberDraft({
    sessionId: 'sit-1',
    questionId: 's-part-1',
    blob: new Blob(['one'], { type: 'audio/webm' }),
    mimeType: 'audio/webm',
    savedAt: 1,
  });
  await rememberDraft({
    sessionId: 'sit-1',
    questionId: 's-part-2',
    blob: new Blob(['two'], { type: 'audio/webm' }),
    mimeType: 'audio/webm',
    savedAt: 1,
  });

  await forgetDraft('sit-1', 's-part-1');
  expect(await loadDraft('sit-1', 's-part-1')).toBeNull();
  expect(await loadDraft('sit-1', 's-part-2')).not.toBeNull();
});

it('does nothing at all, quietly, where there is no IndexedDB', async () => {
  const held = indexedDB;
  // eslint-disable-next-line no-global-assign -- deliberate absence
  indexedDB = undefined as unknown as IDBFactory;
  resetDraftConnection();

  await rememberDraft({
    sessionId: 'sit-1',
    questionId: 's-part-2',
    blob: new Blob(['spoken'], { type: 'audio/webm' }),
    mimeType: 'audio/webm',
    savedAt: 1,
  });
  expect(await loadDraft('sit-1', 's-part-2')).toBeNull();

  // eslint-disable-next-line no-global-assign -- restore
  indexedDB = held;
});
