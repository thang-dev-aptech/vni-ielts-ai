import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

const permissions = new Set<string>(['exam.update.own', 'exam.preview', 'exam.submit', 'media.read']);

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({ accessToken: 'test-token' }),
}));

vi.mock('../lib/operator.js', () => ({
  useOperator: () => ({
    can: (permission: string) => permissions.has(permission),
    isOperator: true,
    name: 'Người soạn',
    email: 'author@vni.test',
    previewing: false,
    previewLabel: null,
  }),
}));

const getExam = vi.fn();
const getExamContent = vi.fn();
const listMedia = vi.fn();
const saveExamContent = vi.fn();
const validateExamContent = vi.fn();
const submitExam = vi.fn();

vi.mock('../lib/adminApi.js', () => ({
  getExam: (...args: unknown[]) => getExam(...args),
  getExamContent: (...args: unknown[]) => getExamContent(...args),
  listMedia: (...args: unknown[]) => listMedia(...args),
  saveExamContent: (...args: unknown[]) => saveExamContent(...args),
  validateExamContent: (...args: unknown[]) => validateExamContent(...args),
  submitExam: (...args: unknown[]) => submitExam(...args),
}));

vi.mock('../components/ExamPreview.js', () => ({
  ExamPreview: () => <div>Bản xem thử</div>,
}));

const { QuestionBuilderPage } = await import('../screens/QuestionBuilderPage.js');

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/my-exams/ev-1/builder']}>
      <Routes>
        <Route path="/my-exams/:versionId/builder" element={<QuestionBuilderPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  permissions.clear();
  permissions.add('exam.update.own');
  permissions.add('exam.preview');
  permissions.add('exam.submit');
  getExam.mockReset().mockResolvedValue({
    examVersionId: 'ev-1',
    definitionId: 'ed-1',
    versionNumber: 1,
    title: 'Practice 024',
    variant: 'academic',
    status: 'draft',
    createdByName: 'Người soạn',
    reviewNotes: [],
    modules: [],
    assets: [],
  });
  getExamContent.mockReset().mockResolvedValue({
    formatVersion: '1.0',
    title: 'Practice 024',
    variant: 'academic',
    timingProfile: { sections: {} },
    scoringProfile: { rawToBand: {} },
    sections: [],
  });
  listMedia.mockReset().mockResolvedValue([]);
  saveExamContent.mockReset().mockResolvedValue({ valid: false, findings: [] });
  validateExamContent.mockReset().mockResolvedValue({
    valid: false,
    findings: [
      {
        stage: 'schema',
        code: 'SCHEMA_INVALID',
        pointer: '/sections',
        message: 'minItems: at least 1 item',
      },
    ],
  });
  submitExam.mockReset();
});

describe('QuestionBuilderPage', () => {
  it('renders the three-column workspace with checklist codes from the import pipeline', async () => {
    renderPage();

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Practice 024' })).toBeInTheDocument());
    expect(screen.getByRole('tab', { name: 'Cấu trúc' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Soạn' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Kiểm tra' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText('SCHEMA_INVALID')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Lưu nháp' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Xem thử như học viên' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Nộp duyệt' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Xuất bản/i })).not.toBeInTheDocument();
  });

  it('offers the first four Reading presets from the picker', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByRole('button', { name: '+ Thêm kỹ năng' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: '+ Thêm kỹ năng' }));
    fireEvent.click(screen.getByRole('button', { name: 'reading' }));
    await waitFor(() => expect(screen.getByRole('button', { name: '+ Thêm câu hỏi' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: '+ Thêm câu hỏi' }));
    expect(screen.getByText('Multiple Choice — một đáp án')).toBeInTheDocument();
    expect(screen.getByText('Multiple Choice — nhiều đáp án')).toBeInTheDocument();
    expect(screen.getByText('True / False / Not Given')).toBeInTheDocument();
    expect(screen.getByText('Short Answer')).toBeInTheDocument();
    expect(screen.getByText('Matching Headings')).toBeInTheDocument();
    expect(screen.getByText('Diagram / Map / Plan Labelling')).toBeInTheDocument();
    expect(screen.queryByText('Writing Task 1')).not.toBeInTheDocument();
  });

  it('offers Listening, Writing and Speaking presets from their pickers', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByRole('button', { name: '+ Thêm kỹ năng' })).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: '+ Thêm kỹ năng' }));
    fireEvent.click(screen.getByRole('button', { name: 'listening' }));
    await waitFor(() => expect(screen.getByRole('button', { name: '+ Thêm câu hỏi' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: '+ Thêm câu hỏi' }));
    expect(screen.getByText('Sentence Completion')).toBeInTheDocument();
    expect(screen.queryByText('True / False / Not Given')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Huỷ' }));

    fireEvent.click(screen.getByRole('button', { name: '+ Thêm kỹ năng' }));
    fireEvent.click(screen.getByRole('button', { name: 'writing' }));
    const writingAdd = screen.getAllByRole('button', { name: '+ Thêm câu hỏi' }).at(-1);
    fireEvent.click(writingAdd!);
    expect(screen.getByText('Writing Task 1')).toBeInTheDocument();
    expect(screen.getByText('Writing Task 2')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Huỷ' }));

    fireEvent.click(screen.getByRole('button', { name: '+ Thêm kỹ năng' }));
    fireEvent.click(screen.getByRole('button', { name: 'speaking' }));
    const speakingAdd = screen.getAllByRole('button', { name: '+ Thêm câu hỏi' }).at(-1);
    fireEvent.click(speakingAdd!);
    expect(screen.getByText('Speaking Part 1')).toBeInTheDocument();
    expect(screen.getByText('Speaking Part 2')).toBeInTheDocument();
    expect(screen.getByText('Speaking Part 3')).toBeInTheDocument();
  });

  it('announces save state and marks the selected tree node', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Practice 024' })).toBeInTheDocument());
    const saveStatus = screen.getByText(/Chưa lưu|Tự lưu khi schema hợp lệ|Đang lưu|Không lưu/);
    expect(saveStatus).toHaveAttribute('aria-live', 'polite');

    fireEvent.click(screen.getByRole('button', { name: '+ Thêm kỹ năng' }));
    fireEvent.click(screen.getByRole('button', { name: 'reading' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Reading' })).toHaveAttribute('aria-current', 'true'));
  });

  it('lets a checklist pointer select the matching node', async () => {
    validateExamContent.mockResolvedValue({
      valid: false,
      findings: [
        {
          stage: 'schema',
          code: 'SCHEMA_INVALID',
          pointer: '/sections/0',
          message: 'duration missing',
        },
      ],
    });
    renderPage();
    await waitFor(() => expect(screen.getByRole('button', { name: '+ Thêm kỹ năng' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: '+ Thêm kỹ năng' }));
    fireEvent.click(screen.getByRole('button', { name: 'reading' }));
    await waitFor(() => expect(screen.getByText('SCHEMA_INVALID')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: /SCHEMA_INVALID/ }));
    expect(screen.getByRole('button', { name: 'Reading' })).toHaveAttribute('aria-current', 'true');
  });

  it('blocks editing on a non-Draft and never offers Publish', async () => {
    getExam.mockResolvedValue({
      examVersionId: 'ev-1',
      definitionId: 'ed-1',
      versionNumber: 1,
      title: 'Practice 024',
      variant: 'academic',
      status: 'inreview',
      createdByName: 'Người soạn',
      reviewNotes: [],
      modules: [],
      assets: [],
    });
    renderPage();
    await waitFor(() =>
      expect(screen.getByText('Chỉ bản nháp mới sửa được. Mở vòng đời đề nếu cần rút về nháp.')).toBeInTheDocument(),
    );
    expect(screen.queryByRole('button', { name: '+ Thêm kỹ năng' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Lưu nháp' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Nộp duyệt' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: /Xuất bản/i })).not.toBeInTheDocument();
  });
});
