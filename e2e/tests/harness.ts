import { expect, type APIRequestContext, type BrowserContext, type Page } from '@playwright/test';

export const API = 'http://localhost:5199';
export const WEB = 'http://localhost:5273';

/** The four-module synthetic paper used by E2E only (staged in global-setup). */
export const SYNTHETIC_EXAM = 'VNI Synthetic Practice Test';

const PASSWORD = 'mot-mat-khau-du-dai-2026';

export interface Learner {
  email: string;
  session: {
    accessToken: string;
    accessTokenExpiresAt: string;
    refreshToken: string;
    refreshTokenExpiresAt: string;
    userId: string;
    displayName: string;
  };
}

const key = () => crypto.randomUUID().replace(/-/g, '');

/**
 * Registers a learner over the API rather than through the sign-up form.
 *
 * <b>Deliberate, and the reason is not speed.</b> Every test below is about
 * something other than registration — a race, a dropped connection, two tabs.
 * Driving the form to reach them would put the sign-up screen's markup in the
 * failure path of all of them, so a renamed label would break twelve tests that
 * have nothing to do with sign-up and none of the messages would say so.
 *
 * The sign-up form has its own coverage in the component suite, and `I7.4`
 * covers registration over HTTP.
 */
export async function registerLearner(request: APIRequestContext): Promise<Learner> {
  const email = `e2e.${key()}@example.com`;

  const response = await request.post(`${API}/api/v1/auth/register`, {
    headers: { 'Idempotency-Key': key() },
    data: { email, password: PASSWORD, displayName: 'Học viên E2E' },
  });

  expect(response.status(), await response.text()).toBe(201);

  return { email, session: (await response.json()).session };
}

/**
 * Puts a session in `localStorage` exactly as `packages/auth` writes it, then
 * loads the app.
 *
 * <b>The origin has to exist before `localStorage` does.</b> A fresh context has
 * no origin at all, so writing storage before the first navigation writes it
 * into `about:blank` and it is gone by the time the app reads. Navigating to a
 * cheap page first, seeding, then navigating for real is the sequence that
 * works — and getting it wrong looks exactly like the app ignoring a valid
 * session.
 */
export async function signIn(page: Page, learner: Learner, to = '/students/dashboard') {
  await page.goto('/404');
  await page.evaluate(
    (session) => localStorage.setItem('vni.session', JSON.stringify(session)),
    learner.session,
  );
  await page.goto(to);
}

/** The exam catalogue, over the API. */
export async function syntheticExamId(
  request: APIRequestContext,
  accessToken: string,
): Promise<string> {
  const response = await request.get(`${API}/api/v1/exams`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });

  expect(response.ok(), await response.text()).toBeTruthy();

  const exam = (await response.json()).exams.find(
    (e: { title: string }) => e.title === SYNTHETIC_EXAM,
  );

  expect(exam, `'${SYNTHETIC_EXAM}' is not in the catalogue.`).toBeTruthy();
  return exam.examVersionId;
}

/** Starts a Full Test and returns the session view. */
export async function startFullTest(request: APIRequestContext, accessToken: string) {
  const examVersionId = await syntheticExamId(request, accessToken);

  const response = await request.post(`${API}/api/v1/sessions`, {
    headers: { Authorization: `Bearer ${accessToken}`, 'Idempotency-Key': key() },
    data: { examVersionId, mode: 'full', module: null },
  });

  expect(response.status(), await response.text()).toBe(201);
  return response.json();
}

export async function getSession(
  request: APIRequestContext,
  accessToken: string,
  sessionId: string,
) {
  const response = await request.get(`${API}/api/v1/sessions/${sessionId}`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });

  expect(response.ok(), await response.text()).toBeTruthy();
  return response.json();
}

/** Post-submit results view — bands, markings, markingStatuses. */
export async function getResults(
  request: APIRequestContext,
  accessToken: string,
  sessionId: string,
) {
  const response = await request.get(`${API}/api/v1/sessions/${sessionId}/results`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });

  expect(response.ok(), await response.text()).toBeTruthy();
  return response.json();
}

/**
 * Reads what the app has stored right now.
 *
 * Used by the two-tab tests to tell an adopted token from a rotated one — the
 * whole point of `packages/auth/src/coordinator.ts`.
 */
export async function storedSession(page: Page) {
  return page.evaluate(() => {
    const raw = localStorage.getItem('vni.session');
    return raw ? (JSON.parse(raw) as { accessToken: string; refreshToken: string }) : null;
  });
}

/** Every request the page has made to the API, in the order the browser sent them. */
export function recordApiCalls(context: BrowserContext) {
  const calls: { method: string; path: string }[] = [];

  context.on('request', (request) => {
    const url = new URL(request.url());
    if (url.origin === API) calls.push({ method: request.method(), path: url.pathname });
  });

  return calls;
}

export interface PracticeUnit {
  id: string;
  examVersionId: string;
  title: string;
  module: 'reading' | 'listening' | 'writing' | 'speaking' | null;
  scope: 'part' | 'skill' | 'full-test';
  partIds: string[];
  slotCount: number;
  durationSeconds: number;
  scoreCapability: string;
}

/** Practice-unit catalogue, over the API. */
export async function listPracticeUnits(
  request: APIRequestContext,
  accessToken: string,
  filters: { skill?: string; scope?: string; variant?: string } = {},
): Promise<PracticeUnit[]> {
  const params = new URLSearchParams();
  if (filters.skill) params.set('skill', filters.skill);
  if (filters.scope) params.set('scope', filters.scope);
  if (filters.variant) params.set('variant', filters.variant);

  const qs = params.toString();
  const response = await request.get(
    `${API}/api/v1/practice-units${qs ? `?${qs}` : ''}`,
    { headers: { Authorization: `Bearer ${accessToken}` } },
  );

  expect(response.ok(), await response.text()).toBeTruthy();
  return (await response.json()).units;
}

/** The first part-scoped unit for a skill on the synthetic four-module paper. */
export function findSyntheticPartUnit(
  units: PracticeUnit[],
  skill: 'reading' | 'listening',
): PracticeUnit {
  /*
   * Title must match `SYNTHETIC_EXAM`. Part-id alone is not enough: Exam 1 and
   * the synthetic paper both publish `reading-part-1` / `listening-part-1`, and
   * `units.find` would otherwise pick Exam 1 first — then the runner shows
   * Leatherback turtles while the spec waits for Surveying the Lower Delta.
   */
  const unit = units.find(
    (candidate) =>
      candidate.title === SYNTHETIC_EXAM &&
      candidate.module === skill &&
      candidate.scope === 'part' &&
      candidate.partIds.length === 1 &&
      candidate.partIds[0] === `${skill}-part-1`,
  );

  expect(unit, `No ${skill} part-1 unit for '${SYNTHETIC_EXAM}'.`).toBeTruthy();
  return unit!;
}

/** Starts a practice unit and returns the session view. */
export async function startPracticeUnit(
  request: APIRequestContext,
  accessToken: string,
  practiceUnitId: string,
  targetSeconds?: number,
) {
  const response = await request.post(`${API}/api/v1/sessions`, {
    headers: { Authorization: `Bearer ${accessToken}`, 'Idempotency-Key': key() },
    data: {
      practiceUnitId,
      ...(targetSeconds !== undefined ? { targetSeconds } : {}),
    },
  });

  expect(response.status(), await response.text()).toBe(201);
  return response.json();
}

/**
 * Reading practice on a phone starts on the Passage tab; questions are hidden
 * until the learner opens Questions. Desktop has no toggle — this is a no-op.
 *
 * Must wait for the sitting shell: calling this immediately after `signIn`
 * races the session fetch, sees no tab yet, and leaves the questions pane
 * `display: none` on mobile.
 *
 * Do not use a one-shot `isVisible()` on the tab — that races the CSS
 * breakpoint paint and silently skips the click (mobile then never sees
 * answer fields). Wait for either the tab (narrow) or the questions region
 * already visible (wide).
 */
export async function showPracticeQuestions(page: Page) {
  await page
    .getByRole('region', { name: /Bài đọc|Passage|Câu hỏi|Questions/i })
    .first()
    .waitFor({ state: 'visible', timeout: 30_000 });

  const questionsTab = page.getByRole('button', { name: /^(Câu hỏi|Questions)$/i });
  const questionsRegion = page.getByRole('region', { name: /^(Câu hỏi|Questions)$/i });

  try {
    await questionsTab.waitFor({ state: 'visible', timeout: 2_000 });
  } catch {
    // Desktop / wide layout: no tab strip — questions already share the viewport.
    await expect(questionsRegion).toBeVisible({ timeout: 10_000 });
    return;
  }

  await questionsTab.click();
  await expect(questionsTab).toHaveAttribute('aria-pressed', 'true');
  await expect(questionsRegion).toBeVisible();
}

const FORBIDDEN_PRE_SUBMIT_KEYS = new Set(['answerkey', 'explanation', 'transcript']);

/** Records session GET and autosave response bodies for leak scanning. */
export function recordPreSubmitResponses(context: BrowserContext) {
  const bodies: { method: string; path: string; body: unknown }[] = [];

  context.on('response', async (response) => {
    const url = new URL(response.url());
    if (url.origin !== API) return;

    const method = response.request().method();
    const path = url.pathname;
    const isSessionGet = method === 'GET' && /\/api\/v1\/sessions\/[^/]+$/.test(path);
    const isAutosave = method === 'PUT' && /\/api\/v1\/sessions\/[^/]+\/answers$/.test(path);

    if (!isSessionGet && !isAutosave) return;

    try {
      bodies.push({ method, path, body: await response.json() });
    } catch {
      // Non-JSON bodies are not part of the sitting contract.
    }
  });

  return bodies;
}

/** Walks recorded JSON and fails if answer keys, explanations or transcripts leak. */
export function assertNoPreSubmitLeaks(bodies: { body: unknown }[]) {
  for (const { body } of bodies) walkJson(body, []);
}

function walkJson(value: unknown, path: string[]) {
  if (value === null || typeof value !== 'object') return;

  if (Array.isArray(value)) {
    value.forEach((item, index) => walkJson(item, [...path, String(index)]));
    return;
  }

  for (const [key, child] of Object.entries(value as Record<string, unknown>)) {
    expect(
      FORBIDDEN_PRE_SUBMIT_KEYS.has(key.toLowerCase()),
      `Pre-submit leak at ${[...path, key].join('.')}: "${key}" must not appear before submit.`,
    ).toBe(false);
    walkJson(child, [...path, key]);
  }
}
