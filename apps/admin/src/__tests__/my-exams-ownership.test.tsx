import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

const auth = {
  accessToken: 'test-token' as string | null,
  user: null as { userId: string; displayName: string; email: string } | null,
};

const listExams = vi.fn();
const createExam = vi.fn();

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => auth,
}));

vi.mock('../lib/operator.js', () => ({
  useOperator: () => ({
    can: (permission: string) => permission === 'exam.read.own' || permission === 'exam.create',
    isOperator: true,
    name: 'Người soạn',
    email: 'author@vni.test',
    previewing: false,
    previewLabel: null,
  }),
}));

vi.mock('../lib/adminApi.js', () => ({
  listExams: (...args: unknown[]) => listExams(...args),
  getExam: vi.fn(),
  createExam: (...args: unknown[]) => createExam(...args),
  deleteExam: vi.fn(),
  submitExam: vi.fn(),
  withdrawExam: vi.fn(),
  returnExam: vi.fn(),
  approveExam: vi.fn(),
  unapproveExam: vi.fn(),
  publishExam: vi.fn(),
  unpublishExam: vi.fn(),
  resumeExam: vi.fn(),
}));

const { MyExamsPage } = await import('../screens/MyExamsPage.js');

function renderPage() {
  return render(
    <MemoryRouter>
      <MyExamsPage />
    </MemoryRouter>,
  );
}

const ownedDraft = {
  examVersionId: 'ev-owned',
  definitionId: 'ed-owned',
  versionNumber: 1,
  title: 'ZIP Draft của người xác nhận',
  variant: 'academic',
  status: 'draft',
  authorId: 'author-1',
  createdByName: 'Người xác nhận',
  submittedAt: null,
  reviewedAt: null,
  reviewedByName: null,
  publishedAt: null,
  reviewNotes: [],
  modules: [{ module: 'reading', questionCount: 1, durationSeconds: 60 }],
};

beforeEach(() => {
  auth.accessToken = 'test-token';
  auth.user = { userId: 'author-1', displayName: 'Người xác nhận', email: 'author@vni.test' };
  listExams.mockReset().mockResolvedValue({ exams: [ownedDraft] });
  createExam.mockReset();
});

describe('MyExamsPage ownership mapping', () => {
  it('does not call listExams when the access token exists but the account id has not loaded', () => {
    auth.user = null;
    renderPage();
    // INT clears the spinner when userId is missing (does not hang on "Đang tải…").
    expect(listExams).not.toHaveBeenCalled();
    expect(screen.queryByText('ZIP Draft của người xác nhận')).not.toBeInTheDocument();
  });

  it('lists the API-returned Draft owned by the signed-in confirmer via authorId', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('ZIP Draft của người xác nhận')).toBeInTheDocument());
    expect(screen.queryByText('Bạn chưa soạn đề nào')).not.toBeInTheDocument();
    expect(listExams).toHaveBeenCalledWith('test-token');
  });
});
