import '@testing-library/jest-dom/vitest';

/**
 * `crypto.randomUUID` is present in modern jsdom but not guaranteed across the
 * versions this suite might run under, and the preview store mints ids with
 * it. An inert counter keeps the tests deterministic without the store having
 * to know it is being tested.
 */
if (typeof globalThis.crypto?.randomUUID !== 'function') {
  let n = 0;
  Object.defineProperty(globalThis, 'crypto', {
    value: { ...globalThis.crypto, randomUUID: () => `test-${++n}` },
    writable: true,
  });
}
