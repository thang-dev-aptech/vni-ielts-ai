import { expect, test } from '@playwright/test';
import { registerLearner, signIn, startFullTest } from './harness';

/**
 * The harness itself, checked before anything relies on it.
 *
 * <b>A suite whose fixtures are broken fails in the shape of the thing it was
 * testing.</b> An unseeded catalogue, a CORS origin nobody added, a session
 * written to the wrong origin — each of those surfaces as "the exam runner did
 * not appear", which reads like a product defect and is not one. This test
 * exists so that failure has its own name.
 */
test('the app loads, a learner signs in, and a Full Test opens on Reading', async ({
  page,
  request,
}) => {
  const learner = await registerLearner(request);

  const sitting = await startFullTest(request, learner.session.accessToken);
  expect(sitting.current.module).toBe('reading');

  await signIn(page, learner, `/students/session/${sitting.sessionId}`);

  // The passage and its first question, drawn from the paper the API served.
  await expect(page.getByText('Surveying the Lower Delta')).toBeVisible();
});
