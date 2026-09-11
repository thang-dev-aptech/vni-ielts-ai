import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import { AdminPaths } from '../routes/paths.js';
import { candidateOf } from './parsedCandidate.js';

const permissions = new Set<string>(['package.read', 'exam.review']);
let previewing = false;

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({ accessToken: 'test-token' }),
}));

vi.mock('../lib/operator.js', () => ({
  useOperator: () => ({
    can: (key: string) => permissions.has(key),
    isOperator: true,
    name: 'Trưởng chuyên môn',
    email: 'lead@vni.test',
    previewing,
    previewLabel: previewing ? 'Trưởng chuyên môn' : null,
  }),
}));

const getPackageCandidate = vi.fn();
const correctPackageCandidate = vi.fn();
const rejectPackageCandidate = vi.fn();
const confirmPackageCandidate = vi.fn();
const createCandidateDraft = vi.fn();

vi.mock('../lib/adminApi.js', () => ({
  getPackageCandidate: (...args: unknown[]) => getPackageCandidate(...args),
  correctPackageCandidate: (...args: unknown[]) => correctPackageCandidate(...args),
  rejectPackageCandidate: (...args: unknown[]) => rejectPackageCandidate(...args),
  confirmPackageCandidate: (...args: unknown[]) => confirmPackageCandidate(...args),
  createCandidateDraft: (...args: unknown[]) => createCandidateDraft(...args),
}));

const { CandidateReviewPage } = await import('../screens/CandidateReviewPage.js');

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/packages/pkg-1/candidates/cand-1']}>
      <Routes>
        <Route path={AdminPaths.candidatePattern} element={<CandidateReviewPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  permissions.clear();
  permissions.add('package.read');
  permissions.add('exam.review');
  permissions.add('exam.create');
  previewing = false;
  getPackageCandidate.mockReset();
  correctPackageCandidate.mockReset();
  rejectPackageCandidate.mockReset();
  confirmPackageCandidate.mockReset();
  createCandidateDraft.mockReset();
});

describe('CandidateReviewPage', () => {
  it('reloads on an optimistic conflict and does not overwrite', async () => {
    getPackageCandidate.mockResolvedValue(candidateOf());
    confirmPackageCandidate.mockRejectedValue(
      new ApiError({
        title: 'Conflict',
        status: 409,
        detail: 'The parsed candidate changed after it was loaded. Reload it and try again.',
        code: 'PARSED_CANDIDATE_VERSION_CONFLICT',
      }),
    );

    renderPage();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Xác nhận đề xuất' })).toBeEnabled());

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Xác nhận đề xuất' }));
    });

    await waitFor(() =>
      expect(screen.getByText(/Đề đề xuất đã đổi sau khi bạn mở/)).toBeInTheDocument(),
    );
    expect(confirmPackageCandidate).toHaveBeenCalledOnce();

    getPackageCandidate.mockResolvedValue(candidateOf({ version: 2, title: 'Reading 1 (reloaded)' }));
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Tải lại' }));
    });
    await waitFor(() => expect(screen.getByLabelText('Tiêu đề')).toHaveValue('Reading 1 (reloaded)'));
    expect(getPackageCandidate).toHaveBeenCalledTimes(2);
  });

  it('does not send a mutation while a role is being previewed', async () => {
    previewing = true;
    getPackageCandidate.mockResolvedValue(candidateOf());
    renderPage();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Lưu sửa đổi' })).toBeInTheDocument());

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Lưu sửa đổi' }));
    });

    await waitFor(() => expect(screen.getByText(/Đang xem trước vai trò/)).toBeInTheDocument());
    expect(correctPackageCandidate).not.toHaveBeenCalled();
  });

  it('hides review actions for an author and never offers publish', async () => {
    permissions.delete('exam.review');
    getPackageCandidate.mockResolvedValue(candidateOf());
    renderPage();

    await waitFor(() => expect(screen.getByLabelText('Tiêu đề')).toBeInTheDocument());
    expect(screen.getByText(/thiếu quyền/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Xác nhận đề xuất' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Xuất bản' })).not.toBeInTheDocument();
  });

  it('shows completion form when confirmed and creates draft', async () => {
    getPackageCandidate.mockResolvedValue(candidateOf({ status: 'confirmed' }));
    createCandidateDraft.mockResolvedValue({
      examVersionId: 'ev-created-123',
      definitionId: 'ed-123',
      versionNumber: 1,
      status: 'draft',
    });

    renderPage();

    await waitFor(() =>
      expect(screen.getByText(/Hoàn thiện thông số chuẩn hoá để tạo bản nháp/)).toBeInTheDocument(),
    );
    expect(screen.getByRole('button', { name: /Tạo bản nháp đề thi/ })).toBeEnabled();

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /Tạo bản nháp đề thi/ }));
    });

    expect(createCandidateDraft).toHaveBeenCalledOnce();
    await waitFor(() =>
      expect(screen.getByText(/Đã tạo bản nháp đề thi thành công!/)).toBeInTheDocument(),
    );
    expect(screen.getByText(/ev-created-123/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Đi đến bản nháp đề thi' })).toHaveAttribute(
      'href',
      '/my-exams/ev-created-123/builder',
    );
  });

  it('displays validation findings when draft creation returns 422', async () => {
    getPackageCandidate.mockResolvedValue(candidateOf({ status: 'confirmed' }));
    createCandidateDraft.mockRejectedValue(
      new ApiError({
        title: 'Candidate completion invalid',
        status: 422,
        detail: 'The canonical exam definition failed schema or invariant validation.',
        code: 'CANDIDATE_COMPLETION_INVALID',
        errors: [
          {
            path: 'timing.sections.reading',
            code: 'SECTION_TIMING_INVALID',
            message: 'Reading duration must be at least 30 minutes.',
          },
        ],
      }),
    );

    renderPage();

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Tạo bản nháp đề thi/ })).toBeEnabled(),
    );

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /Tạo bản nháp đề thi/ }));
    });

    await waitFor(() =>
      expect(screen.getByText(/The canonical exam definition failed schema/)).toBeInTheDocument(),
    );
    expect(screen.getByText(/Reading duration must be at least 30 minutes./)).toBeInTheDocument();
  });
});
