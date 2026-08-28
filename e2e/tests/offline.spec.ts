import { expect, test } from '@playwright/test';
import { getSession, registerLearner, signIn, startFullTest } from './harness';

/**
 * A learner whose network goes away mid-answer.
 *
 * <b>This is the ordinary case, not the edge case.</b> Most of these sittings
 * will happen on a phone, and a phone loses its connection in a lift, on a
 * train, and every time a Vietnamese apartment block's wifi drops for ten
 * seconds. The product's answer is `patchJournal.ts` plus the retry ladder in
 * `useAnswerSheet.ts` — both written in August 2026, both never once run in a
 * browser.
 *
 * <b>What no other test could reach.</b> `jsdom` has no network stack to take
 * away: the component suite fakes a rejected promise, which exercises the error
 * branch and nothing about what the browser does with an in-flight request,
 * `navigator.onLine`, or an IndexedDB write racing a page teardown.
 */
test.describe('offline', () => {
  test('an answer typed offline is saved when the connection returns', async ({
    page,
    context,
    request,
  }) => {
    const learner = await registerLearner(request);
    const sitting = await startFullTest(request, learner.session.accessToken);

    await signIn(page, learner, `/students/session/${sitting.sessionId}`);

    const answer = page.getByRole('textbox', { name: /What did the survey team measure/ });
    await expect(answer).toBeVisible();

    await context.setOffline(true);
    await answer.fill('river depth');

    /*
     * <b>Asserted on the state class, not on the words.</b> The chip's copy is
     * translated and the app decides the language at runtime — this suite gets
     * English, the screenshots in the design get Vietnamese. A test written
     * against either one is a test that breaks when somebody improves the
     * wording, which is the change least likely to be a defect.
     *
     * The class is the state machine, and the state machine is the contract:
     * product law L2 says only `is-saved` may carry a tick, because a learner
     * who sees a tick stops checking, and a tick over work still sitting on the
     * device is data loss the interface caused.
     */
    const chip = page.locator('.save-chip');

    await expect(chip).toHaveClass(/is-queued|is-pending|is-sending/, { timeout: 10_000 });
    await expect(chip).not.toHaveClass(/is-saved/);

    await context.setOffline(false);

    // The ladder's first rung is a second, with jitter. Fifteen is generous
    // and still far short of a hang.
    await expect(chip).toHaveClass(/is-saved/, { timeout: 15_000 });

    const stored = await getSession(request, learner.session.accessToken, sitting.sessionId);
    expect(stored.current.answers['syn-r-1']).toBe('river depth');
  });

  test('an answer typed offline survives a reload that happens before it is sent', async ({
    page,
    context,
    request,
  }) => {
    /*
     * <b>The one `patchJournal` exists for, and the only place it can be
     * proved.</b>
     *
     * Everything the retry ladder holds lives in React state. A reload throws
     * that away — and a learner whose connection dropped is exactly the learner
     * who reloads, because a page that has stopped responding is the symptom
     * they can see. Without a journal on disk, the answer they typed is gone and
     * nothing ever told them.
     *
     * A component test cannot reach this: unmounting a hook in `jsdom` is not a
     * reload, and the whole question is whether the write reached IndexedDB
     * before the document went away.
     */
    const learner = await registerLearner(request);
    const sitting = await startFullTest(request, learner.session.accessToken);

    await signIn(page, learner, `/students/session/${sitting.sessionId}`);

    const answer = page.getByRole('textbox', { name: /What did the survey team measure/ });
    await expect(answer).toBeVisible();

    await context.setOffline(true);
    await answer.fill('river depth');

    // Long enough for the debounce to fire and the send to fail, so the answer
    // is genuinely unsent rather than merely untyped.
    await expect(page.locator('.save-chip')).toHaveClass(/is-queued|is-pending/, {
      timeout: 10_000,
    });

    /*
     * <b>Back online *before* the reload, and that is the honest sequence
     * rather than a convenience.</b> A reload with no network cannot fetch the
     * document at all — the browser shows its own error page, and what is being
     * tested here is the app, not Chromium's offline screen.
     *
     * The situation this models is the real one: the page stopped responding,
     * the learner reloaded, and by the time it came back the connection had
     * returned. The answer was never sent before that reload, so the only thing
     * that can still carry it is the journal on disk.
     */
    await context.setOffline(false);
    await page.reload();

    const reloaded = page.getByRole('textbox', { name: /What did the survey team measure/ });
    await expect(reloaded).toBeVisible();

    await expect
      .poll(
        async () => {
          const stored = await getSession(request, learner.session.accessToken, sitting.sessionId);
          return stored.current.answers['syn-r-1'] ?? null;
        },
        {
          timeout: 30_000,
          message:
            'The answer typed offline never reached the server after a reload. ' +
            'That is the failure patchJournal.ts was written to prevent.',
        },
      )
      .toBe('river depth');
  });
});
