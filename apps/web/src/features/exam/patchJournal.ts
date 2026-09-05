/**
 * Unsent answers, on disk, until the server says it has them.
 *
 * <b>What this is for, stated as the failure it removes.</b> A learner types an
 * answer. The autosave has not fired yet, or it has fired and is in flight, and
 * then the tab reloads — a crash, a WebView the OS reclaimed, a phone that
 * dropped to no signal and the learner pulled to refresh. Everything the page
 * held was in memory, so the answer is gone, and the sitting comes back looking
 * exactly as it did before they typed it.
 *
 * <b>IndexedDB, not `localStorage`, and the reason is not size.</b>
 * `localStorage` is synchronous, so every write blocks the thread the exam is
 * rendering on — during typing, which is the one moment it must not. It is also
 * a single string per key, so two tabs writing one sitting's journal read,
 * mutate and write back the whole thing, and the second one silently drops
 * whatever the first wrote. That is a lost-update bug in the mechanism whose
 * entire purpose is not to lose updates. IndexedDB is asynchronous and keyed
 * per record, so neither applies.
 *
 * <b>One record per response slot, not per keystroke.</b> An earlier value for a
 * slot is superseded by a later one by definition — the ordering token says
 * so — so keeping both would be keeping something the server would ignore. The
 * key is `session:module:responseSlotId` and a re-type overwrites in place.
 *
 * <b>An entry is deleted only when its own sequence is acknowledged.</b> Not
 * when "a save succeeded": the learner can type again while a request is in
 * flight, and clearing the journal on that request's response would drop an
 * answer the server has never seen — invisibly, and most often on the last
 * answer before Nộp bài, which is the one people go back to fix.
 *
 * <b>Every operation degrades to nothing.</b> A private window, a locked-down
 * WebView, a browser told to block site data, or a quota that is full all throw
 * — some on `indexedDB.open`, some later. Android and iOS ship through a
 * Capacitor WebView, so this is a real surface and not a theoretical one. A
 * journal that cannot be written is a journal that is not there, and the exam
 * must carry on exactly as it did before this file existed. It is a net, not a
 * dependency.
 */

const DATABASE = 'vni.exam';
const STORE = 'patches';
const VERSION = 1;

/** One unsent answer for one response slot. */
export interface JournalEntry {
  sessionId: string;
  module: string;
  responseSlotId: string;
  value: string | null;
  /** The ordering token this value was issued under. → `useAnswerSheet` */
  sequence: number;
  /** Client clock, for diagnosis only. Nothing decides anything from it. */
  savedAt: number;
}

function keyOf(sessionId: string, module: string, responseSlotId: string): string {
  return `${sessionId}:${module}:${responseSlotId}`;
}

/**
 * The open database, or null if this browser will not give us one.
 *
 * Opened once and shared. Re-opening per operation would be a connection per
 * keystroke, and a failed open would be retried on every one of them.
 */
let connecting: Promise<IDBDatabase | null> | null = null;

function connect(): Promise<IDBDatabase | null> {
  connecting ??= new Promise<IDBDatabase | null>((resolve) => {
    let request: IDBOpenDBRequest;

    try {
      // `indexedDB` itself can be absent — jsdom without a polyfill, a WebView
      // with storage disabled — and reading the property can throw rather than
      // return undefined.
      if (typeof indexedDB === 'undefined') return resolve(null);
      request = indexedDB.open(DATABASE, VERSION);
    } catch {
      return resolve(null);
    }

    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(STORE)) {
        const store = db.createObjectStore(STORE);
        // Reading one sitting's journal must not scan every sitting the device
        // has ever held. A learner who sits ten papers accumulates ten
        // journals, and the restore runs while the exam is loading.
        store.createIndex('bySection', ['sessionId', 'module'], { unique: false });
      }
    };

    request.onsuccess = () => resolve(request.result);
    request.onerror = () => resolve(null);
    request.onblocked = () => resolve(null);
  });

  return connecting;
}

/** Runs `work` in a transaction, resolving to `fallback` if anything refuses. */
async function inStore<T>(
  mode: IDBTransactionMode,
  fallback: T,
  work: (store: IDBObjectStore, done: (value: T) => void) => void,
): Promise<T> {
  const db = await connect();
  if (db === null) return fallback;

  return new Promise<T>((resolve) => {
    let settled = false;
    const finish = (value: T) => {
      if (settled) return;
      settled = true;
      resolve(value);
    };

    try {
      const transaction = db.transaction(STORE, mode);
      transaction.onerror = () => finish(fallback);
      transaction.onabort = () => finish(fallback);
      work(transaction.objectStore(STORE), finish);
    } catch {
      // A transaction can throw synchronously — the store is gone, the
      // connection was closed by the browser reclaiming storage.
      finish(fallback);
    }
  });
}

/**
 * Records one unsent answer, replacing whatever that slot held.
 *
 * Fire and forget from the caller's point of view: a keystroke must not wait
 * on a disk write, and a disk write that fails must not surface as an error in
 * the middle of a paper. What it protects against is the tab going away, and a
 * tab that goes away takes the pending write with it either way.
 */
export async function remember(entry: JournalEntry): Promise<void> {
  await inStore<void>('readwrite', undefined, (store, done) => {
    const request = store.put(
      entry,
      keyOf(entry.sessionId, entry.module, entry.responseSlotId),
    );
    request.onsuccess = () => done(undefined);
    request.onerror = () => done(undefined);
  });
}

/**
 * Forgets one answer, but only if the journal still holds the sequence given.
 *
 * <b>The condition is the whole point.</b> Clearing on "a save succeeded" would
 * drop an answer typed while that save was in flight: the response acknowledges
 * sequence 7, the journal already holds 8, and 8 is the one the server has
 * never seen. Deleting it there loses exactly one correction, silently.
 */
export async function acknowledge(
  sessionId: string,
  module: string,
  responseSlotId: string,
  sequence: number,
): Promise<void> {
  const key = keyOf(sessionId, module, responseSlotId);

  await inStore<void>('readwrite', undefined, (store, done) => {
    const read = store.get(key);

    read.onerror = () => done(undefined);
    read.onsuccess = () => {
      const held = read.result as JournalEntry | undefined;

      // Nothing held, or something newer is held: leave it alone.
      if (held === undefined || held.sequence > sequence) return done(undefined);

      const remove = store.delete(key);
      remove.onsuccess = () => done(undefined);
      remove.onerror = () => done(undefined);
    };
  });
}

/** Everything unsent for one section, in no particular order. */
export async function restore(sessionId: string, module: string): Promise<JournalEntry[]> {
  return inStore<JournalEntry[]>('readonly', [], (store, done) => {
    const request = store.index('bySection').getAll([sessionId, module]);
    request.onsuccess = () => done((request.result as JournalEntry[]) ?? []);
    request.onerror = () => done([]);
  });
}

/**
 * Drops a whole section's journal.
 *
 * <b>Called when the section closes, not when the sitting ends.</b> A closed
 * section takes no more writes — the server refuses them, by ADR-0015 — so
 * anything still journalled for it is work that can never be sent, and keeping
 * it would restore an answer on the next load that the learner can neither save
 * nor remove.
 */
export async function forgetSection(sessionId: string, module: string): Promise<void> {
  await inStore<void>('readwrite', undefined, (store, done) => {
    const request = store.index('bySection').getAllKeys([sessionId, module]);

    request.onerror = () => done(undefined);
    request.onsuccess = () => {
      const keys = (request.result as IDBValidKey[]) ?? [];
      if (keys.length === 0) return done(undefined);

      let left = keys.length;
      for (const key of keys) {
        const remove = store.delete(key);
        const tick = () => {
          if (--left === 0) done(undefined);
        };
        remove.onsuccess = tick;
        remove.onerror = tick;
      }
    };
  });
}

/** For tests: drops the shared connection so the next call opens a fresh one. */
export function resetJournalConnection(): void {
  connecting = null;
}
