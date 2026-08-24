import { render } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useReveal } from './useReveal.js';

function Harness() {
  useReveal();
  return (
    <>
      <div data-reveal data-testid="a" />
      <div data-reveal data-testid="b" />
    </>
  );
}

/** jsdom has neither matchMedia nor IntersectionObserver. */
function stubEnvironment({ reducedMotion }: { reducedMotion: boolean }) {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({
      matches: query.includes('prefers-reduced-motion') ? reducedMotion : false,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  );

  const observed: Element[] = [];
  const disconnect = vi.fn();

  class FakeObserver {
    constructor(private readonly cb: IntersectionObserverCallback) {}
    observe(el: Element) {
      observed.push(el);
    }
    unobserve = vi.fn();
    disconnect = disconnect;
    /** Drives the callback the way scrolling would. */
    fire(el: Element) {
      this.cb(
        [{ target: el, isIntersecting: true } as unknown as IntersectionObserverEntry],
        this as unknown as IntersectionObserver,
      );
    }
  }

  let instance: FakeObserver | null = null;
  vi.stubGlobal(
    'IntersectionObserver',
    vi.fn((cb: IntersectionObserverCallback) => {
      instance = new FakeObserver(cb);
      return instance;
    }),
  );

  return { observed, disconnect, getInstance: () => instance };
}

afterEach(() => {
  vi.unstubAllGlobals();
  document.documentElement.className = '';
});

describe('useReveal', () => {
  it('does nothing at all when the visitor asked for reduced motion', () => {
    // The important half is not "no animation" — it is that nothing gets
    // HIDDEN. The hiding rule is scoped to .reveal-armed, so never adding that
    // class is what keeps the page fully readable.
    const env = stubEnvironment({ reducedMotion: true });

    render(<Harness />);

    expect(document.documentElement.classList.contains('reveal-armed')).toBe(false);
    expect(env.observed).toHaveLength(0);
  });

  it('arms the hiding rule only after the effect runs', () => {
    // Content is visible by default in CSS and hidden only once this class is
    // present. If the bundle fails to load, the page reads normally instead of
    // rendering blank — which is the failure mode of the usual
    // hidden-by-default approach.
    stubEnvironment({ reducedMotion: false });

    render(<Harness />);

    expect(document.documentElement.classList.contains('reveal-armed')).toBe(true);
  });

  it('observes every marked block', () => {
    const env = stubEnvironment({ reducedMotion: false });

    render(<Harness />);

    expect(env.observed).toHaveLength(2);
  });

  it('reveals a block once and then stops watching it', () => {
    // Re-hiding on scroll-up makes a page feel unstable and makes text
    // impossible to re-read.
    const env = stubEnvironment({ reducedMotion: false });
    const { getByTestId } = render(<Harness />);

    const target = getByTestId('a');
    env.getInstance()!.fire(target);

    expect(target.classList.contains('is-revealed')).toBe(true);
    expect(env.getInstance()!.unobserve).toHaveBeenCalledWith(target);
  });

  it('disconnects and disarms on unmount', () => {
    const env = stubEnvironment({ reducedMotion: false });

    const { unmount } = render(<Harness />);
    unmount();

    expect(env.disconnect).toHaveBeenCalled();
    expect(document.documentElement.classList.contains('reveal-armed')).toBe(false);
  });
});
