import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';

/**
 * The route guard for `/documents` and `/articles`.
 *
 * <b>Why this renders the real `App`, unlike `library-content.test.tsx`.</b>
 * `Gate` lives inline in `App.tsx` and is not exported — what is under test
 * here is specifically "does the router send an operator with neither
 * `document.write` nor `document.publish` to 1.2 (`ForbiddenPage`) instead of
 * to the screen", which only exists at the router's level. `AdminAuthProvider`
 * and `ViewAsProvider` are mocked to pass their children through untouched, so
 * this does not also exercise session restore or the dev-only preview switch.
 */

vi.mock('../lib/AdminAuth.js', () => ({
  AdminAuthProvider: ({ children }: { children: React.ReactNode }) => children,
  useAdminAuth: () => ({
    status: 'signed-in',
    user: { displayName: 'Học viên', email: 'learner@vni.test', userId: 'u1' },
    accessToken: 'token-1',
    isOperator: true,
    can: (permission: string) => permission === 'user.read',
    signIn: vi.fn(),
    signOut: vi.fn(),
  }),
}));

vi.mock('../lib/operator.js', () => ({
  ViewAsProvider: ({ children }: { children: React.ReactNode }) => children,
  useViewAs: () => ({ preset: null, setPreset: vi.fn(), available: false }),
  useOperator: () => ({
    can: (permission: string) => permission === 'user.read',
    isOperator: true,
    name: 'Học viên',
    email: 'learner@vni.test',
    previewing: false,
    previewLabel: null,
  }),
  ROLE_PRESETS: [],
}));

const { App } = await import('../App.js');

describe('route guard on /documents and /articles', () => {
  it('sends an operator holding neither document.write nor document.publish to the forbidden screen', () => {
    window.history.pushState({}, '', '/documents');
    render(<App />);

    expect(screen.getByText('Không đủ quyền')).toBeInTheDocument();
    expect(screen.getByText('document.write')).toBeInTheDocument();
    expect(screen.queryByText('Tài liệu')).not.toBeInTheDocument();
  });

  it('does the same for /articles', () => {
    window.history.pushState({}, '', '/articles');
    render(<App />);

    expect(screen.getByText('Không đủ quyền')).toBeInTheDocument();
    expect(screen.getByText('article.write')).toBeInTheDocument();
  });
});
