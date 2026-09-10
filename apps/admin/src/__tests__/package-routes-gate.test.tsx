import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';

const permissions = new Set<string>();

vi.mock('../lib/AdminAuth.js', () => ({
  AdminAuthProvider: ({ children }: { children: React.ReactNode }) => children,
  useAdminAuth: () => ({
    status: 'signed-in',
    user: { displayName: 'Reviewer', email: 'reviewer@vni.test', userId: 'u-rev' },
    accessToken: 'token-rev',
    isOperator: true,
    can: (permission: string) => permissions.has(permission),
    signIn: vi.fn(),
    signOut: vi.fn(),
  }),
}));

vi.mock('../lib/operator.js', () => ({
  ViewAsProvider: ({ children }: { children: React.ReactNode }) => children,
  useViewAs: () => ({ preset: null, setPreset: vi.fn(), available: false }),
  useOperator: () => ({
    can: (permission: string) => permissions.has(permission),
    isOperator: true,
    name: 'Reviewer',
    email: 'reviewer@vni.test',
    previewing: false,
    previewLabel: null,
  }),
  ROLE_PRESETS: [],
}));

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    getPackage: vi.fn().mockResolvedValue({
      packageId: 'pkg-1',
      sourceKind: 'zip',
      fileName: 'test.zip',
      status: 'needs-review',
      uploadedByName: 'Ops',
      findings: [],
      entries: [],
      createdVersionIds: [],
      createdAt: '2026-09-07T00:00:00.000Z',
      updatedAt: '2026-09-07T00:00:00.000Z',
    }),
    listPackageCandidates: vi.fn().mockResolvedValue([]),
    listPackages: vi.fn().mockResolvedValue([]),
    listExams: vi.fn().mockResolvedValue({ exams: [] }),
    getImportDraft: vi.fn(),
  };
});

const { App } = await import('../App.js');

describe('Reviewer permission route test (F7)', () => {
  beforeEach(() => {
    permissions.clear();
  });

  it('allows access to /packages/:packageId when operator holds exam.review without package.read', async () => {
    permissions.add('exam.review');
    window.history.pushState({}, '', '/packages/pkg-1');
    render(<App />);

    await waitFor(() => {
      expect(screen.queryByText('Không đủ quyền')).not.toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'test.zip' })).toBeInTheDocument();
    });
  });

  it('allows access to /packages when operator holds exam.review without package.read', async () => {
    permissions.add('exam.review');
    window.history.pushState({}, '', '/packages');
    render(<App />);

    await waitFor(() => {
      expect(screen.queryByText('Không đủ quyền')).not.toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'Lịch sử gói' })).toBeInTheDocument();
    });
  });

  it('forbids access to /packages/:packageId when operator holds neither package.read nor exam.review', async () => {
    window.history.pushState({}, '', '/packages/pkg-1');
    render(<App />);

    await waitFor(() => {
      expect(screen.getByText('Không đủ quyền')).toBeInTheDocument();
    });
  });

  it('hides "Nhập đề mới" on /packages when operator lacks package.upload or exam.create', async () => {
    permissions.add('package.read');
    permissions.add('package.upload');
    // lacks exam.create
    window.history.pushState({}, '', '/packages');
    render(<App />);

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: 'Lịch sử gói' })).toBeInTheDocument();
    });
    expect(screen.queryByRole('link', { name: 'Nhập đề mới' })).not.toBeInTheDocument();
  });

  it('shows "Nhập đề mới" on /packages when operator holds both package.upload and exam.create', async () => {
    permissions.add('package.read');
    permissions.add('package.upload');
    permissions.add('exam.create');
    window.history.pushState({}, '', '/packages');
    render(<App />);

    await waitFor(() => {
      expect(screen.getByRole('link', { name: 'Nhập đề mới' })).toBeInTheDocument();
    });
  });
});
