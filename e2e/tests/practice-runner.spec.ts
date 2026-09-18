import { expect, test, type Page } from '@playwright/test';
import {
  API,
  assertNoPreSubmitLeaks,
  findSyntheticPartUnit,
  getSession,
  listPracticeUnits,
  recordPreSubmitResponses,
  registerLearner,
  showPracticeQuestions,
  signIn,
  startPracticeUnit,
} from './harness';

const READING_PASSAGE = /Surveying the Lower Delta/;
const LISTENING_TITLE = /Workshop enquiry/;

async function fillReadingPart1(page: Page) {
  await showPracticeQuestions(page);
  await page.getByRole('textbox', { name: /What did the survey team measure/ }).fill('river depth');
  await page.locator('input[type="radio"][value="FALSE"]').check();
  await page.getByRole('radio', { name: /A.*instruments needed recalibrating/i }).check();

  const bank = page.getByRole('list', { name: /Ngân hàng đáp án|Answer bank/i });
  await bank.getByRole('button', { name: /B.*flow meter/i }).click();
  await page.getByRole('button', { name: /The reading taken at dawn/i }).click();
}

async function fillListeningPart1(page: Page) {
  // Completion gaps are named by blank number in the group text, not the prompt.
  await page.getByRole('textbox', { name: '1', exact: true }).fill('9:30');
  await page.getByRole('textbox', { name: '2', exact: true }).fill('three');
  await page.getByRole('checkbox', { name: /A.*notebook/i }).check();
  await page.getByRole('checkbox', { name: /C.*packed lunch/i }).check();
  await page.getByRole('textbox', { name: /contact to cancel/i }).fill('registrar');
}

async function waitForSaved(page: Page) {
  await expect(page.locator('.save-chip')).toHaveClass(/is-saved/, { timeout: 20_000 });
}

async function submitPractice(page: Page, sessionId: string) {
  await page.locator('.exr-foot .exr-btn-primary, .prun-foot .exam-submit').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.locator('.exam-submit').click();
  await expect(page).toHaveURL(`/results/${sessionId}`, { timeout: 30_000 });
  await expect(page.getByText(/Kết quả|Results/i).first()).toBeVisible();
}

async function openReadingPart(request: Parameters<typeof registerLearner>[0]) {
  const learner = await registerLearner(request);
  const units = await listPracticeUnits(request, learner.session.accessToken, {
    skill: 'reading',
    scope: 'part',
  });
  const unit = findSyntheticPartUnit(units, 'reading');
  const sitting = await startPracticeUnit(request, learner.session.accessToken, unit.id);
  return { learner, sitting, unit };
}

async function openListeningPart(request: Parameters<typeof registerLearner>[0]) {
  const learner = await registerLearner(request);
  const units = await listPracticeUnits(request, learner.session.accessToken, {
    skill: 'listening',
    scope: 'part',
  });
  const unit = findSyntheticPartUnit(units, 'listening');
  const sitting = await startPracticeUnit(request, learner.session.accessToken, unit.id);
  return { learner, sitting, unit };
}

test.describe('practice runner', () => {
  test('a learner completes a Reading practice part and reaches results', async ({
    page,
    request,
  }) => {
    const { learner, sitting } = await openReadingPart(request);

    await signIn(page, learner, `/students/practice/${sitting.sessionId}`);
    await expect(page.getByText(READING_PASSAGE)).toBeVisible();

    await fillReadingPart1(page);
    await waitForSaved(page);
    await submitPractice(page, sitting.sessionId);

    /*
      The marked summary, not the word "đúng".

      `getByText(/Đúng|correct/i)` matched twelve elements on this page — the
      summary sentence, the stat label, a card subtitle, a filter chip and two
      per question — so it failed on strict mode while the screen was entirely
      correct. A locator that broad also could not tell "the paper was marked"
      from "the word appears somewhere", which is the thing this test is for.
    */
    await expect(page.getByText(/Bạn trả lời đúng \d+\/\d+ câu/)).toBeVisible();
  });

  test('a learner completes a Listening practice part and reaches results', async ({
    page,
    request,
  }) => {
    const { learner, sitting } = await openListeningPart(request);

    await signIn(page, learner, `/students/practice/${sitting.sessionId}`);
    /*
      <b>A region, not a heading.</b>

      The Listening runner renders no `<h2>` for the part: audio has no passage
      panel, so the part's own title reaches the page as the question group's
      caption, and the group's `<section aria-labelledby>` carries it in the
      region's accessible name ("Questions 1 – 2 Workshop enquiry"). Asserting a
      heading was asserting a DOM shape this screen has never had for Listening,
      and it went red the moment anyone looked.

      The region is also the better assertion: it proves the right part is on
      screen *and* that assistive technology can name it, which a heading lookup
      only implied.
    */
    await expect(page.getByRole('region', { name: LISTENING_TITLE }).first()).toBeVisible();

    await fillListeningPart1(page);
    await waitForSaved(page);
    await submitPractice(page, sitting.sessionId);

    /*
      The marked summary, not the word "đúng".

      `getByText(/Đúng|correct/i)` matched twelve elements on this page — the
      summary sentence, the stat label, a card subtitle, a filter chip and two
      per question — so it failed on strict mode while the screen was entirely
      correct. A locator that broad also could not tell "the paper was marked"
      from "the word appears somewhere", which is the thing this test is for.
    */
    await expect(page.getByText(/Bạn trả lời đúng \d+\/\d+ câu/)).toBeVisible();
  });

  test('pre-submit session and autosave responses carry no keys, explanations or transcripts', async ({
    page,
    context,
    request,
  }) => {
    const { learner, sitting } = await openReadingPart(request);
    const recorded = recordPreSubmitResponses(context);

    await signIn(page, learner, `/students/practice/${sitting.sessionId}`);
    await showPracticeQuestions(page);
    await expect(
      page.getByRole('textbox', { name: /What did the survey team measure/ }),
    ).toBeVisible();

    await page
      .getByRole('textbox', { name: /What did the survey team measure/ })
      .fill('river depth');
    await waitForSaved(page);

    expect(
      recorded.length,
      'Expected at least one session GET and one autosave response.',
    ).toBeGreaterThan(0);
    assertNoPreSubmitLeaks(recorded);
  });

  test('a reload mid-part keeps answers that were already saved', async ({ page, request }) => {
    const { learner, sitting } = await openReadingPart(request);

    await signIn(page, learner, `/students/practice/${sitting.sessionId}`);
    await showPracticeQuestions(page);

    const answer = page.getByRole('textbox', { name: /What did the survey team measure/ });
    await expect(answer).toBeVisible();
    await answer.fill('river depth');
    await waitForSaved(page);

    await page.reload();
    await showPracticeQuestions(page);

    await expect(
      page.getByRole('textbox', { name: /What did the survey team measure/ }),
    ).toHaveValue('river depth');

    const stored = await getSession(request, learner.session.accessToken, sitting.sessionId);
    expect(stored.current.answers['syn-r-1']).toBe('river depth');
  });

  test('a double-click on submit produces one submission, not two', async ({ page, request }) => {
    const { learner, sitting } = await openReadingPart(request);
    const submits: number[] = [];

    page.on('response', (response) => {
      const url = new URL(response.url());
      if (url.origin === API && url.pathname.endsWith('/submit')) submits.push(response.status());
    });

    await signIn(page, learner, `/students/practice/${sitting.sessionId}`);
    await fillReadingPart1(page);
    await waitForSaved(page);

    await page.locator('.exr-foot .exr-btn-primary, .prun-foot .exam-submit').click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();

    const confirm = dialog.locator('.exam-submit');
    await confirm.dblclick({ delay: 50 });

    await expect(page).toHaveURL(`/results/${sitting.sessionId}`, { timeout: 30_000 });
    expect(
      submits.filter((status) => status >= 200 && status < 300).length,
      'Submit must be idempotent under a double-click — one success, not two competing papers.',
    ).toBe(1);
  });
});
