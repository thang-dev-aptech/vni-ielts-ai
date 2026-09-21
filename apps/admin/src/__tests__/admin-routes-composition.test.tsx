import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { ApiError } from '@vni/auth';

/**
 * Route composition for evaluations, package history, and config.
 *
 * `Gate` lives inline in `App.tsx`. These tests render the real `App` with
 * auth mocked through, so they prove the router (not a screen in isolation)
 * sends the right permission to 1.2 and the right child path to the right
 * screen — including `/evaluations/failed-jobs` not colliding with detail.
 */

const held = new Set<string>(['user.read']);

vi.mock('../lib/AdminAuth.js', () => ({
  AdminAuthProvider: ({ children }: { children: React.ReactNode }) => children,
  useAdminAuth: () => ({
    status: 'signed-in',
    user: { displayName: 'Điều hành', email: 'ops@vni.test', userId: 'u1' },
    accessToken: 'token-1',
    isOperator: true,
    can: (permission: string) => held.has(permission),
    signIn: vi.fn(),
    signOut: vi.fn(),
  }),
}));

vi.mock('../lib/operator.js', () => ({
  ViewAsProvider: ({ children }: { children: React.ReactNode }) => children,
  useViewAs: () => ({ preset: null, setPreset: vi.fn(), available: false }),
  useOperator: () => ({
    can: (permission: string) => held.has(permission),
    isOperator: true,
    name: 'Điều hành',
    email: 'ops@vni.test',
    previewing: false,
    previewLabel: null,
  }),
  ROLE_PRESETS: [],
}));

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    listEvaluations: vi.fn(),
    getEvaluation: vi.fn(),
    listFailedMarkingJobs: vi.fn(),
    listPackageHistory: vi.fn(),
    getPackageHistory: vi.fn(),
    getRuntimeConfiguration: vi.fn(),
  };
});

const {
  listEvaluations,
  getEvaluation,
  listFailedMarkingJobs,
  listPackageHistory,
  getPackageHistory,
  getRuntimeConfiguration,
} = await import('../lib/adminApi.js');
const { App } = await import('../App.js');

beforeEach(() => {
  grant();
  vi.mocked(listEvaluations).mockReset();
  vi.mocked(getEvaluation).mockReset();
  vi.mocked(listFailedMarkingJobs).mockReset();
  vi.mocked(listPackageHistory).mockReset();
  vi.mocked(getPackageHistory).mockReset();
  vi.mocked(getRuntimeConfiguration).mockReset();
});

function grant(...keys: string[]) {
  held.clear();
  held.add('user.read');
  for (const key of keys) held.add(key);
}

function open(path: string) {
  window.history.pushState({}, '', path);
  return render(<App />);
}

describe('admin route composition', () => {
  it('sends an operator without evaluation.read, package.read or config.read to 1.2', () => {
    grant();
    const evaluations = open('/evaluations');
    expect(screen.getByText('Không đủ quyền')).toBeInTheDocument();
    expect(screen.getByText('evaluation.read')).toBeInTheDocument();
    evaluations.unmount();

    const packages = open('/packages');
    expect(screen.getByText('package.read')).toBeInTheDocument();
    packages.unmount();

    open('/config');
    expect(screen.getByText('config.read')).toBeInTheDocument();
    expect(screen.queryByText('Đang tải cấu hình…')).not.toBeInTheDocument();
  });

  it('loads evaluation list, failed queue, package history and config behind their gates', async () => {
    grant('evaluation.read', 'package.read', 'config.read');
    vi.mocked(listEvaluations).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 50 });
    vi.mocked(listFailedMarkingJobs).mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 50,
    });
    vi.mocked(listPackageHistory).mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 50,
    });
    vi.mocked(getRuntimeConfiguration).mockReturnValue(new Promise(() => undefined));

    const { unmount } = open('/evaluations');
    expect(await screen.findByRole('heading', { name: 'Đánh giá AI' })).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'Hàng chờ chấm hỏng' }).length).toBeGreaterThan(0);
    unmount();

    open('/evaluations/failed-jobs');
    expect(await screen.findByRole('heading', { name: 'Hàng chờ chấm hỏng' })).toBeInTheDocument();
    expect(screen.queryByText('Không tìm thấy đánh giá này')).not.toBeInTheDocument();
    unmount();

    open('/packages');
    expect(await screen.findByRole('heading', { name: 'Lịch sử gói' })).toBeInTheDocument();
    unmount();

    open('/config');
    expect(await screen.findByRole('heading', { name: 'Cấu hình' })).toBeInTheDocument();
  });

  it('treats unknown history and evaluation ids as missing, not as another route', async () => {
    grant('evaluation.read', 'package.read');
    vi.mocked(getPackageHistory).mockRejectedValue(
      new ApiError({ title: 'Not Found', status: 404, detail: 'Not found.', code: 'NOT_FOUND' }),
    );
    vi.mocked(getEvaluation).mockRejectedValue(
      new ApiError({ title: 'Not Found', status: 404, detail: 'Not found.', code: 'NOT_FOUND' }),
    );

    const { unmount } = open('/packages/history/00000000-0000-0000-0000-000000000000');
    expect(await screen.findByText('Không tìm thấy lần tải này')).toBeInTheDocument();
    unmount();

    open('/evaluations/missing-session/missing-marking');
    expect(await screen.findByText('Không tìm thấy đánh giá này')).toBeInTheDocument();
  });

  it('reaches the failed-queue child from the evaluation list without colliding', async () => {
    grant('evaluation.read');
    vi.mocked(listEvaluations).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 50 });
    vi.mocked(listFailedMarkingJobs).mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 50,
    });

    open('/evaluations');
    expect(await screen.findByRole('heading', { name: 'Đánh giá AI' })).toBeInTheDocument();

    const nav = screen.getByRole('navigation', { name: 'Điều hướng quản trị' });
    fireEvent.click(within(nav).getByRole('link', { name: 'Hàng chờ chấm hỏng' }));
    expect(await screen.findByRole('heading', { name: 'Hàng chờ chấm hỏng' })).toBeInTheDocument();

    fireEvent.click(within(nav).getByRole('link', { name: 'Đánh giá AI' }));
    expect(await screen.findByRole('heading', { name: 'Đánh giá AI' })).toBeInTheDocument();
  });

  it('does not keep exporting completed placeholders from PendingPages', async () => {
    const pending = await import('../screens/PendingPages.js');
    expect(pending).not.toHaveProperty('EvaluationsPage');
    expect(pending).not.toHaveProperty('PackagesPage');
    expect(pending).not.toHaveProperty('ConfigPage');
  });
});
