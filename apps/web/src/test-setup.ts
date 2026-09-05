import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';

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

/*
 * Testing Library's own async budget, which `testTimeout` does not govern.
 *
 * `findBy*` and `waitFor` default to one second. Every test in this suite
 * mounts a whole `<App/>` under StrictMode — which double-invokes each mount
 * effect — and then waits on a session restore plus at least one fetch before
 * anything it asserts on exists. One second is the budget for a component
 * test, not for that.
 *
 * The failures this produced were the expensive kind: a different two or three
 * files each run, always ones awaiting a fetch, never reproducible alone. That
 * reads as a real defect and is not one, and a suite that cries wolf teaches
 * people to re-run instead of to look.
 *
 * <b>Never restate this as a per-call `{ timeout: n }`.</b> A local number
 * overrides the global one rather than adding to it, so three files that each
 * carried their own literal were the only three the shared budget could not
 * reach — and they were the three that kept failing. If a wait needs longer
 * than everything else, that is worth a comment saying why, not a silent
 * duplicate of a number that lives here.
 *
 * <b>Sized for a loaded machine, not for this one.</b> On a quiet twelve-core
 * box every one of these settles in a couple of hundred milliseconds, so the
 * budget is never approached; it exists so that a laptop running a build, or a
 * CI box sharing a host, fails a genuinely stuck test rather than a merely
 * starved one. → the pool note in `vitest.config.ts`
 *
 * 8 seconds, not 5: the dictation library file takes 9.6s on its own with
 * the machine idle, so under a full-suite thread it routinely needed more
 * than five. It still sits well below the 15s `testTimeout`.
 *
 * A passing assertion still settles immediately — this only changes how long a
 * genuinely stuck one takes to admit it, and it stays well under the 15s
 * `testTimeout` so a real hang still fails the test rather than the file.
 */
configure({ asyncUtilTimeout: 8_000 });

/*
 * ── A render crash must fail the test that caused it ─────────────────────
 *
 * <b>Written 2026-08-27, after the suite passed 199/199 while an app was
 * crashing inside it.</b>
 *
 * A token test's mock answered `/me/sessions` with `{ sessions: [] }` where the
 * client expects `{ sittings: [...] }`. The dashboard read `sittings.length`
 * off `undefined`, React unwound to `ErrorBoundary`, the boundary rendered its
 * apology — and the assertions, which were about tokens, went on passing. The
 * only trace was a stderr block nobody reads in a green run.
 *
 * That is precisely the failure this whole codebase has been chasing from the
 * other direction: two halves that each pass while the thing between them is
 * broken. A suite that tolerates a crashed render is not a release gate, it is
 * a mood.
 *
 * <b>Deliberately an `afterEach` throw rather than a `console.error` spy.</b>
 * Spying swallows the message, so the failure arrives with no stack and the
 * next reader has to reproduce it to learn anything. This lets React print
 * everything it wants, remembers that it did, and then fails the test by name.
 *
 * A test that *means* to render an error path opts out with
 * `expectRenderError()`, which is one line and self-documenting — unlike a
 * global mute, which is one line and hides everything after it.
 */
/*
 * ── No test may reach a real server ──────────────────────────────────────
 *
 * <b>Written 2026-08-27, after three rounds of "the machine is flaky".</b>
 *
 * The suite failed one or two tests per full run, never the same ones, always
 * ones awaiting a fetch, every one passing in isolation. Twice that was
 * diagnosed as CPU contention and twice the diagnosis was wrong. The cause,
 * traced with a stack, is that <b>the tests were talking to the API running on
 * this machine</b>, and it answered.
 *
 * Two things combine to open the hole:
 *
 * <b>1 · `vi.unstubAllGlobals()` restores the real `fetch` before the tree is
 * unmounted.</b> Vitest runs `afterEach` in reverse registration order, so a
 * test file's own `afterEach` runs BEFORE Testing Library's cleanup — which is
 * registered here, in a setup file, and therefore last. Between those two
 * moments the app is still mounted, still loading, and no longer mocked.
 *
 * <b>2 · A 401 from that leaked request renews the token of whatever session is
 * current.</b> `renewOnce()` is a module singleton with no memory of which
 * session issued the request. So a stray call from the previous test got a
 * real 401 from `localhost:5099`, the transport asked the *next* test's
 * provider to refresh, that test's mock had no `/auth/refresh` route, and the
 * refusal signed out a learner who had just been signed in. What it looked
 * like from the report: a dashboard test asserting against the sign-in page.
 *
 * The second of those is a product defect, not a test defect — a stale request
 * must not be able to end a live session — and it is tracked as its own task.
 * This gate closes the first, and it closes it in the one place that cannot be
 * forgotten by a new test file.
 *
 * <b>Installed in `beforeEach`, and that placement is the whole mechanism.</b>
 * `beforeEach` runs in registration order, so this lands before any test's
 * `vi.stubGlobal('fetch', …)`. `vi.stubGlobal` remembers the value it replaced,
 * so `vi.unstubAllGlobals()` restores <i>this guard</i> rather than the real
 * `fetch` — which shuts the window in (1) without any test file changing.
 *
 * <b>It rejects with a `TypeError`, deliberately.</b> That is the shape a real
 * `fetch` gives for "no connection", and `isUnreachable()` already treats it as
 * unreachable — so a request that escapes a test degrades down the app's
 * offline path instead of down its refusal path. A refusal path deletes
 * credentials; that is precisely the behaviour that made this flake destructive
 * rather than merely noisy.
 */
const unmockedCalls: string[] = [];
let allowUnmockedFetch = false;

/** For a test whose subject IS an unmocked or failing transport. */
export function expectUnmockedFetch(): void {
  allowUnmockedFetch = true;
}

function guardFetch(): typeof fetch {
  return (async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : ((input as Request).url ?? String(input));
    unmockedCalls.push(url);
    throw new TypeError(
      `No test stub answered ${url}. The suite must never reach a real server; ` +
        'stub `fetch` for this call, or call `expectUnmockedFetch()` if the ' +
        'unreachable transport is what the test is about.',
    );
  }) as typeof fetch;
}

beforeEach(() => {
  unmockedCalls.length = 0;
  allowUnmockedFetch = false;
  globalThis.fetch = guardFetch();
});

let renderErrors: string[] = [];
let allowRenderErrors = false;

/** For a test whose subject IS the error path. Scoped to that test only. */
export function expectRenderError(): void {
  allowRenderErrors = true;
}

/** Substitutes React's `%s` placeholders so a recorded line names its component. */
function format(args: unknown[]): string {
  const say = (a: unknown) => (a instanceof Error ? a.message : String(a));
  const [first, ...rest] = args;

  if (typeof first !== 'string' || !first.includes('%s')) return args.map(say).join(' ');

  let next = 0;
  const filled = first.replace(/%s/g, () => (next < rest.length ? say(rest[next++]) : '%s'));
  return [filled, ...rest.slice(next).map(say)].join(' ');
}

const realConsoleError = console.error;

beforeEach(() => {
  renderErrors = [];
  allowRenderErrors = false;

  console.error = (...args: unknown[]) => {
    /*
     * React formats with `%s`, so the component's name arrives as a separate
     * argument. Joining without substituting produced "An update to %s inside
     * a test…", which names nothing and sends the reader back to the raw
     * stderr to find out which component it was.
     */
    const text = format(args);

    /*
     * Only the shapes that mean "the app broke", not every console.error.
     *
     * React reports a caught render error twice — once as the error and once
     * as the boundary notice — and a DOM-nesting violation is a real defect
     * that renders anyway. Everything else stays a message: failing on all
     * console.error would make this a lint rule with a worse error message,
     * and the first noisy third-party warning would get it disabled.
     */
    if (
      text.includes('Unhandled render error') ||
      text.includes('error occurred in the') ||
      text.includes('cannot contain a nested') ||
      text.includes('validateDOMNesting')
    ) {
      renderErrors.push(text.split('\n')[0] ?? text);
    }

    /*
     * <b>And an update outside `act` is a defect too, from 2026-08-27.</b>
     *
     * This was 21 warnings a run, from eight tests, and every one of them was
     * a test that moved the clock or yielded through a bare `setTimeout` while
     * the app had work in flight. It reads as noise, which is why it survived
     * three rounds of tidying.
     *
     * It is not noise. Outside an `act` scope React does not flush effects on
     * the test's schedule, so what the next assertion sees depends on timing
     * rather than on the code — which is the same shape as the flake that had
     * this suite failing a different test every run. A warning nobody can act
     * on also trains people to scroll past the ones that matter, and the
     * render-crash gate immediately above exists because exactly that
     * happened.
     */
    if (text.includes('was not wrapped in act')) {
      renderErrors.push(`React state update outside act(...): ${text.split('\n')[0] ?? text}`);
    }

    realConsoleError(...args);
  };
});

afterEach(() => {
  console.error = realConsoleError;

  /*
   * ── Judge first, unmount second, throw last ──────────────────────────────
   *
   * <b>The order below is three lessons, each paid for.</b>
   *
   * <b>Judge before unmounting</b>, because the boundary detector reads the DOM
   * and the network detector must not count what unmounting itself does. A
   * component tearing down can start a request — and by this point the test
   * file's own `afterEach` has already run `vi.unstubAllGlobals()`, so that
   * request meets the guard. Counting it would fail a test for finishing.
   *
   * <b>Unmount before throwing</b>, because Testing Library registers its
   * cleanup when it is imported — here, in a setup file — and Vitest runs
   * `afterEach` in reverse registration order, so that cleanup is the LAST hook
   * to run. A gate that throws never reaches it, and the tree stays mounted
   * into the next test: two `<App/>`s in one document, two routers, and a query
   * that resolves against the dead one. That produced a second failure with
   * nothing wrong with it — an autosave that never fired, because the field
   * being typed into belonged to a tree nobody was rendering any more. One
   * failing test must not manufacture the next.
   *
   * <b>`cleanup()` is idempotent</b>, so Testing Library's own call afterwards
   * is a no-op.
   */
  if (!allowRenderErrors && document.querySelector('[data-error-boundary]') !== null) {
    renderErrors.push('ErrorBoundary rendered its apology — a component threw during render.');
  }

  const unmocked = allowUnmockedFetch ? [] : [...new Set(unmockedCalls)];
  const crashes = allowRenderErrors ? [] : [...new Set(renderErrors)];

  if (unmocked.length > 0 || crashes.length > 0) cleanup();

  unmockedCalls.length = 0;
  renderErrors = [];

  if (unmocked.length > 0) {
    throw new Error(
      'This test reached for a URL no stub answered. Before this gate existed ' +
        'that call went to whatever was listening on localhost, and a real 401 ' +
        'from it signed out the next test that ran.\n\n' +
        unmocked.map((url) => `  \u00b7 ${url}`).join('\n') +
        '\n\nStub it, or call `expectUnmockedFetch()` if an unreachable ' +
        'transport is what this test is about.',
    );
  }

  if (crashes.length === 0) return;

  throw new Error(
    'The app crashed, rendered invalid DOM, or updated state outside act(...) ' +
      'during this test. A green assertion over a crashed render — or over a ' +
      'render React never flushed on schedule — is how a suite stops being a ' +
      'release gate.\n\n' +
      crashes.map((line) => `  \u00b7 ${line}`).join('\n') +
      '\n\nIf this test is *about* the error path, call `expectRenderError()` in it.',
  );
});
