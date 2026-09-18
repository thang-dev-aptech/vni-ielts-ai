/**
 * The two async budgets this suite runs on — in one file, because they are one
 * decision and not two.
 *
 * <b>Written 2026-09-18, after they drifted into each other.</b> `testTimeout`
 * lived in `vitest.config.ts` at 15s and `asyncUtilTimeout` lived in
 * `test-setup.ts` at 8s, each with a long note explaining itself and neither
 * mentioning the other. Two numbers nobody compared, and the comparison is the
 * whole point: `8000 × 2 > 15000`, so a test that spent one full async budget
 * on a wait that <i>did</i> settle could never reach the deadline of its next
 * one. The test timeout fired first, and the test timeout knows nothing about
 * what the test was waiting for.
 *
 * What that cost, measured over 12 full runs: <b>16 of 24 failures arrived as a
 * bare "Test timed out in 15000ms"</b> with no element, no query and no
 * message. A red run that cannot be read is worse than a slow one, because the
 * only evidence rule this project has is "a test was seen to go red when the
 * fix was removed" — and a nameless red cannot tell you which of the two it is.
 *
 * So the relationship is the requirement, and it is expressed as arithmetic
 * rather than as two literals that happen to be compatible today.
 */

/**
 * Testing Library's own budget for one `waitFor` / `findBy*`.
 *
 * <b>8 seconds, and the size is measured, not chosen.</b> Every test here
 * mounts a whole `<App/>` under StrictMode — which double-invokes each mount
 * effect — then waits on a session restore plus at least one fetch before
 * anything it asserts on exists. `dictation-library` takes 9.6s on its own with
 * the machine idle. One second, Testing Library's default, is the budget for a
 * component test and not for that.
 *
 * <b>Never restate this as a per-call `{ timeout: n }`.</b> A local number
 * overrides the global one rather than adding to it, so three files that each
 * carried their own literal were the only three the shared budget could not
 * reach — and they were the three that kept failing. A wait that genuinely
 * needs longer than everything else deserves a comment saying why, not a silent
 * duplicate of a number that lives here.
 */
export const ASYNC_UTIL_TIMEOUT_MS = 8_000;

/**
 * How many fully-consumed async waits one test may spend and still be told, by
 * name, what the wait after them was looking for.
 *
 * Three, not two. Two is the minimum that fixes the reported symptom — one slow
 * wait followed by a stuck one — and a minimum leaves no room for the test that
 * has a slow wait, a retry, and then the stuck one. `src/__tests__` averages
 * 1.7 waits per `it()`, so three covers the shape of this suite with the
 * pathological case in hand rather than assumed away.
 */
export const BUDGETED_WAITS_PER_TEST = 3;

/**
 * Time above the budgeted waits, for the render, the typing and the fetches
 * that sit between them. A test that has burned three full async budgets is
 * already failing; this only decides whether it fails with a name.
 */
export const TEST_TIMEOUT_SLACK_MS = 6_000;

/**
 * The per-test deadline. Derived, so it cannot fall below the relationship.
 *
 * Raising it does not slow a passing run down — a wait that settles still
 * settles immediately. It only changes how long a genuinely stuck test takes to
 * admit it, and what it is able to say when it does.
 */
export const TEST_TIMEOUT_MS =
  ASYNC_UTIL_TIMEOUT_MS * BUDGETED_WAITS_PER_TEST + TEST_TIMEOUT_SLACK_MS;
