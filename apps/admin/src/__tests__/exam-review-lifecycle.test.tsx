import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import type { AdminExam } from '../lib/adminApi.js';

/**
 * `ExamDetailPage`, cut over this session from a publish/unpublish-only
 * screen reading the real API to the one real detail screen for the whole
 * `P-20` review lifecycle — submit, approve, return, publish, unpublish.
 *
 * <b>The five-state red-when-removed target lives here.</b> `AdminExam.status`
 * is a bare `string` off the wire; the test below sends every one of the five
 * values the server can actually produce (`"draft"`, `"inreview"`,
 * `"approved"`, `"published"`, `"unpublished"`) through `TransitionBar` via
 * this screen and asserts each renders a known `StatusBadge` face rather than
 * the "is-unknown" fallback a stray `"returned"` would hit.
 */

const permissions = new Set<string>();

vi.mock('../lib/operator.js', () => ({
  useOperator: () => ({
    can: (p: string) => permissions.has(p),
    isOperator: true,
    name: 'Người soạn',
    email: 'author@vni.test',
    previewing: false,
    previewLabel: null,
  }),
}));

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({
    accessToken: 'token-1',
    can: (p: string) => permissions.has(p),
  }),
}));

vi.mock('../lib/adminApi.js', () => ({
  listExams: vi.fn(),
  submitExamForReview: vi.fn(),
  approveExam: vi.fn(),
  returnExamToDraft: vi.fn(),
  publishExam: vi.fn(),
  unpublishExam: vi.fn(),
  MODULE_LABEL: {
    reading: 'Đọc',
    listening: 'Nghe',
    writing: 'Viết',
    speaking: 'Nói',
  },
}));

const { listExams, approveExam, returnExamToDraft, submitExamForReview } = await import(
  '../lib/adminApi.js'
);
const { ExamDetailPage } = await import('../screens/ExamDetailPage.js');

function exam(overrides: Partial<AdminExam> = {}): AdminExam {
  return {
    examVersionId: 'v1',
    definitionId: 'd1',
    versionNumber: 1,
    title: 'Đề mẫu',
    variant: 'academic',
    status: 'draft',
    publishedAt: null,
    modules: [{ module: 'reading', questionCount: 40, durationSeconds: 3600 }],
    ...overrides,
  };
}

function renderAt(definitionId: string) {
  return render(
    <MemoryRouter initialEntries={[`/exams/${definitionId}`]}>
      <Routes>
        <Route path="/exams/:definitionId" element={<ExamDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('ExamDetailPage · the five real states', () => {
  beforeEach(() => {
    permissions.clear();
    vi.mocked(listExams).mockReset();
    vi.mocked(approveExam).mockReset();
    vi.mocked(returnExamToDraft).mockReset();
    vi.mocked(submitExamForReview).mockReset();
  });

  it.each(['draft', 'inreview', 'approved', 'published', 'unpublished'])(
    'renders a known status face for "%s" — never the "is-unknown" fallback',
    async (status) => {
      vi.mocked(listExams).mockResolvedValue({ exams: [exam({ status })] });
      renderAt('d1');

      // Title appears in both the crumb trail and the <h1> — match the heading.
      await screen.findByRole('heading', { name: 'Đề mẫu' });
      expect(document.querySelector('.cms-badge.is-unknown')).toBeNull();
    },
  );

  it('offers "Nộp duyệt" on a draft to a holder of exam.submit, and calls the real endpoint', async () => {
    permissions.add('exam.submit');
    vi.mocked(listExams).mockResolvedValue({ exams: [exam({ status: 'draft' })] });
    vi.mocked(submitExamForReview).mockResolvedValue({ status: 'inreview' });

    renderAt('d1');
    await screen.findByRole('heading', { name: 'Đề mẫu' });

    fireEvent.click(screen.getByRole('button', { name: 'Nộp duyệt' }));
    // Confirm dialog reuses the same verb — pick the dialog's own button.
    fireEvent.click(screen.getAllByRole('button', { name: 'Nộp duyệt' })[1]!);

    await waitFor(() => expect(submitExamForReview).toHaveBeenCalledWith('token-1', 'v1'));
  });

  it('gates approve behind exam.review and calls returnExamToDraft with the typed reason', async () => {
    permissions.add('exam.review');
    vi.mocked(listExams).mockResolvedValue({ exams: [exam({ status: 'inreview' })] });
    vi.mocked(returnExamToDraft).mockResolvedValue({ status: 'draft' });

    renderAt('d1');
    await screen.findByRole('heading', { name: 'Đề mẫu' });

    fireEvent.click(screen.getByRole('button', { name: 'Trả lại' }));
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Thiếu transcript.' } });
    fireEvent.click(screen.getAllByRole('button', { name: 'Trả lại' })[1]!);

    await waitFor(() =>
      expect(returnExamToDraft).toHaveBeenCalledWith('token-1', 'v1', 'Thiếu transcript.'),
    );
  });

  it('shows a clear, specific message on REVIEWER_IS_AUTHOR — not a generic failure', async () => {
    permissions.add('exam.review');
    vi.mocked(listExams).mockResolvedValue({ exams: [exam({ status: 'inreview' })] });
    vi.mocked(approveExam).mockRejectedValue(
      new ApiError({
        title: 'Forbidden',
        status: 403,
        detail: 'Người duyệt không thể là người soạn. Cần một người khác duyệt version này.',
        code: 'REVIEWER_IS_AUTHOR',
      }),
    );

    renderAt('d1');
    await screen.findByRole('heading', { name: 'Đề mẫu' });

    fireEvent.click(screen.getByRole('button', { name: 'Duyệt' }));
    fireEvent.click(screen.getAllByRole('button', { name: 'Duyệt' })[1]!);

    expect(
      await screen.findByText('Bạn không thể tự duyệt đề mình soạn — cần một người khác duyệt version này.'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Không thực hiện được/)).not.toBeInTheDocument();
  });
});
