import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { AdminExamPreview } from '../lib/adminApi.js';

const getExamPreview = vi.fn();

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({
    accessToken: 'test-token',
    can: () => true,
  }),
}));

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    getExamPreview: (...args: unknown[]) => getExamPreview(...args),
  };
});

const { ExamModulePreviewScreen } = await import('../screens/ExamModulePreviewScreen.js');

function mockPreviewData(overrides: Partial<AdminExamPreview> = {}): AdminExamPreview {
  return {
    examVersionId: 'ev-123',
    title: 'Cambridge IELTS 18 Test 1',
    sections: [
      {
        module: 'reading',
        parts: [
          {
            order: 1,
            title: 'Passage 1: Urban Farming',
            body: 'Urban farming has emerged as a revolutionary approach to food production...',
            transcript: null,
            cueCard: null,
            questions: [
              {
                id: 'q-1',
                order: 1,
                type: 'multiple-choice',
                prompt: 'What is the main benefit of vertical agriculture?',
                options: [
                  { key: 'A', text: 'It requires more fossil fuels' },
                  { key: 'B', text: 'It saves water and space' },
                  { key: 'C', text: 'It produces less yield' },
                ],
                answerKey: [{ single: 'B', all: null, pairLeft: null, pairRight: null }],
              },
              {
                id: 'q-2',
                order: 2,
                type: 'true-false-notgiven',
                prompt: 'Vertical farms only operate during daytime.',
                options: [],
                answerKey: [{ single: 'FALSE', all: null, pairLeft: null, pairRight: null }],
              },
            ],
          },
          {
            order: 2,
            title: 'Passage 2: History of Glass',
            body: 'Glassmaking dates back several millennia...',
            transcript: null,
            cueCard: null,
            questions: [
              {
                id: 'q-3',
                order: 3,
                type: 'completion',
                prompt: 'The earliest glass beads were discovered in [gap].',
                options: [],
                answerKey: [{ single: 'Egypt', all: null, pairLeft: null, pairRight: null }],
              },
            ],
          },
        ],
      },
      {
        module: 'listening',
        parts: [
          {
            order: 1,
            title: 'Section 1: Hotel Booking',
            body: null,
            transcript: 'Agent: Good morning, City Hotel reservations.\nCustomer: Hello, I would like to book a room.',
            cueCard: null,
            questions: [
              {
                id: 'q-10',
                order: 1,
                type: 'short-answer',
                prompt: 'Customer name:',
                options: [],
                answerKey: [{ single: 'John Smith', all: null, pairLeft: null, pairRight: null }],
              },
            ],
          },
        ],
      },
      {
        module: 'speaking',
        parts: [
          {
            order: 2,
            title: 'Part 2: Individual Long Turn',
            body: null,
            transcript: null,
            cueCard: {
              topic: 'Describe a memorable journey you took.',
              bullets: [
                'Where you went',
                'Who you went with',
                'What you did there',
                'And explain why it was memorable',
              ],
            },
            questions: [],
          },
        ],
      },
    ],
    ...overrides,
  };
}

function renderScreen(examVersionId = 'ev-123', module = 'reading') {
  return render(
    <MemoryRouter initialEntries={[`/exam-preview/${examVersionId}/${module}`]}>
      <Routes>
        <Route
          path="/exam-preview/:examVersionId/:module"
          element={<ExamModulePreviewScreen />}
        />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  getExamPreview.mockReset();
});

describe('ExamModulePreviewScreen', () => {
  it('renders reading preview with parts navigation, passage, questions and answer key', async () => {
    getExamPreview.mockResolvedValue(mockPreviewData());
    renderScreen('ev-123', 'reading');

    await waitFor(() => {
      expect(screen.getByText(/Cambridge IELTS 18 Test 1 · Kỹ năng Đọc/)).toBeInTheDocument();
    });

    // Part nav pills
    expect(screen.getByRole('button', { name: 'Part 1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Part 2' })).toBeInTheDocument();

    // Source passage
    expect(screen.getByText('Passage 1: Urban Farming')).toBeInTheDocument();
    expect(screen.getByText(/Urban farming has emerged/)).toBeInTheDocument();

    // Question 1
    expect(screen.getByText('What is the main benefit of vertical agriculture?')).toBeInTheDocument();
    expect(screen.getByText('It saves water and space')).toBeInTheDocument();
    expect(screen.getByText('Trắc nghiệm một đáp án')).toBeInTheDocument();

    // Answer key
    expect(screen.getAllByText('Đáp án đúng:').length).toBeGreaterThan(0);
    expect(screen.getAllByText('B').length).toBeGreaterThanOrEqual(2);
  });

  it('renders listening preview with transcript details', async () => {
    getExamPreview.mockResolvedValue(mockPreviewData());
    renderScreen('ev-123', 'listening');

    await waitFor(() => {
      expect(screen.getByText(/Cambridge IELTS 18 Test 1 · Kỹ năng Nghe/)).toBeInTheDocument();
    });

    expect(screen.getByText('Section 1: Hotel Booking')).toBeInTheDocument();
    expect(screen.getByText('Bản ghi lời thoại (Transcript)')).toBeInTheDocument();
    expect(screen.getByText(/City Hotel reservations/)).toBeInTheDocument();
    expect(screen.getByText('Customer name:')).toBeInTheDocument();
    expect(screen.getByText('John Smith')).toBeInTheDocument();
  });

  it('renders speaking preview with cue card', async () => {
    getExamPreview.mockResolvedValue(mockPreviewData());
    renderScreen('ev-123', 'speaking');

    await waitFor(() => {
      expect(screen.getByText(/Cambridge IELTS 18 Test 1 · Kỹ năng Nói/)).toBeInTheDocument();
    });

    expect(screen.getByText('Chủ đề nói (Cue Card)')).toBeInTheDocument();
    expect(screen.getByText('Describe a memorable journey you took.')).toBeInTheDocument();
    expect(screen.getByText('Where you went')).toBeInTheDocument();
  });

  it('shows empty message when the requested module is not found in the exam', async () => {
    getExamPreview.mockResolvedValue(mockPreviewData());
    renderScreen('ev-123', 'writing');

    await waitFor(() => {
      expect(screen.getByText('Không tìm thấy nội dung kỹ năng này.')).toBeInTheDocument();
    });
  });

  it('shows error alert if API fails', async () => {
    getExamPreview.mockRejectedValue(new Error('Network error'));
    renderScreen('ev-123', 'reading');

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent('Không tải được nội dung đề.');
    });
  });

  it('keeps multiline, Unicode, long tokens and HTML-like copy as text nodes', async () => {
    getExamPreview.mockResolvedValue(
      mockPreviewData({
        sections: [
          {
            module: 'reading',
            parts: [
              {
                order: 1,
                title: 'Đoạn 1 — Nông nghiệp đô thị',
                body: 'Paragraph one.\n\nParagraph two with ____ gaps.\nhttps://example.test/very-long-unbroken-token-path',
                transcript: null,
                cueCard: null,
                questions: [
                  {
                    id: 'q-html',
                    order: 1,
                    type: 'multiple-choice',
                    prompt: '<script>alert(1)</script>\nDòng hai',
                    options: [
                      { key: 'A', text: 'Cà phê\nvà trà' },
                      { key: 'B', text: 'Đáp án dài không xuống dòng xen kẽ' },
                    ],
                    answerKey: [
                      { single: 'B', all: null, pairLeft: null, pairRight: null },
                      { single: 'colour', all: null, pairLeft: null, pairRight: null },
                    ],
                  },
                  {
                    id: 'q-empty',
                    order: 2,
                    type: 'short-answer',
                    prompt: 'Không có đáp án',
                    options: [],
                    answerKey: [],
                  },
                  {
                    id: 'q-all',
                    order: 3,
                    type: 'multiple-select',
                    prompt: 'Chọn đủ',
                    options: [],
                    answerKey: [{ single: null, all: ['A', 'C'], pairLeft: null, pairRight: null }],
                  },
                  {
                    id: 'q-pair',
                    order: 4,
                    type: 'matching',
                    prompt: 'Nối',
                    options: [],
                    answerKey: [{ single: null, all: null, pairLeft: 'i', pairRight: 'Heading 1' }],
                  },
                ],
              },
            ],
          },
        ],
      }),
    );
    const { container } = renderScreen('ev-123', 'reading');

    await waitFor(() => expect(screen.getByText(/Paragraph one/)).toBeInTheDocument());
    expect(screen.getByText(/Paragraph two with ____ gaps/)).toBeInTheDocument();
    expect(screen.getByText(/Đoạn 1 — Nông nghiệp đô thị/)).toBeInTheDocument();
    expect(screen.getByText((content) => content.includes('<script>alert(1)</script>'))).toBeInTheDocument();
    expect(screen.getByText((content) => content.includes('Dòng hai'))).toBeInTheDocument();
    expect(container.querySelectorAll('script')).toHaveLength(0);
    expect(screen.getByText('Đáp án đúng')).toBeInTheDocument();
    expect(screen.queryByText(/hoặc/)).not.toBeInTheDocument();
    expect(screen.getByText('colour')).toBeInTheDocument();
    expect(screen.getByText('—')).toBeInTheDocument();
    expect(screen.getByText('A + C')).toBeInTheDocument();
    expect(screen.getByText('i → Heading 1')).toBeInTheDocument();
  });
});
