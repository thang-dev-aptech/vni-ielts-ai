import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import type { AdminEvaluationListItem, AdminEvaluationPage } from '../lib/adminApi.js';

/**
 * Evaluation list — `eval-verification`.
 *
 * `useAdminAuth` and the list call are mocked. Route wiring stays with
 * admin-route-composition; these tests prove the list tells the truth about
 * a page the server already returned, and that the URL carries every filter
 * the server understands (`eval-admin-ui` acceptance: "URL-stable server
 * filters").
 */

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({ accessToken: 'token-1' }),
}));

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    listEvaluations: vi.fn(),
  };
});

const { listEvaluations } = await import('../lib/adminApi.js');
const { EvaluationsPage } = await import('./EvaluationsPage.js');

function row(overrides: Partial<AdminEvaluationListItem> = {}): AdminEvaluationListItem {
  return {
    sessionId: 'sess-1',
    markingId: 'mark-1',
    module: 'writing',
    taskNumber: 2,
    rubricVersion: 'writing-v2',
    recomputedBand: 6.5,
    reportedBand: 6.5,
    flags: [],
    isCurrent: true,
    version: 1,
    markedAt: '2026-09-21T08:00:00Z',
    ...overrides,
  };
}

function page(overrides: Partial<AdminEvaluationPage> = {}): AdminEvaluationPage {
  return {
    items: [row()],
    totalCount: 1,
    page: 1,
    pageSize: 50,
    ...overrides,
  };
}

function renderList() {
  return render(
    <MemoryRouter>
      <EvaluationsPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.mocked(listEvaluations).mockReset();
});

describe('EvaluationsPage', () => {
  it('shows session/marking, skill, recomputed and reported bands, status and flags', async () => {
    vi.mocked(listEvaluations).mockResolvedValue(
      page({
        items: [
          row({
            markingId: 'mark-flagged',
            flags: ['evidencenotgrounded'],
            reportedBand: 7,
            recomputedBand: 6.5,
          }),
        ],
      }),
    );
    renderList();

    expect(await screen.findByRole('link', { name: /mark-fla/ })).toHaveAttribute(
      'href',
      '/evaluations/sess-1/mark-flagged',
    );
    const table = screen.getByRole('table');
    expect(table).toHaveTextContent('Viết');
    expect(table).toHaveTextContent('Task 2');
    expect(table).toHaveTextContent('6.5');
    expect(table).toHaveTextContent('7');
    expect(table).toHaveTextContent('Đang dùng');
    expect(table).toHaveTextContent('Trích dẫn không có trong bài');
  });

  it('renders a superseded row distinctly from a current one', async () => {
    vi.mocked(listEvaluations).mockResolvedValue(
      page({
        items: [row({ markingId: 'old', isCurrent: false, version: 1 })],
      }),
    );
    renderList();

    expect(await screen.findByText('Đã thay')).toBeInTheDocument();
    expect(screen.getByText('v1')).toBeInTheDocument();
  });

  it('sends date, module, flagged and current filters to the server', async () => {
    vi.mocked(listEvaluations).mockResolvedValue(page({ items: [] }));
    renderList();
    await screen.findByText('Chưa có đánh giá nào');

    fireEvent.change(screen.getByLabelText('Từ ngày'), { target: { value: '2026-09-01' } });
    fireEvent.change(screen.getByLabelText('Đến ngày'), { target: { value: '2026-09-21' } });
    fireEvent.change(screen.getByLabelText('Kỹ năng'), { target: { value: 'speaking' } });
    fireEvent.change(screen.getByLabelText('Cờ'), { target: { value: 'true' } });
    fireEvent.change(screen.getByLabelText('Phiên bản'), { target: { value: 'false' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lọc' }));

    expect(listEvaluations).toHaveBeenLastCalledWith('token-1', {
      page: 1,
      from: '2026-09-01',
      to: '2026-09-21',
      module: 'speaking',
      flagged: true,
      current: false,
    });
  });

  it('pages from the server total rather than a client slice', async () => {
    vi.mocked(listEvaluations).mockResolvedValue(
      page({ items: [row()], totalCount: 51, page: 1, pageSize: 50 }),
    );
    renderList();
    expect(await screen.findByText('1 / 2')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Trang sau' }));
    expect(listEvaluations).toHaveBeenLastCalledWith('token-1', { page: 2 });
  });

  it('links to the failed-jobs queue rather than mixing failed rows into this table', async () => {
    vi.mocked(listEvaluations).mockResolvedValue(page());
    renderList();
    await screen.findByRole('table');

    expect(screen.getByRole('link', { name: 'Hàng chờ chấm hỏng' })).toHaveAttribute(
      'href',
      '/evaluations/failed-jobs',
    );
  });

  it('shows a filtered-empty message rather than the never-evaluated copy', async () => {
    vi.mocked(listEvaluations).mockResolvedValue(page({ items: [], totalCount: 0 }));
    renderList();
    await screen.findByText('Chưa có đánh giá nào');

    fireEvent.change(screen.getByLabelText('Kỹ năng'), { target: { value: 'writing' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lọc' }));

    expect(await screen.findByText('Không có đánh giá nào khớp bộ lọc')).toBeInTheDocument();
  });

  it('announces loading and error states without painting a stale table', async () => {
    vi.mocked(listEvaluations).mockReturnValue(new Promise(() => undefined));
    const { unmount } = renderList();
    expect(screen.getByText('Đang tải…')).toBeInTheDocument();
    unmount();

    vi.mocked(listEvaluations).mockRejectedValue(
      new ApiError({
        title: 'Forbidden',
        status: 403,
        detail: 'This account does not hold evaluation.read.',
        code: 'PERMISSION_DENIED',
      }),
    );
    renderList();
    expect(await screen.findByRole('alert')).toHaveTextContent('evaluation.read');
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });
});
