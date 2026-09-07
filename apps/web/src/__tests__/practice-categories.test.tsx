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

const exams = [
  {
    examVersionId: 'cam17-1',
    title: 'Cambridge IELTS 17 — Test 1',
    variant: 'academic',
    description: null,
    moduleSequence: ['reading', 'listening', 'writing', 'speaking'],
    modules: [
      { module: 'reading', questionCount: 40, durationSeconds: 3600 },
      { module: 'listening', questionCount: 40, durationSeconds: 1800 },
      { module: 'writing', questionCount: 2, durationSeconds: 3600 },
      { module: 'speaking', questionCount: 3, durationSeconds: 900 },
    ],
  },
  {
    examVersionId: 'cam17-2',
    title: 'Cambridge IELTS 17 — Test 2',
    variant: 'academic',
    description: null,
    moduleSequence: ['reading', 'listening', 'writing', 'speaking'],
    modules: [
      { module: 'reading', questionCount: 40, durationSeconds: 3600 },
      { module: 'listening', questionCount: 40, durationSeconds: 1800 },
      { module: 'writing', questionCount: 2, durationSeconds: 3600 },
      { module: 'speaking', questionCount: 3, durationSeconds: 900 },
    ],
  },
];

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

const me = {
  userId: 'user-1',
  displayName: 'Nguyễn Thị Đào',
  email: 'dao@example.com',
  emailVerified: true,
  permissions: ['exam.read'],
  providers: ['google'],
  hasPassword: false,
};

function signedIn() {
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/exams')) return json({ exams });
      if (url.includes('/api/v1/me')) return json(me);
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

it('lists sets grouped by category, and search narrows them', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/categories');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  expect(await screen.findByRole('link', { name: 'Cambridge IELTS 17' })).toBeInTheDocument();
  expect(screen.getByText('2 đề')).toBeInTheDocument();

  await waitFor(() => expect(document.title).toMatch(/^Danh sách bộ đề/));
});

it('shows the sets inside one category', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/categories/cambridge-ielts');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  expect(await screen.findByRole('heading', { name: 'Cambridge IELTS' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Cambridge IELTS 17' })).toBeInTheDocument();
});

it('shows an honest empty state for an unknown category slug', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/categories/does-not-exist');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  expect(await screen.findByText('Không tìm thấy bộ đề nào khớp.')).toBeInTheDocument();
});
