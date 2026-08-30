import { expect, test } from '@playwright/test';
import {
  API,
  findSyntheticPartUnit,
  getSession,
  listPracticeUnits,
  registerLearner,
  showPracticeQuestions,
  signIn,
  startFullTest,
  startPracticeUnit,
  storedSession,
} from './harness';

const READING_Q1 = /What did the survey team measure/;

/**
 * The situations that only exist because a browser is a concurrent machine.
 *
 * Everything here has a counterpart in the component suite that passes, and
 * passes against a world where requests arrive in the order they were sent,
 * one tab exists, and a promise that resolves is a promise that mattered. None
 * of those three is true of a phone on a train.
 */
test.describe('races', () => {
  /**
   * Two autosaves delivered out of order.
   *
   * <b>The one the ordering tokens in `useAnswerSheet.ts` exist for.</b> A
   * learner corrects an answer; the correction is sent second and arrives
   * first, because the first request went out over a connection that stalled.
   * Whichever request the server writes last wins unless something carries the
   * learner's own order — and what they typed last is what they meant.
   *
   * The failure is silent: both saves succeed, the chip says saved, and the
   * answer marked at the end of the section is the one they rejected.
   */
  test('the answer typed last wins, whatever order the saves arrive in', async ({
    page,
    request,
  }) => {
    const learner = await registerLearner(request);
    const sitting = await startFullTest(request, learner.session.accessToken);

    await signIn(page, learner, `/students/session/${sitting.sessionId}`);

    const answer = page.getByRole('textbox', { name: READING_Q1 });
    await expect(answer).toBeVisible();

    /*
     * Hold the first autosave on the wire and let everything after it through.
     * That is exactly the shape of a stalled connection: not a failure, not a
     * timeout, just late.
     */
    let held = 0;
    await page.route(`${API}/api/v1/sessions/*/answers`, async (route) => {
      held += 1;
      if (held === 1) await new Promise((resolve) => setTimeout(resolve, 4_000));
      await route.continue();
    });

    await answer.fill('the wrong one');
    // Long enough for the debounce to fire and that first request to be held.
    await page.waitForTimeout(2_000);

    await answer.fill('river depth');

    await expect(page.locator('.save-chip')).toHaveClass(/is-saved/, { timeout: 20_000 });

    const stored = await getSession(request, learner.session.accessToken, sitting.sessionId);

    expect(
      stored.current.answers['syn-r-1'],
      'The earlier answer overwrote the later one — the learner’s correction was lost ' +
        'while both saves reported success.',
    ).toBe('river depth');
  });

  test('on a practice sitting the answer typed last wins when saves arrive out of order', async ({
    page,
    request,
  }) => {
    const learner = await registerLearner(request);
    const units = await listPracticeUnits(request, learner.session.accessToken, {
      skill: 'reading',
      scope: 'part',
    });
    const unit = findSyntheticPartUnit(units, 'reading');
    const sitting = await startPracticeUnit(request, learner.session.accessToken, unit.id);

    await signIn(page, learner, `/students/practice/${sitting.sessionId}`);
    await showPracticeQuestions(page);

    const answer = page.getByRole('textbox', { name: READING_Q1 });
    await expect(answer).toBeVisible();

    let held = 0;
    await page.route(`${API}/api/v1/sessions/*/answers`, async (route) => {
      held += 1;
      if (held === 1) await new Promise((resolve) => setTimeout(resolve, 4_000));
      await route.continue();
    });

    await answer.fill('the wrong one');
    await page.waitForTimeout(2_000);
    await answer.fill('river depth');

    await expect(page.locator('.save-chip')).toHaveClass(/is-saved/, { timeout: 20_000 });

    const stored = await getSession(request, learner.session.accessToken, sitting.sessionId);
    expect(stored.current.answers['syn-r-1']).toBe('river depth');
  });

  /**
   * Two tabs of the same sitting, both hitting an expired token at once.
   *
   * <b>What `packages/auth/src/coordinator.ts` was written for, and it had never
   * been run in a browser.</b> Refresh tokens rotate and reuse is treated as
   * theft: if both tabs redeem the same token, the second redemption looks like
   * a stolen one, the family is revoked, and the learner is signed out of a
   * timed exam by their own second tab.
   *
   * <b>Why a component test cannot reach it.</b> The coordinator's cross-tab
   * half is `navigator.locks` and `BroadcastChannel`, and neither exists between
   * two `jsdom` instances. The unit suite exercises the single-tab promise; this
   * is the only place the other half runs at all.
   *
   * ── Measured 2026-08-28: there are two defences, not one ─────────────────
   *
   * The first version of this test asserted only that neither tab was signed
   * out — and it <b>passed with the coordinator's adopt-inside-the-lock deleted</b>.
   * That is not a flaw in the product; it is a fact worth knowing. Without the
   * coordinator both tabs really do refresh, and the second presents a token
   * that has already been rotated — which the <i>server</i> recognises as the
   * lost-response case through `successorTokenHash` and answers with the
   * successor rather than revoking the family.
   *
   * So the browser stops the duplicate and the server survives it. Belt and
   * braces, and neither had been observed until now.
   *
   * <b>The count is therefore the assertion that means something.</b> "Nobody
   * was signed out" is true either way; "one rotation, not two" is true only
   * when the coordinator is doing its job, and it goes red the moment it is not.
   */
  test('two tabs hitting an expired token produce one rotation, not two', async ({
    context,
    request,
  }) => {
    const learner = await registerLearner(request);
    const sitting = await startFullTest(request, learner.session.accessToken);

    const rotations: number[] = [];
    context.on('response', (response) => {
      const url = new URL(response.url());
      if (url.origin === API && url.pathname === '/api/v1/auth/refresh') {
        rotations.push(response.status());
      }
    });

    const first = await context.newPage();
    await signIn(first, learner, `/students/session/${sitting.sessionId}`);
    await expect(first.getByRole('textbox', { name: READING_Q1 })).toBeVisible();

    const second = await context.newPage();
    await second.goto(`/students/session/${sitting.sessionId}`);
    await expect(second.getByRole('textbox', { name: READING_Q1 })).toBeVisible();

    /*
     * <b>One 401 per tab, on the autosave.</b>
     *
     * The obvious way to force this — overwrite the access token in
     * `localStorage` — does nothing at all, measured: the running app holds its
     * session in memory, so it keeps using the token it already had and no
     * renewal is ever attempted. A test written that way passes while proving
     * nothing, which is what the first draft of this one did.
     *
     * A 401 from the API is what an expired access token actually looks like
     * from the client's side, and it is the only thing that puts both tabs into
     * the renewal path at once.
     */
    const expireOnce = async (page: typeof first) => {
      let fired = false;
      await page.route(`${API}/api/v1/sessions/*/answers`, async (route) => {
        if (fired) return route.continue();
        fired = true;
        await route.fulfill({
          status: 401,
          contentType: 'application/problem+json',
          body: JSON.stringify({
            title: 'Unauthorized',
            status: 401,
            detail: 'The access token has expired.',
            code: 'UNAUTHORIZED',
          }),
        });
      });
    };

    await expireOnce(first);
    await expireOnce(second);

    await Promise.all([
      first.getByRole('textbox', { name: READING_Q1 }).fill('river depth'),
      second.getByRole('textbox', { name: READING_Q1 }).fill('river depth'),
    ]);

    await expect(first.locator('.save-chip')).toHaveClass(/is-saved/, { timeout: 20_000 });
    await expect(second.locator('.save-chip')).toHaveClass(/is-saved/, { timeout: 20_000 });

    expect(
      rotations,
      'Each tab redeemed the refresh token for itself. The server survives that ' +
        'through lost-response recovery, but the coordinator is what is supposed ' +
        'to stop it happening — and with more tabs, or a slower server, the ' +
        'second redemption is the one that reads as theft.',
    ).toEqual([200]);

    for (const [name, page] of [
      ['first', first],
      ['second', second],
    ] as const) {
      expect(new URL(page.url()).pathname, `The ${name} tab was signed out mid-sitting.`).not.toBe(
        '/login',
      );
    }

    // Both are holding the same credential rather than two rival ones — the
    // adopt-rather-than-rotate half of the coordinator.
    const held = await Promise.all([storedSession(first), storedSession(second)]);

    expect(held[0]?.refreshToken).toBeTruthy();
    expect(held[0]?.refreshToken).toBe(held[1]?.refreshToken);

    const stored = await getSession(request, learner.session.accessToken, sitting.sessionId);
    expect(stored.current.answers['syn-r-1']).toBe('river depth');
  });
});
