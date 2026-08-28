import { expect, test } from '@playwright/test';
import { API, getSession, registerLearner, signIn, startFullTest } from './harness';

const READING_Q1 = /What did the survey team measure/;
/**
 * `syn-r-2` is True/False/Not Given, so it is a radio group rather than a
 * textbox — and the radios carry no accessible name of their own, only a value.
 * Targeting the value is what works; `getByRole('radio', { name: 'FALSE' })`
 * finds nothing, which reads as "the question is missing".
 */
const falseRadio = 'input[type="radio"][value="FALSE"]';

const clockSeconds = (text: string | null) => {
  const [minutes, seconds] = (text ?? '0:0').split(':').map(Number);
  return (minutes ?? 0) * 60 + (seconds ?? 0);
};

test.describe('resilience', () => {
  /**
   * The lid closes, the phone locks, the tab is frozen — and the clock is still
   * right when it comes back.
   *
   * <b>The failure this guards against is a plausible optimisation.</b> A
   * countdown that decrements a number once a second is the obvious way to draw
   * an exam clock, and it is wrong in a way nothing on a developer's desk would
   * show: a browser throttles or stops timers in a background tab, so the number
   * comes back <i>behind</i> real time. The learner sees minutes they do not
   * have, keeps writing, and the server — correctly — closes the section under
   * them. From their side the exam stole their time.
   *
   * <b>Measured 2026-08-28: it does not drift</b>, because every tick recomputes
   * from `deadlineAt` rather than decrementing. This test exists to keep it that
   * way, and it is the only place that can check it — `jsdom` has no lifecycle
   * state to freeze.
   */
  test('the exam clock is still the server’s after the tab is frozen and woken', async ({
    page,
    context,
    request,
  }) => {
    const learner = await registerLearner(request);
    const sitting = await startFullTest(request, learner.session.accessToken);

    await signIn(page, learner, `/students/session/${sitting.sessionId}`);
    await expect(page.getByRole('textbox', { name: READING_Q1 })).toBeVisible();

    const clock = page.locator('.exam-clock .num');
    await expect(clock).not.toHaveText('--:--');

    // Chromium's own suspension, not a simulation of one.
    const cdp = await context.newCDPSession(page);
    await cdp.send('Page.setWebLifecycleState', { state: 'frozen' });
    await page.waitForTimeout(12_000);
    await cdp.send('Page.setWebLifecycleState', { state: 'active' });

    // One tick to redraw.
    await page.waitForTimeout(1_500);

    const shown = clockSeconds(await clock.textContent());
    const server = (await getSession(request, learner.session.accessToken, sitting.sessionId))
      .current.remainingSeconds as number;

    /*
     * Three seconds of slack covers the round trip and the tick boundary, and
     * is far tighter than the twelve a frozen countdown would have lost. The
     * sign matters as much as the size: a clock that is *ahead* of the server is
     * the one that costs a learner an answer.
     */
    expect(
      Math.abs(shown - server),
      `The clock says ${shown}s and the server says ${server}s. A clock that survives ` +
        'a freeze only by luck will show a learner time they do not have.',
    ).toBeLessThanOrEqual(3);
  });

  /**
   * A connection slow enough that saves overlap.
   *
   * <b>What breaks here is not the network but the queue.</b> Under a fast
   * connection each autosave finishes before the next is scheduled, so a hook
   * that kept no queue at all would look correct forever. Add three seconds of
   * latency and the learner types two more answers while the first save is
   * still on the wire — and every question about ordering, about overlapping
   * requests, and about what the chip is allowed to claim becomes live at once.
   *
   * This is the ordinary Vietnamese mobile connection on a bad day, not a
   * pathological case.
   */
  test('nothing is lost when the learner types faster than the network answers', async ({
    page,
    request,
  }) => {
    const learner = await registerLearner(request);
    const sitting = await startFullTest(request, learner.session.accessToken);

    await signIn(page, learner, `/students/session/${sitting.sessionId}`);
    await expect(page.getByRole('textbox', { name: READING_Q1 })).toBeVisible();

    let inFlight = 0;
    let overlapped = 0;

    await page.route(`${API}/api/v1/sessions/*/answers`, async (route) => {
      inFlight += 1;
      if (inFlight > 1) overlapped += 1;
      await new Promise((resolve) => setTimeout(resolve, 3_000));
      await route.continue();
      inFlight -= 1;
    });

    await page.getByRole('textbox', { name: READING_Q1 }).fill('river depth');

    // Inside the 1.2 s debounce window, so the second edit lands while the
    // first save is still on the wire. That overlap is the whole test.
    await page.waitForTimeout(1_500);
    await page.locator(falseRadio).check();

    await expect(page.locator('.save-chip')).toHaveClass(/is-saved/, { timeout: 40_000 });

    const stored = await getSession(request, learner.session.accessToken, sitting.sessionId);

    expect(stored.current.answers['syn-r-1']).toBe('river depth');
    expect(stored.current.answers['syn-r-2']).toBe('FALSE');

    /*
     * <b>And only one save was ever on the wire.</b> The hook keeps a single
     * request in flight and folds later edits into the next one — which is what
     * makes ordering tractable at all. Two overlapping saves of the same sheet
     * is the shape of the bug where an older draft lands last.
     */
    expect(
      overlapped,
      'Two autosaves were in flight at once. Whichever the server writes last wins, ' +
        'and it is not necessarily the one the learner typed last.',
    ).toBe(0);
  });
});
