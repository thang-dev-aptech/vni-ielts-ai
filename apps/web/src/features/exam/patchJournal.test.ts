import 'fake-indexeddb/auto';
import { afterEach, beforeEach, expect, it } from 'vitest';
import { IDBFactory } from 'fake-indexeddb';
import {
  acknowledge,
  forgetSection,
  remember,
  resetJournalConnection,
  restore,
} from './patchJournal.js';

/**
 * The journal that keeps an answer through a reload.
 *
 * <b>Against a real IndexedDB implementation, not a stub of one.</b> Every rule
 * here is one an in-memory `Map` gets right for free and the actual API does
 * not: an index over a compound key, a `get` and a `delete` inside one
 * transaction, a store that has to be created on upgrade. A fake that resolved
 * those by construction would prove nothing about the file it is testing —
 * which is the whole reason `fake-indexeddb` is a dependency rather than a
 * dictionary.
 */

beforeEach(() => {
  // A fresh database per test. Sharing one would make "restores what was never
  // sent" depend on the order the file happened to run in.
  globalThis.indexedDB = new IDBFactory();
  resetJournalConnection();
});

afterEach(() => {
  resetJournalConnection();
});

it('keeps an unsent answer, keyed by its question', async () => {
  await remember({
    sessionId: 'sit-1',
    module: 'reading',
    questionId: 'r-1',
    value: 'cartography',
    sequence: 3,
    savedAt: 1,
  });

  const held = await restore('sit-1', 'reading');

  expect(held).toHaveLength(1);
  expect(held[0]!.value).toBe('cartography');
  expect(held[0]!.sequence).toBe(3);
});

it('replaces a question rather than stacking every keystroke', async () => {
  // An earlier value for a question is superseded by a later one by definition
  // — the ordering token says so — so keeping both would keep something the
  // server would ignore, and would grow the journal for the length of an essay.
  for (const [value, sequence] of [
    ['c', 1],
    ['ca', 2],
    ['cart', 3],
  ] as const) {
    await remember({
      sessionId: 'sit-1',
      module: 'reading',
      questionId: 'r-1',
      value,
      sequence,
      savedAt: sequence,
    });
  }

  const held = await restore('sit-1', 'reading');

  expect(held).toHaveLength(1);
  expect(held[0]!.value).toBe('cart');
});

it('keeps each section apart', async () => {
  // Reading and Listening are different sheets with different revisions and
  // different tokens. Restoring one section's work into another would send a
  // question id to a section that has never heard of it.
  await remember({
    sessionId: 'sit-1',
    module: 'reading',
    questionId: 'r-1',
    value: 'reading',
    sequence: 1,
    savedAt: 1,
  });
  await remember({
    sessionId: 'sit-1',
    module: 'listening',
    questionId: 'l-1',
    value: 'listening',
    sequence: 1,
    savedAt: 1,
  });

  expect(await restore('sit-1', 'reading')).toHaveLength(1);
  expect((await restore('sit-1', 'listening'))[0]!.value).toBe('listening');
  expect(await restore('sit-2', 'reading')).toHaveLength(0);
});

it('forgets an answer once its own sequence is acknowledged', async () => {
  await remember({
    sessionId: 'sit-1',
    module: 'reading',
    questionId: 'r-1',
    value: 'sent',
    sequence: 4,
    savedAt: 1,
  });

  await acknowledge('sit-1', 'reading', 'r-1', 4);

  expect(await restore('sit-1', 'reading')).toHaveLength(0);
});

it('keeps an answer typed while the save that acknowledged the last one was in flight', async () => {
  /*
   * <b>The condition on the delete is the whole point of the file.</b>
   *
   * Sequence 4 goes out. The learner types again while it is in flight, so the
   * journal now holds 5. Then 4's response arrives. Clearing on "a save
   * succeeded" would drop 5 — an answer the server has never seen — and it
   * would do it invisibly, most often on the last answer before Nộp bài, which
   * is the one people go back to fix.
   */
  await remember({
    sessionId: 'sit-1',
    module: 'reading',
    questionId: 'r-1',
    value: 'typed while in flight',
    sequence: 5,
    savedAt: 2,
  });

  await acknowledge('sit-1', 'reading', 'r-1', 4);

  const held = await restore('sit-1', 'reading');

  expect(held).toHaveLength(1);
  expect(held[0]!.value).toBe('typed while in flight');
});

it('drops a whole section when it closes', async () => {
  // A closed section takes no more writes (ADR-0015), so anything still
  // journalled for it is work that can never be sent — and restoring it on the
  // next load would put an answer on screen the learner can neither save nor
  // remove.
  await remember({
    sessionId: 'sit-1',
    module: 'reading',
    questionId: 'r-1',
    value: 'one',
    sequence: 1,
    savedAt: 1,
  });
  await remember({
    sessionId: 'sit-1',
    module: 'reading',
    questionId: 'r-2',
    value: 'two',
    sequence: 2,
    savedAt: 2,
  });
  await remember({
    sessionId: 'sit-1',
    module: 'listening',
    questionId: 'l-1',
    value: 'kept',
    sequence: 1,
    savedAt: 1,
  });

  await forgetSection('sit-1', 'reading');

  expect(await restore('sit-1', 'reading')).toHaveLength(0);
  expect(await restore('sit-1', 'listening')).toHaveLength(1);
});

it('records a cleared answer, because an erase is work too', async () => {
  // `null` is the learner rubbing an answer out, and it has to survive a reload
  // exactly as a typed answer does. Treating absence and null as the same thing
  // would restore the answer they deleted.
  await remember({
    sessionId: 'sit-1',
    module: 'reading',
    questionId: 'r-1',
    value: null,
    sequence: 2,
    savedAt: 1,
  });

  const held = await restore('sit-1', 'reading');

  expect(held).toHaveLength(1);
  expect(held[0]!.value).toBeNull();
});

it('does nothing at all, quietly, where there is no IndexedDB', async () => {
  /*
   * A private window, a locked-down WebView, a browser told to block site data.
   * Android and iOS ship through a Capacitor WebView, so this is a real surface
   * rather than a theoretical one — and the exam has to carry on exactly as it
   * did before this file existed. A net, not a dependency.
   */
  const real = globalThis.indexedDB;

  try {
    // @ts-expect-error deliberately removing the API the way a browser does
    delete globalThis.indexedDB;
    resetJournalConnection();

    await expect(
      remember({
        sessionId: 'sit-1',
        module: 'reading',
        questionId: 'r-1',
        value: 'nowhere to put this',
        sequence: 1,
        savedAt: 1,
      }),
    ).resolves.toBeUndefined();

    expect(await restore('sit-1', 'reading')).toEqual([]);
    await expect(acknowledge('sit-1', 'reading', 'r-1', 1)).resolves.toBeUndefined();
    await expect(forgetSection('sit-1', 'reading')).resolves.toBeUndefined();
  } finally {
    globalThis.indexedDB = real;
    resetJournalConnection();
  }
});
