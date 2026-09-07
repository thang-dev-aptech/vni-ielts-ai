import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

const session = {
  accessToken: 'access-token',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh-token',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  userId: 'user-1',
  displayName: 'Nguyễn Thị Đào',
};

const me = {
  userId: 'user-1',
  displayName: 'Nguyễn Thị Đào',
  email: 'dao@example.com',
  emailVerified: true,
  permissions: ['exam.read'],
  providers: ['google'],
  hasPassword: false,
};

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

/**
 * Minimal but *complete* running-sitting payload for `PracticeRunnerPage` —
 * the same shape `practice-runner.test.tsx` uses. This test only needs the
 * runner to mount without crashing (it is proving *routing*, not runner
 * behaviour), but the runner reads `current.parts` unconditionally, so a
 * partial fixture throws during render instead of failing the assertion.
 */
function runningSitting() {
  return {
    sessionId: 'sit-1',
    examVersionId: 'cam17-1',
    examTitle: 'Cambridge IELTS 17 — Test 1',
    practiceUnitId: null,
    scope: null,
    completedPartIds: [],
    mode: 'single',
    status: 'inprogress',
    startedAt: new Date().toISOString(),
    serverNow: new Date().toISOString(),
    completedModules: [],
    current: {
      module: 'reading',
      partId: null,
      startedAt: new Date().toISOString(),
      deadlineAt: null,
      remainingSeconds: null,
      elapsedSeconds: 0,
      running: true,
      targetSeconds: null,
      parts: [
        {
          order: 1,
          kind: 'passage',
          title: 'The History of Cartography',
          body: 'Đoạn văn mẫu.',
          audioKey: null,
          imageKey: null,
          taskNumber: null,
          partNumber: null,
          cueCard: null,
          minWords: null,
          questions: [
            { id: 'r-1', order: 1, type: 'short-answer', prompt: 'Câu hỏi 1', options: [], maxWords: 2, group: null },
          ],
        },
      ],
      answers: {},
      answerRevision: 1,
      speakingTiming: [],
      transferSeconds: null,
      audioPlayback: null,
    },
  };
}

function signedIn(extra?: (url: string, init?: RequestInit) => Response | undefined) {
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const overridden = extra?.(url, init);
      if (overridden !== undefined) return overridden;
      if (url.includes('/api/v1/exams')) return json({ exams: [] });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/api/v1/sessions') && init?.method === 'POST') {
        return json({
          sessionId: 'sit-new-1',
          examVersionId: 'cam17-1',
          examTitle: 'Cambridge IELTS 17 — Test 1',
          practiceUnitId: null,
          scope: null,
          completedPartIds: [],
          mode: 'full',
          status: 'inprogress',
        });
      }
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  window.history.pushState({}, '', '/');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('a literal path segment under /students/practice never falls into the :sessionId runner', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/categories');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  // The categories page has a heading and a sidebar; the runner has neither.
  await screen.findByRole('heading', { name: 'Danh sách bộ đề' });
  expect(document.querySelector('.shell-rail')).not.toBeNull();
});

it('a real session id still opens the practice runner, unaffected by the new sibling routes', async () => {
  signedIn((url) => {
    if (url.includes('/api/v1/sessions/sit-1')) return json(runningSitting());
    return undefined;
  });
  window.history.pushState({}, '', '/students/practice/sit-1');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  // The runner renders outside every shell — no sidebar.
  await waitFor(() => expect(document.querySelector('[data-surface="exam"]')).not.toBeNull());
  expect(document.querySelector('.shell-rail')).toBeNull();
});

it('the exam launcher creates a session and hands off to the deadline runner', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/exam/cam17-1?timing=deadline');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  await waitFor(() => expect(window.location.pathname).toBe('/students/session/sit-new-1'));
});
