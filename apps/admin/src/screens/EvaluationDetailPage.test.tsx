import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import type { AdminEvaluationDetail } from '../lib/adminApi.js';

/**
 * Evaluation detail — `eval-verification`.
 *
 * <b>The redaction contract is the point of this file.</b> The server omits
 * `evidence` / `ungroundedEvidence` / `learnerSubmission` / `rawOutput`
 * unless the caller both asked for `includeContent` and held
 * `learner-content.read` (`AdminEvaluationEndpoints.DetailEndpoint`). This
 * screen must never imply it has content it was not given — it renders an
 * absent block as absent, not as empty, and it must never call `getEvaluation`
 * with `includeContent: true` itself (that is an explicit, audited operator
 * action the CMS spec places elsewhere).
 */

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({ accessToken: 'token-1' }),
}));

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    getEvaluation: vi.fn(),
  };
});

const { getEvaluation } = await import('../lib/adminApi.js');
const { EvaluationDetailPage } = await import('./EvaluationDetailPage.js');

function detail(overrides: Partial<AdminEvaluationDetail> = {}): AdminEvaluationDetail {
  return {
    sessionId: 'sess-1',
    markingId: 'mark-2',
    module: 'writing',
    taskNumber: 2,
    rubricVersion: 'writing-v2',
    recomputedBand: 6.5,
    reportedBand: 6.5,
    flags: [],
    isCurrent: true,
    version: 2,
    markedAt: '2026-09-21T08:00:00Z',
    criteria: [
      { criterion: 'taskResponse', band: 6.5, feedback: 'Đủ ý.', evidence: ['câu trích dẫn'] },
    ],
    advisories: [],
    provenance: { promptVersion: 'writing-eval-prompt-v2', providerSection: 'OpenAi' },
    supersedesId: 'mark-1',
    supersededById: null,
    attempts: [],
    ...overrides,
  };
}

function renderDetail(sessionId = 'sess-1', markingId = 'mark-2') {
  return render(
    <MemoryRouter initialEntries={[`/evaluations/${sessionId}/${markingId}`]}>
      <Routes>
        <Route path="/evaluations/:sessionId/:markingId" element={<EvaluationDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.mocked(getEvaluation).mockReset();
});

describe('EvaluationDetailPage', () => {
  it('fetches without includeContent — the screen never requests protected content itself', async () => {
    vi.mocked(getEvaluation).mockResolvedValue(detail());
    renderDetail();

    await screen.findByRole('heading', { name: /Viết/ });
    const [accessArg, sessionArg, markingArg, includeContentArg] =
      vi.mocked(getEvaluation).mock.calls[0]!;
    expect(accessArg).toBe('token-1');
    expect(sessionArg).toBe('sess-1');
    expect(markingArg).toBe('mark-2');
    expect(includeContentArg ?? false).toBe(false);
  });

  it('shows the recomputed band as the score in use and flags a mismatch against the reported band', async () => {
    vi.mocked(getEvaluation).mockResolvedValue(detail({ recomputedBand: 6.5, reportedBand: 7 }));
    renderDetail();

    await screen.findByRole('heading', { name: /Viết/ });
    expect(screen.getByText('Khác điểm tính lại')).toBeInTheDocument();
  });

  it('does not flag a mismatch when the reported band matches the recomputed one', async () => {
    vi.mocked(getEvaluation).mockResolvedValue(detail({ recomputedBand: 6.5, reportedBand: 6.5 }));
    renderDetail();

    await screen.findByRole('heading', { name: /Viết/ });
    expect(screen.queryByText('Khác điểm tính lại')).not.toBeInTheDocument();
  });

  it('does not flag a mismatch when no reported band was ever claimed', async () => {
    vi.mocked(getEvaluation).mockResolvedValue(detail({ reportedBand: null }));
    renderDetail();

    await screen.findByRole('heading', { name: /Viết/ });
    expect(screen.queryByText('Khác điểm tính lại')).not.toBeInTheDocument();
  });

  it('redacts evidence, ungrounded evidence and the learner submission by default', async () => {
    // The default GET's shape: evidence/ungroundedEvidence/learnerSubmission
    // are absent keys entirely — never null, never an empty array/object.
    // `detail()` already omits the latter two; only `evidence` needs erasing.
    const withoutContent = detail({
      criteria: [{ criterion: 'taskResponse', band: 6.5, feedback: 'Đủ ý.' }],
    });
    vi.mocked(getEvaluation).mockResolvedValue(withoutContent);
    renderDetail();

    await screen.findByRole('heading', { name: /Viết/ });
    expect(screen.getByText('Không có trong phản hồi này')).toBeInTheDocument();
    expect(screen.getByText('Trích dẫn từ bài làm không có trong phản hồi này.')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Bài làm' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Trích dẫn không bám bài' })).not.toBeInTheDocument();
  });

  it('renders evidence and the learner submission once the server includes them', async () => {
    vi.mocked(getEvaluation).mockResolvedValue(
      detail({
        criteria: [
          {
            criterion: 'taskResponse',
            band: 6.5,
            feedback: 'Đủ ý.',
            evidence: ['space research delivers practical benefits'],
          },
        ],
        ungroundedEvidence: [],
        learnerSubmission: { 'w-task-2-slot-1': 'It is often argued that...' },
      }),
    );
    renderDetail();

    await screen.findByRole('heading', { name: /Viết/ });
    expect(screen.getByText('space research delivers practical benefits')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Bài làm' })).toBeInTheDocument();
    expect(screen.getByText('It is often argued that...')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Trích dẫn không bám bài' })).toBeInTheDocument();
    expect(screen.getByText('Không có.')).toBeInTheDocument();
  });

  it('labels rejected raw model output as rejected, never as a score, and shows it only when the server includes it', async () => {
    vi.mocked(getEvaluation).mockResolvedValue(
      detail({
        attempts: [
          {
            id: 'att-1',
            taskNumber: 2,
            provider: 'OpenAi',
            model: 'deepseek-v4-pro',
            requestId: 'req-1',
            startedAt: '2026-09-21T07:00:00Z',
            finishedAt: '2026-09-21T07:00:10Z',
            outcome: 'rejected',
            errorCode: 'CRITERION_SET_MISMATCH',
            errorMessage: 'Missing: taskResponse. Unexpected: taskAchievement.',
            rawOutputTruncated: false,
            rawOutput: '{"criteria":{"taskAchievement":{"band":47}}}',
            markingId: null,
            markingVersion: null,
          },
        ],
      }),
    );
    renderDetail();

    await screen.findByRole('heading', { name: /Viết/ });
    expect(screen.getByText('Đầu ra thô bị từ chối — không dùng làm điểm.', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('{"criteria":{"taskAchievement":{"band":47}}}')).toBeInTheDocument();
    expect(screen.getByText('Bị từ chối')).toBeInTheDocument();
  });

  it('shows a rejected outcome without a raw-output block when the server omitted it (unauthorized content)', async () => {
    vi.mocked(getEvaluation).mockResolvedValue(
      detail({
        attempts: [
          {
            id: 'att-2',
            taskNumber: 2,
            provider: 'OpenAi',
            model: 'deepseek-v4-pro',
            requestId: 'req-2',
            startedAt: '2026-09-21T07:00:00Z',
            finishedAt: '2026-09-21T07:00:10Z',
            outcome: 'rejected',
            errorCode: 'CRITERION_SET_MISMATCH',
            errorMessage: 'Missing: taskResponse.',
            rawOutputTruncated: false,
            markingId: null,
            markingVersion: null,
          },
        ],
      }),
    );
    renderDetail();

    await screen.findByRole('heading', { name: /Viết/ });
    expect(screen.getByText('Đầu ra thô bị từ chối — không dùng làm điểm.', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('Phần thân đầu ra không có trong phản hồi này.')).toBeInTheDocument();
  });

  it('links "Thay cho" / "Bị thay bởi" to the sibling marking within the same session', async () => {
    vi.mocked(getEvaluation).mockResolvedValue(
      detail({ supersedesId: 'mark-1', supersededById: 'mark-3' }),
    );
    renderDetail();

    await screen.findByRole('heading', { name: /Viết/ });
    expect(screen.getByRole('link', { name: 'mark-1' })).toHaveAttribute(
      'href',
      '/evaluations/sess-1/mark-1',
    );
    expect(screen.getByRole('link', { name: 'mark-3' })).toHaveAttribute(
      'href',
      '/evaluations/sess-1/mark-3',
    );
  });

  it('shows an em dash for both supersession links on a marking with no history', async () => {
    vi.mocked(getEvaluation).mockResolvedValue(
      detail({ supersedesId: null, supersededById: null }),
    );
    renderDetail();

    await screen.findByRole('heading', { name: /Viết/ });
    expect(screen.queryByRole('link', { name: /mark-/ })).not.toBeInTheDocument();
  });

  it('shows the "not found" state on a 404 rather than an empty screen', async () => {
    vi.mocked(getEvaluation).mockRejectedValue(
      new ApiError({ title: 'Not Found', status: 404, detail: '', code: 'NOT_FOUND' }),
    );
    renderDetail();

    expect(await screen.findByText('Không tìm thấy đánh giá này')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Về danh sách đánh giá' })).toHaveAttribute(
      'href',
      '/evaluations',
    );
  });

  it('shows a readable error rather than a blank screen on a non-404 failure', async () => {
    vi.mocked(getEvaluation).mockRejectedValue(
      new ApiError({
        title: 'Forbidden',
        status: 403,
        detail: 'This account does not hold evaluation.read.',
        code: 'PERMISSION_DENIED',
      }),
    );
    renderDetail();

    expect(await screen.findByRole('alert')).toHaveTextContent('evaluation.read');
  });
});
