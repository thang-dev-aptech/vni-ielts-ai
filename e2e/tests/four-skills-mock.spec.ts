import { expect, test, type Page } from '@playwright/test';
import {
  API,
  getResults,
  getSession,
  registerLearner,
  signIn,
  startFullTest,
} from './harness';

/**
 * FS7 phase gate — shortened four-skill Full Mock in a real browser.
 *
 * Server integration already proves no-voice honest pending and advance
 * idempotency. This file is what those tests cannot see: the runner follows
 * `moduleSequence`, Next/Submit under a double-click, and the results screen
 * never invents an overall band when Speaking is still pending.
 *
 * Speaking recording is optional here. Chromium in CI usually has no mic;
 * skipping capture still ends with Speaking/overall pending rather than a
 * fabricated band — the same honesty the production-default no-voice path
 * owes the learner.
 */

const READING_PASSAGE = /Surveying the Lower Delta/;
const LISTENING_HEADING = 'Workshop enquiry';
const WRITING_TABLE = /visitors to four public libraries/i;
const SPEAKING_CUE = /journey you took that did not go as planned|where you live/i;

const ESSAY =
  'The table compares visitor numbers across four libraries over three years. ' +
  'Central declined while Riverside and Eastgate rose sharply, and Hilltop stayed flat. ' +
  'Overall the city shifted from one large institution toward more local venues. '.repeat(3);

async function waitForSaved(page: Page) {
  await expect(page.locator('.save-chip')).toHaveClass(/is-saved/, { timeout: 20_000 });
}

async function fillReading(page: Page) {
  await page.getByRole('textbox', { name: /What did the survey team measure/ }).fill('river depth');
  await page.locator('input[type="radio"][value="FALSE"]').check();
  await page.getByRole('radio', { name: /A.*instruments needed recalibrating/i }).check();

  const bank = page.getByRole('list', { name: /Ngân hàng đáp án|Answer bank/i });
  await bank.getByRole('button', { name: /B.*flow meter/i }).click();
  await page.getByRole('button', { name: /The reading taken at dawn/i }).click();
}

async function fillListening(page: Page) {
  // Completion gaps are named by their blank number in the group text, not the prompt.
  await page.getByRole('textbox', { name: '1', exact: true }).fill('9:30');
  await page.getByRole('textbox', { name: '2', exact: true }).fill('three');
  await page.getByRole('checkbox', { name: /A.*notebook/i }).check();
  await page.getByRole('checkbox', { name: /C.*packed lunch/i }).check();
  await page.getByRole('textbox', { name: /contact to cancel/i }).fill('registrar');
}

async function fillWriting(page: Page) {
  await expect(page.getByText(WRITING_TABLE)).toBeVisible();
  await page.locator('.q-essay').fill(ESSAY);
  await waitForSaved(page);

  await page.getByRole('button', { name: /Phần 2|Part 2/i }).click();
  await expect(page.getByText(/cultural budget/i)).toBeVisible();
  await page.locator('.q-essay').fill(ESSAY + ESSAY);
  await waitForSaved(page);
}

/** Footer primary action — "Tiếp theo" while skills remain, "Nộp bài" on the last. */
function primaryAction(page: Page) {
  return page.locator('.prun-foot .exam-submit, .exam-foot .exam-submit');
}

async function clickNext(page: Page) {
  const button = primaryAction(page);
  await expect(button).toBeEnabled();
  await button.click();
}

test.describe('four-skill mock', () => {
  test('a Full Mock follows moduleSequence through four skills and ends with honest pending', async ({
    page,
    request,
  }) => {
    test.setTimeout(180_000);

    const learner = await registerLearner(request);
    const sitting = await startFullTest(request, learner.session.accessToken);

    const sequence: string[] = sitting.moduleSequence;
    expect(sequence, 'Server must publish the sitting order.').toEqual([
      'reading',
      'listening',
      'writing',
      'speaking',
    ]);
    expect(sitting.current.module).toBe(sequence[0]);

    await signIn(page, learner, `/students/session/${sitting.sessionId}`);
    await expect(page.getByRole('heading', { name: READING_PASSAGE })).toBeVisible();
    await expect(page.getByText(/Kỹ năng 1\/4|Skill 1 of 4/i)).toBeVisible();

    // ── Reading ──────────────────────────────────────────────────────────
    await fillReading(page);
    await waitForSaved(page);
    await clickNext(page);

    await expect(page.getByRole('heading', { name: LISTENING_HEADING, exact: true })).toBeVisible({
      timeout: 30_000,
    });
    await expect(page.getByText(/Kỹ năng 2\/4|Skill 2 of 4/i)).toBeVisible();
    let open = await getSession(request, learner.session.accessToken, sitting.sessionId);
    expect(open.current.module).toBe(sequence[1]);
    expect(open.completedModules).toEqual([sequence[0]]);

    // ── Listening ────────────────────────────────────────────────────────
    await fillListening(page);
    await waitForSaved(page);
    await clickNext(page);

    await expect(page.getByText(WRITING_TABLE)).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(/Kỹ năng 3\/4|Skill 3 of 4/i)).toBeVisible();
    open = await getSession(request, learner.session.accessToken, sitting.sessionId);
    expect(open.current.module).toBe(sequence[2]);
    expect(open.completedModules).toEqual([sequence[0], sequence[1]]);

    // ── Writing (essays autosave; fake evaluator / no live AI) ────────────
    await fillWriting(page);
    await clickNext(page);

    await expect(page.getByText(SPEAKING_CUE).first()).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(/Kỹ năng 4\/4|Skill 4 of 4/i)).toBeVisible();
    open = await getSession(request, learner.session.accessToken, sitting.sessionId);
    expect(open.current.module).toBe(sequence[3]);
    expect(open.completedModules).toEqual([sequence[0], sequence[1], sequence[2]]);

    // ── Speaking — skip mic; submit ends the sitting ─────────────────────
    // No recording when the environment has no voice device. Honest pending
    // (NothingSubmitted / AwaitingVoiceProvider / dash) must still hold.
    await clickNext(page);

    await expect(page).toHaveURL(`/practice/results/${sitting.sessionId}`, {
      timeout: 30_000,
    });
    await expect(page.getByText(/Kết quả|Results/i).first()).toBeVisible();
    await expect(page.locator('.result-overall-value')).toHaveText('—');
    await expect(
      page.getByText(/Điểm tổng chỉ có khi đủ cả bốn kỹ năng|overall band needs all four skills/i),
    ).toBeVisible();

    const results = await getResults(request, learner.session.accessToken, sitting.sessionId);
    expect(results.overallBand, 'Must not invent an overall from three skills.').toBeNull();

    const markedModules = (results.sections as { module: string; band: number | null }[]).map(
      (s) => s.module,
    );
    expect(markedModules.sort()).toEqual(['listening', 'reading']);
    expect(markedModules).not.toContain('speaking');
    expect(markedModules).not.toContain('writing');

    const markings = (results.markings ?? []) as { module: string; band: number }[];
    expect(
      markings.filter((m) => m.module === 'speaking' || m.module === 'writing'),
      'No fabricated Writing/Speaking bands on a no-voice / unevaluated run.',
    ).toEqual([]);

    const statuses = (results.markingStatuses ?? []) as {
      module: string;
      code: string | null;
      state: string;
    }[];
    const speakingStatus = statuses.find((s) => s.module === 'speaking');
    if (speakingStatus?.code) {
      expect(
        ['AwaitingVoiceProvider', 'AwaitingTranscript', 'NothingSubmitted', 'AwaitingEvaluator'],
        'Speaking pending code must be honest, never a scored completion.',
      ).toContain(speakingStatus.code);
    }

    // UI: Writing and Speaking rows show "not marked", not a numeric band.
    await expect(page.getByText(/Chưa chấm|Not marked/i).first()).toBeVisible();
  });

  test('a double-click on Next advances once, not twice', async ({ page, request }) => {
    test.setTimeout(90_000);

    const learner = await registerLearner(request);
    const sitting = await startFullTest(request, learner.session.accessToken);

    const advances: number[] = [];
    page.on('response', (response) => {
      const url = new URL(response.url());
      if (url.origin === API && /\/api\/v1\/sessions\/[^/]+\/advance$/.test(url.pathname)) {
        advances.push(response.status());
      }
    });

    await signIn(page, learner, `/students/session/${sitting.sessionId}`);
    await expect(page.getByRole('heading', { name: READING_PASSAGE })).toBeVisible();

    await fillReading(page);
    await waitForSaved(page);

    /*
     * Hold the first advance on the wire so both clicks of a dblclick land
     * while the section is still Reading. A fast local API otherwise finishes
     * the first advance before the second click, and the footer button would
     * mean Listening→Writing — a different transition, not a double Reading
     * advance. The latch + remounted footer are what this race is for.
     */
    let release!: () => void;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    let heldOnce = false;
    await page.route(`${API}/api/v1/sessions/*/advance`, async (route) => {
      if (!heldOnce) {
        heldOnce = true;
        await held;
      }
      await route.continue();
    });

    const next = primaryAction(page);
    await expect(next).toHaveText(/Tiếp theo|Next/i);
    await next.dblclick({ delay: 40 });
    release();

    await expect(page.getByRole('heading', { name: LISTENING_HEADING, exact: true })).toBeVisible({
      timeout: 30_000,
    });

    const open = await getSession(request, learner.session.accessToken, sitting.sessionId);
    expect(
      open.current.module,
      'Double-click must not skip Listening into Writing.',
    ).toBe('listening');
    expect(open.completedModules).toEqual(['reading']);

    expect(
      advances.filter((status) => status >= 200 && status < 300).length,
      'Advance must be idempotent under a double-click — one success, not two.',
    ).toBe(1);
  });

  test('a double-click on Submit on the last skill produces one completion', async ({
    page,
    request,
  }) => {
    test.setTimeout(180_000);

    const learner = await registerLearner(request);
    const sitting = await startFullTest(request, learner.session.accessToken);

    const submits: number[] = [];
    const advances: number[] = [];
    page.on('response', (response) => {
      const url = new URL(response.url());
      if (url.origin !== API) return;
      if (/\/api\/v1\/sessions\/[^/]+\/submit$/.test(url.pathname)) {
        submits.push(response.status());
      }
      if (/\/api\/v1\/sessions\/[^/]+\/advance$/.test(url.pathname)) {
        advances.push(response.status());
      }
    });

    await signIn(page, learner, `/students/session/${sitting.sessionId}`);

    await fillReading(page);
    await waitForSaved(page);
    await clickNext(page);
    await expect(page.getByRole('heading', { name: LISTENING_HEADING, exact: true })).toBeVisible({
      timeout: 30_000,
    });

    await fillListening(page);
    await waitForSaved(page);
    await clickNext(page);
    await expect(page.getByText(WRITING_TABLE)).toBeVisible({ timeout: 30_000 });

    await fillWriting(page);
    await clickNext(page);
    await expect(page.getByText(SPEAKING_CUE).first()).toBeVisible({ timeout: 30_000 });

    const submit = primaryAction(page);
    await expect(submit).toHaveText(/Nộp bài|Submit/i);
    await submit.dblclick({ delay: 40 });

    await expect(page).toHaveURL(`/practice/results/${sitting.sessionId}`, {
      timeout: 30_000,
    });

    expect(
      advances.filter((status) => status >= 200 && status < 300).length,
      'Reading→Listening→Writing→Speaking is three advances.',
    ).toBe(3);
    expect(
      submits.filter((status) => status >= 200 && status < 300).length,
      'Submit on the last skill must be idempotent under a double-click.',
    ).toBe(1);

    const results = await getResults(request, learner.session.accessToken, sitting.sessionId);
    expect(results.overallBand).toBeNull();
    expect(results.status).toBe('submitted');
  });
});
