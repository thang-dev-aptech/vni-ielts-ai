import { expect, it } from 'vitest';
import { getConfig, waitFor } from '@testing-library/react';
import {
  ASYNC_UTIL_TIMEOUT_MS,
  BUDGETED_WAITS_PER_TEST,
  TEST_TIMEOUT_MS,
} from '../test-timeouts.js';

/**
 * The ruler, measured.
 *
 * <b>Written 2026-09-18.</b> This project has exactly one evidence mechanism —
 * <i>"a test was seen to go red when the fix was removed"</i> — and for months
 * most of this suite's red runs said only `Test timed out in 15000ms`. A red
 * with no name cannot tell you whether the fix is gone or the machine was busy,
 * which makes every other test in the repository worth less than it reads.
 *
 * The cause was arithmetic between two numbers nobody had compared:
 * `asyncUtilTimeout` was 8s and `testTimeout` was 15s, so any test that spent
 * one full async budget on a wait that <i>did</i> settle could never reach the
 * deadline of the next one. Testing Library's error — the one that names the
 * element, the query and the DOM — was never thrown; vitest's, which names
 * nothing, was. Over 12 full runs, <b>16 of 24 failures arrived unreadable.</b>
 *
 * These tests are the guard on that arithmetic. They are cheap on purpose: the
 * honest full-scale demonstration costs 16 seconds of wall clock on every run,
 * for ever, and a tax that size on an 85-second suite is how a guard gets
 * deleted. So the numbers are checked exactly, and the mechanism they exist for
 * is demonstrated at 1/16 scale.
 */

/** Vitest's effective per-test deadline, as the running worker actually has it. */
function effectiveTestTimeout(): number {
  const worker = (
    globalThis as unknown as { __vitest_worker__?: { config?: { testTimeout?: number } } }
  ).__vitest_worker__;
  const configured = worker?.config?.testTimeout;
  expect(typeof configured).toBe('number');
  return configured as number;
}

it('runs on the budgets `test-timeouts.ts` declares, not on a literal somewhere else', () => {
  // Both of these were free-standing literals in two different files until
  // 18/09/2026. The check is that the derived numbers are the ones in force —
  // a `vitest.config.ts` edited back to a literal fails here.
  expect(getConfig().asyncUtilTimeout).toBe(ASYNC_UTIL_TIMEOUT_MS);
  expect(effectiveTestTimeout()).toBe(TEST_TIMEOUT_MS);
});

it('leaves a stuck wait room to report after the budgeted ones have been spent', () => {
  /*
   * The relationship, stated against the values actually in force rather than
   * against the constants — so neither a config edit nor a stray `configure()`
   * in another setup file can satisfy it on paper while breaking it in fact.
   *
   * <b>Strictly greater.</b> Equal means the stuck wait's deadline lands on the
   * same millisecond as the test's, and which error surfaces is then a race.
   */
  expect(effectiveTestTimeout()).toBeGreaterThan(
    BUDGETED_WAITS_PER_TEST * getConfig().asyncUtilTimeout,
  );
});

it('keeps the name of what a wait was looking for, even when a slow wait ran first', async () => {
  /*
   * <b>The one place in this suite where a per-call `{ timeout }` is correct.</b>
   * Everywhere else it silently overrides the shared budget — see the note in
   * `test-setup.ts`. Here the budget <i>is</i> the subject, so it is written
   * down, at 1/16 of the shipped size so the demonstration costs ~1s instead of
   * ~16s.
   */
  const budget = 500;
  const started = Date.now();

  // A wait that settles, but only near the end of its budget — the shape that
  // used to eat the test's whole deadline before the next wait began.
  await waitFor(() => expect(Date.now() - started).toBeGreaterThan(budget * 0.8), {
    timeout: budget,
    interval: 10,
  });

  // And now one that never settles. What matters is not that it fails — it is
  // what the failure is able to say.
  const failure = await waitFor(() => expect('nothing here').toBe('LOOKING FOR THE WIDGET'), {
    timeout: budget,
    interval: 10,
  }).then(
    () => null,
    (error: unknown) => error as Error,
  );

  expect(failure).not.toBeNull();
  expect(failure?.message).toContain('LOOKING FOR THE WIDGET');

  // Both budgets were spent inside one test, which is only possible because the
  // test deadline is a multiple of the wait budget rather than 1.875 times it.
  expect(Date.now() - started).toBeGreaterThan(budget * 1.5);
});
