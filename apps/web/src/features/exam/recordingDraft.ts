/**
 * Spoken answers that have not yet been accepted by the server.
 *
 * <b>Same job as `patchJournal`, for bytes instead of strings.</b> A learner
 * finishes a part, the upload stalls on a dead radio, and the tab reloads —
 * without this the only copy was in a `Blob` held by a React ref, and the
 * answer is gone. IndexedDB keeps the bytes across reloads; the recorder reads
 * them back and offers "Gửi lại" rather than forcing a re-record against a
 * clock that never paused.
 *
 * <b>Bytes as `ArrayBuffer`, not `Blob`.</b> Structured clone of a `Blob` is
 * unreliable across fake-indexeddb / some WebViews (the value comes back as a
 * plain object with no `size`). An `ArrayBuffer` round-trips; the recorder
 * rebuilds a `Blob` on load.
 *
 * <b>A separate database from the answer journal.</b> Blobs are larger, the
 * schema is different, and a journal upgrade must never risk the typed-answer
 * store. Failure degrades to nothing: private windows and locked-down WebViews
 * refuse the open, and the exam carries on with in-memory retry only.
 */

const DATABASE = 'vni.speaking';
const STORE = 'drafts';
const VERSION = 1;

export interface RecordingDraft {
  sessionId: string;
  questionId: string;
  blob: Blob;
  mimeType: string;
  /** Client clock, for diagnosis only. */
  savedAt: number;
}

/** On-disk shape — number[] so structured clone never invents a foreign ArrayBuffer. */
interface StoredDraft {
  sessionId: string;
  questionId: string;
  bytes: number[];
  mimeType: string;
  savedAt: number;
}

function keyOf(sessionId: string, questionId: string): string {
  return `${sessionId}:${questionId}`;
}

let connecting: Promise<IDBDatabase | null> | null = null;

function connect(): Promise<IDBDatabase | null> {
  connecting ??= new Promise<IDBDatabase | null>((resolve) => {
    let request: IDBOpenDBRequest;

    try {
      if (typeof indexedDB === 'undefined') return resolve(null);
      request = indexedDB.open(DATABASE, VERSION);
    } catch {
      return resolve(null);
    }

    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(STORE)) {
        db.createObjectStore(STORE);
      }
    };

    request.onsuccess = () => resolve(request.result);
    request.onerror = () => resolve(null);
    request.onblocked = () => resolve(null);
  });

  return connecting;
}

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
      finish(fallback);
    }
  });
}

function readBlobBytes(blob: Blob): Promise<ArrayBuffer> {
  // Prefer FileReader: jsdom Blobs are not acceptable to undici's `Response`
  // (`object.stream is not a function`), and a Response-based polyfill hangs or
  // throws. Real browsers and Node Blobs still work via FileReader or the
  // native `arrayBuffer()` fallback below.
  if (typeof FileReader !== 'undefined') {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as ArrayBuffer);
      reader.onerror = () => {
        if (typeof blob.arrayBuffer === 'function') {
          void blob.arrayBuffer().then(resolve, reject);
          return;
        }
        reject(reader.error ?? new Error('FileReader failed'));
      };
      try {
        reader.readAsArrayBuffer(blob);
      } catch (caught) {
        if (typeof blob.arrayBuffer === 'function') {
          void blob.arrayBuffer().then(resolve, reject);
        } else {
          reject(caught);
        }
      }
    });
  }

  if (typeof blob.arrayBuffer === 'function') return blob.arrayBuffer();
  return Promise.reject(new Error('No Blob byte reader available'));
}

/** Persists one unsent recording, replacing any earlier draft for the slot. */
export async function rememberDraft(draft: RecordingDraft): Promise<void> {
  let buffer: ArrayBuffer;
  try {
    buffer = await readBlobBytes(draft.blob);
  } catch {
    return;
  }

  const stored: StoredDraft = {
    sessionId: draft.sessionId,
    questionId: draft.questionId,
    bytes: Array.from(new Uint8Array(buffer)),
    mimeType: draft.mimeType,
    savedAt: draft.savedAt,
  };

  await inStore<void>('readwrite', undefined, (store, done) => {
    const request = store.put(stored, keyOf(draft.sessionId, draft.questionId));
    request.onsuccess = () => done(undefined);
    request.onerror = () => done(undefined);
  });
}

/** The draft for one question, or null when none / storage refused. */
export async function loadDraft(
  sessionId: string,
  questionId: string,
): Promise<RecordingDraft | null> {
  return inStore<RecordingDraft | null>('readonly', null, (store, done) => {
    const request = store.get(keyOf(sessionId, questionId));
    request.onsuccess = () => {
      const raw = request.result as
        | StoredDraft
        | (RecordingDraft & { bytes?: unknown })
        | undefined;
      if (raw === undefined) return done(null);

      if ('bytes' in raw && Array.isArray(raw.bytes)) {
        return done({
          sessionId: raw.sessionId,
          questionId: raw.questionId,
          blob: new Blob([new Uint8Array(raw.bytes)], { type: raw.mimeType }),
          mimeType: raw.mimeType,
          savedAt: raw.savedAt,
        });
      }

      // ArrayBuffer shape from an earlier revision of this module.
      if ('bytes' in raw && raw.bytes instanceof ArrayBuffer) {
        return done({
          sessionId: raw.sessionId,
          questionId: raw.questionId,
          blob: new Blob([raw.bytes], { type: raw.mimeType }),
          mimeType: raw.mimeType,
          savedAt: raw.savedAt,
        });
      }

      // Legacy Blob shape, if a browser actually preserved it.
      if ('blob' in raw && raw.blob instanceof Blob) {
        return done(raw as RecordingDraft);
      }

      done(null);
    };
    request.onerror = () => done(null);
  });
}

/** Drops the draft once the server has accepted the upload. */
export async function forgetDraft(sessionId: string, questionId: string): Promise<void> {
  await inStore<void>('readwrite', undefined, (store, done) => {
    const request = store.delete(keyOf(sessionId, questionId));
    request.onsuccess = () => done(undefined);
    request.onerror = () => done(undefined);
  });
}

/** For tests: drops the shared connection so the next call opens a fresh one. */
export function resetDraftConnection(): void {
  connecting = null;
}
