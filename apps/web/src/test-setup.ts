import '@testing-library/jest-dom/vitest';

/**
 * jsdom implements neither of these, and both are optional at runtime — the
 * app guards for them. Providing inert versions here keeps every test
 * exercising the ordinary path rather than the fallback, so a component that
 * secretly depends on one still fails loudly.
 */
if (typeof window.matchMedia === 'undefined') {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    }),
  });
}

if (typeof globalThis.IntersectionObserver === 'undefined') {
  class NoopIntersectionObserver implements IntersectionObserver {
    readonly root = null;
    readonly rootMargin = '';
    readonly thresholds: readonly number[] = [];
    observe() {}
    unobserve() {}
    disconnect() {}
    takeRecords(): IntersectionObserverEntry[] {
      return [];
    }
  }
  globalThis.IntersectionObserver =
    NoopIntersectionObserver as unknown as typeof IntersectionObserver;
}

/**
 * jsdom has `window.scrollTo` but it throws "Not implemented" on every call,
 * which fills the run with stderr that looks like a failure and is not. The
 * article page scrolls to the top when the slug changes — real behaviour worth
 * keeping, and nothing a test can observe.
 */
window.scrollTo = () => {};
