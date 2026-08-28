import { expect, type APIRequestContext, type BrowserContext, type Page } from '@playwright/test';

export const API = 'http://localhost:5199';
export const WEB = 'http://localhost:5273';

/** The four-module paper. Selected by name — see `FullSittingJourneyTests`. */
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
