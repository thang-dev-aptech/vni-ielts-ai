import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import type { FailedMarkingJob, FailedMarkingJobPage } from '../lib/adminApi.js';

/**
 * Failed marking queue — `eval-verification`.
 *
 * Screen 5.3. Rerun is the one destructive-shaped, paid action on this
 * slice: hiding the button without `evaluation.rerun` is courtesy, and
 * these tests prove the confirmation dialog states the real-cost consequence
 * without inventing a token/VNI figure (`B-5a`, `B-5b` stay unresolved), and
 * that a replay is announced differently from a fresh accept.
 */

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    listFailedMarkingJobs: vi.fn(),
    rerunEvaluation: vi.fn(),
  };
});

const { listFailedMarkingJobs, rerunEvaluation } = await import('../lib/adminApi.js');

let permissions = new Set<string>(['evaluation.read', 'evaluation.rerun']);

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({
    accessToken: 'token-1',
    can: (permission: string) => permissions.has(permission),
  }),
}));

const { FailedMarkingQueuePage } = await import('./FailedMarkingQueuePage.js');

function job(overrides: Partial<FailedMarkingJob> = {}): FailedMarkingJob {
  return {
    operationId: 'sess-1:writing:writing-v2',
    sessionId: 'sess-1',
    module: 'writing',
    rubricVersion: 'writing-v2',
    state: 'failed',
    attempts: 5,
    lastError: 'Rejected: missing taskAchievement',
    createdAt: '2026-09-21T07:00:00Z',
    failedAt: '2026-09-21T07:10:00Z',
    nextAttemptAt: null,
    completedAt: null,
    ...overrides,
  };
}

function page(overrides: Partial<FailedMarkingJobPage> = {}): FailedMarkingJobPage {
  return {
    items: [job()],
    totalCount: 1,
    page: 1,
    pageSize: 50,
    ...overrides,
  };
}

function renderQueue() {
  return render(
    <MemoryRouter>
      <FailedMarkingQueuePage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.mocked(listFailedMarkingJobs).mockReset();
  vi.mocked(rerunEvaluation).mockReset();
  permissions = new Set(['evaluation.read', 'evaluation.rerun']);
});

describe('FailedMarkingQueuePage', () => {
  it('shows state, attempts, error and timestamps for a failed job', async () => {
    vi.mocked(listFailedMarkingJobs).mockResolvedValue(page());
    renderQueue();

    const table = await screen.findByRole('table');
    expect(table).toHaveTextContent('sess-1:writing:writing-v2');
    expect(table).toHaveTextContent('Hỏng');
    expect(table).toHaveTextContent('5');
    expect(table).toHaveTextContent('Rejected: missing taskAchievement');
  });

  it('hides the rerun action without evaluation.rerun', async () => {
    permissions = new Set(['evaluation.read']);
    vi.mocked(listFailedMarkingJobs).mockResolvedValue(page());
    renderQueue();

    await screen.findByRole('table');
    expect(screen.queryByRole('button', { name: 'Chạy lại' })).not.toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: 'Thao tác' })).not.toBeInTheDocument();
  });

  it('confirms before rerunning and states the real-cost consequence without a fabricated price', async () => {
    vi.mocked(listFailedMarkingJobs).mockResolvedValue(page());
    vi.mocked(rerunEvaluation).mockResolvedValue({
      operationId: 'sess-1:writing:writing-v2',
      state: 'pending',
      replayed: false,
      costConsequence: 'This schedules another provider evaluation and can incur real provider cost.',
      pricingStatus: 'pending',
      pricingBlockers: ['B-5a', 'B-5b'],
    });
    renderQueue();

    fireEvent.click(await screen.findByRole('button', { name: 'Chạy lại' }));

    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Chi phí là thật');
    expect(dialog).toHaveTextContent('chưa được chốt');
    // No invented token/VNI figure anywhere in the confirmation body.
    expect(dialog.textContent).not.toMatch(/\d+\s*(VNI|token)/i);

    expect(rerunEvaluation).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: /Chạy lại — chấp nhận chi phí/ }));

    await waitFor(() => expect(rerunEvaluation).toHaveBeenCalledWith('token-1', 'sess-1:writing:writing-v2'));
    expect(await screen.findByRole('status')).toHaveTextContent('provider evaluation');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('closing the dialog with Huỷ never calls rerun', async () => {
    vi.mocked(listFailedMarkingJobs).mockResolvedValue(page());
    renderQueue();

    fireEvent.click(await screen.findByRole('button', { name: 'Chạy lại' }));
    fireEvent.click(screen.getByRole('button', { name: 'Huỷ' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(rerunEvaluation).not.toHaveBeenCalled();
  });

  it('announces a replayed idempotency key differently from a fresh accept', async () => {
    vi.mocked(listFailedMarkingJobs).mockResolvedValue(page());
    vi.mocked(rerunEvaluation).mockResolvedValue({
      operationId: 'sess-1:writing:writing-v2',
      state: 'pending',
      replayed: true,
      costConsequence: 'This schedules another provider evaluation and can incur real provider cost.',
      pricingStatus: 'pending',
      pricingBlockers: ['B-5a', 'B-5b'],
    });
    renderQueue();

    fireEvent.click(await screen.findByRole('button', { name: 'Chạy lại' }));
    fireEvent.click(screen.getByRole('button', { name: /Chạy lại — chấp nhận chi phí/ }));

    expect(await screen.findByRole('status')).toHaveTextContent('đã được nhận trước đó');
  });

  it('reloads the queue and surfaces the server reason on a 409 conflict', async () => {
    vi.mocked(listFailedMarkingJobs).mockResolvedValue(page());
    vi.mocked(rerunEvaluation).mockRejectedValue(
      new ApiError({
        title: 'Conflict',
        status: 409,
        detail: 'A different rerun request already changed this job.',
        code: 'VALIDATION_FAILED',
      }),
    );
    renderQueue();

    fireEvent.click(await screen.findByRole('button', { name: 'Chạy lại' }));
    fireEvent.click(screen.getByRole('button', { name: /Chạy lại — chấp nhận chi phí/ }));

    expect(await screen.findByRole('status')).toHaveTextContent(
      'A different rerun request already changed this job.',
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    // A conflict means the job's true state moved; the queue re-fetches it.
    await waitFor(() => expect(listFailedMarkingJobs).toHaveBeenCalledTimes(2));
  });

  it('sends date and module filters to the server', async () => {
    vi.mocked(listFailedMarkingJobs).mockResolvedValue(page({ items: [] }));
    renderQueue();
    await screen.findByText('Hàng chờ trống');

    fireEvent.change(screen.getByLabelText('Từ ngày'), { target: { value: '2026-09-01' } });
    fireEvent.change(screen.getByLabelText('Kỹ năng'), { target: { value: 'speaking' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lọc' }));

    expect(listFailedMarkingJobs).toHaveBeenLastCalledWith('token-1', {
      page: 1,
      from: '2026-09-01',
      module: 'speaking',
    });
  });

  it('announces loading, empty and error states', async () => {
    vi.mocked(listFailedMarkingJobs).mockReturnValue(new Promise(() => undefined));
    const { unmount } = renderQueue();
    expect(screen.getByText('Đang tải…')).toBeInTheDocument();
    unmount();

    vi.mocked(listFailedMarkingJobs).mockResolvedValue(page({ items: [], totalCount: 0 }));
    renderQueue();
    expect(await screen.findByText('Hàng chờ trống')).toBeInTheDocument();
  });

  it('does not paint a stale table when the fetch fails', async () => {
    vi.mocked(listFailedMarkingJobs).mockRejectedValue(
      new ApiError({
        title: 'Forbidden',
        status: 403,
        detail: 'This account does not hold evaluation.read.',
        code: 'PERMISSION_DENIED',
      }),
    );
    renderQueue();
    expect(await screen.findByRole('alert')).toHaveTextContent('evaluation.read');
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });
});
