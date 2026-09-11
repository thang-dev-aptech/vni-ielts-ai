import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ImportDraft, ImportJobView } from '../lib/adminApi.js';

/**
 * `ImportPage`, wired to the out-of-band upload from task 9 of the
 * 2026-09-11 import slice.
 *
 * <b>Before this file, every mock here answered a fiction.</b> `POST
 * /packages` used to return a draft synchronously; it now returns `202` with
 * an operation id, and the draft shows up only once a background job that
 * polls `GET /import/jobs/{operationId}` reaches `Completed`. The previous
 * version of this file mocked `uploadImportPackage` resolving straight to an
 * `ImportDraft` — a response shape the server has not sent since the import
 * worker landed — so it passed while testing an endpoint that no longer
 * exists. These tests exercise the responses the server actually sends: a
 * `202`, a poll that progresses through stages, a terminal success, and a
 * terminal failure.
 *
 * <b>`ImportApiError` is the real class, not a mock.</b> `importOriginal` pulls
 * it through unmocked so `error instanceof ImportApiError` in `ImportPage`
 * still works against the errors these tests construct — only the network-
 * calling functions are replaced.
 */

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({ accessToken: 'token-1' }),
}));

vi.mock('../lib/operator.js', () => ({
  useOperator: () => ({
    can: (p: string) => p === 'exam.review',
    isOperator: true,
    name: 'Người duyệt',
    email: 'lead@vni.test',
    previewing: false,
    previewLabel: null,
  }),
}));

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    uploadImportPackage: vi.fn(),
    getImportJob: vi.fn(),
    getImportDraft: vi.fn(),
    downloadImportTemplate: vi.fn(),
    overrideImportWarning: vi.fn(),
    approveImportDraft: vi.fn(),
  };
});

const {
  uploadImportPackage,
  getImportJob,
  getImportDraft,
  downloadImportTemplate,
  overrideImportWarning,
  ImportApiError,
} = await import('../lib/adminApi.js');
const { ImportPage } = await import('../screens/ImportPage.js');

function draft(overrides: Partial<ImportDraft> = {}): ImportDraft {
  return {
    draftId: 'draft-1',
    definitionId: 'def-1',
    versionNumber: 1,
    route: 'structuredpackage',
    approvalState: 'reviewrequired',
    revision: 1,
    reviewedBy: null,
    presentSkills: ['reading'],
    findings: [],
    warnings: [],
    checklistConfirmed: [],
    checklistComplete: false,
    ...overrides,
  };
}

function job(overrides: Partial<ImportJobView> = {}): ImportJobView {
  return {
    operationId: 'op-1',
    definitionId: 'def-1',
    versionNumber: 1,
    stage: 'Extracting',
    state: 'Running',
    attempts: 0,
    maxAttempts: 3,
    draftId: null,
    lastError: null,
    createdAt: '2026-09-11T00:00:00Z',
    nextAttemptAt: null,
    completedAt: null,
    ...overrides,
  };
}

function chooseFile(name = 'package.zip') {
  const file = new File(['zip bytes'], name, { type: 'application/zip' });
  const input = document.querySelector('input[type="file"]') as HTMLInputElement;
  fireEvent.change(input, { target: { files: [file] } });
}

describe('ImportPage', () => {
  beforeEach(() => {
    vi.mocked(uploadImportPackage).mockReset();
    vi.mocked(getImportJob).mockReset();
    vi.mocked(getImportDraft).mockReset();
    vi.mocked(downloadImportTemplate).mockReset();
    vi.mocked(overrideImportWarning).mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows the accepted job, polls it through a stage change, then renders the draft it names', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });

    vi.mocked(uploadImportPackage).mockResolvedValue({
      operationId: 'op-1',
      definitionId: 'def-1',
      versionNumber: 1,
      stage: 'Extracting',
      state: 'Pending',
    });
    vi.mocked(getImportJob)
      .mockResolvedValueOnce(job({ stage: 'Extracting', state: 'Running', attempts: 0 }))
      .mockResolvedValueOnce(job({ stage: 'Parsing', state: 'Running', attempts: 0 }))
      .mockResolvedValueOnce(
        job({ stage: 'Done', state: 'Completed', attempts: 0, draftId: 'draft-1' }),
      );
    vi.mocked(getImportDraft).mockResolvedValue(draft());

    render(<ImportPage />);
    chooseFile();
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và kiểm' }));

    // The first job fetch happens synchronously off the accepted upload —
    // no interval tick needed to see the first stage.
    expect(await screen.findByText(/Đang giải nén gói/)).toBeInTheDocument();

    await act(async () => { await vi.advanceTimersByTimeAsync(15_000); });
    expect(await screen.findByText(/Đang phân tích đề/)).toBeInTheDocument();

    await act(async () => { await vi.advanceTimersByTimeAsync(15_000); });
    expect(await screen.findByText('Bản nháp draft-1')).toBeInTheDocument();
    expect(getImportDraft).toHaveBeenCalledWith('token-1', 'draft-1');

    // Polling stopped: a completed job must not keep asking.
    const callsAtCompletion = vi.mocked(getImportJob).mock.calls.length;
    await act(async () => { await vi.advanceTimersByTimeAsync(120_000); });
    expect(vi.mocked(getImportJob).mock.calls.length).toBe(callsAtCompletion);
  });

  /**
   * Red-when-removed target: rendering the draft panel whenever `draft` is
   * non-null regardless of `job.state`, or rendering it from a `Failed`
   * job's stale `draftId`, makes this fail — a failed job must show its
   * reason, never a half-built draft.
   */
  it('shows the failed job\'s own reason and never renders a draft for it', async () => {
    vi.mocked(uploadImportPackage).mockResolvedValue({
      operationId: 'op-2',
      definitionId: 'def-2',
      versionNumber: 1,
      stage: 'Parsing',
      state: 'Running',
    });
    vi.mocked(getImportJob).mockResolvedValue(
      job({
        operationId: 'op-2',
        stage: 'Parsing',
        state: 'Failed',
        attempts: 3,
        // A stale draft id from an earlier attempt of the same operation —
        // present on purpose, so the assertion below only passes if the
        // code gates the draft fetch on `state === 'Completed'` and not
        // merely on `draftId !== null`.
        draftId: 'draft-stale',
        lastError: 'Parsing: the provider returned a 503 three times.',
      }),
    );

    render(<ImportPage />);
    chooseFile();
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và kiểm' }));

    expect(await screen.findByText('Nhập gói thất bại.')).toBeInTheDocument();
    expect(
      screen.getByText(/the provider returned a 503 three times/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/^Bản nháp/)).not.toBeInTheDocument();
    expect(getImportDraft).not.toHaveBeenCalled();
  });

  /**
   * Red-when-removed target: a polling loop with no cap, or one that keeps
   * calling `getImportJob` past the bound, makes the assertion on the call
   * count fail — this is the "does not poll forever" requirement, proven by
   * counting real calls rather than trusting a message alone.
   */
  it('stops polling at the bound and tells the operator, rather than polling forever', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });

    vi.mocked(uploadImportPackage).mockResolvedValue({
      operationId: 'op-3',
      definitionId: 'def-3',
      versionNumber: 1,
      stage: 'Transcribing',
      state: 'Running',
    });
    vi.mocked(getImportJob).mockResolvedValue(
      job({ operationId: 'op-3', stage: 'Transcribing', state: 'Running' }),
    );

    render(<ImportPage />);
    chooseFile();
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và kiểm' }));

    await screen.findByText(/Đang chuyển băng ghi âm/);

    // One call from the immediate post-upload fetch, then up to 60 more on
    // the interval — comfortably past that bounds the loop (15s × 60 ≈ 15
    // minutes; see `IMPORT_POLL_MAX`'s own comment for why this job's bound
    // is longer than the Writing marking screen's).
    await act(async () => { await vi.advanceTimersByTimeAsync(15_000 * 65); });

    expect(vi.mocked(getImportJob).mock.calls.length).toBe(61);

    const callsAtBound = vi.mocked(getImportJob).mock.calls.length;
    await act(async () => { await vi.advanceTimersByTimeAsync(15_000 * 5); });
    expect(vi.mocked(getImportJob).mock.calls.length).toBe(callsAtBound);
  });

  /**
   * Fix round 2 on task 9: the lapsed-bound state must read as "still
   * running", not as a failure and not as silence. Red-when-removed target:
   * wording the message as a timeout ("hết thời gian", "thất bại", "lỗi") or
   * dropping it back to nothing when the bound is hit makes this fail — a
   * bound that is normally exceeded (this job routinely runs longer than the
   * poll window) must not read as an error to the one person watching it.
   */
  it('says the job is still running when the poll bound lapses, not that it failed', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });

    vi.mocked(uploadImportPackage).mockResolvedValue({
      operationId: 'op-5',
      definitionId: 'def-5',
      versionNumber: 1,
      stage: 'Explaining',
      state: 'Running',
    });
    vi.mocked(getImportJob).mockResolvedValue(
      job({ operationId: 'op-5', stage: 'Explaining', state: 'Running' }),
    );

    render(<ImportPage />);
    chooseFile();
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và kiểm' }));

    await screen.findByText(/Đang tạo giải thích/);

    await act(async () => { await vi.advanceTimersByTimeAsync(15_000 * 65); });

    const message = await screen.findByText(/vẫn đang chạy ở phía máy chủ/);
    expect(message).toBeInTheDocument();
    expect(message.textContent).not.toMatch(/thất bại|lỗi|hết thời gian|hết hạn/i);
    expect(screen.getByRole('button', { name: 'Kiểm tra lại' })).toBeInTheDocument();
    // The badge still reads the job's real state — "still running" is not
    // dressed up as done, either.
    expect(screen.getByText('Đang chạy')).toBeInTheDocument();
  });

  it('downloads the package template so nobody has to guess a folder name', async () => {
    const bytes = new Blob(['pretend zip bytes'], { type: 'application/zip' });
    vi.mocked(downloadImportTemplate).mockResolvedValue(bytes);

    const createdUrl = 'blob:mock-template-url';
    const createObjectURL = vi.fn().mockReturnValue(createdUrl);
    const revokeObjectURL = vi.fn();
    vi.stubGlobal('URL', { ...URL, createObjectURL, revokeObjectURL });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

    render(<ImportPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Tải mẫu gói (.zip)' }));

    await waitFor(() => expect(downloadImportTemplate).toHaveBeenCalledWith('token-1'));
    expect(createObjectURL).toHaveBeenCalledWith(bytes);
    expect(click).toHaveBeenCalled();
    await waitFor(() => expect(revokeObjectURL).toHaveBeenCalledWith(createdUrl));

    click.mockRestore();
    vi.unstubAllGlobals();
  });

  it('renders the specific PACKAGE_REJECTED message, not a generic failure', async () => {
    vi.mocked(uploadImportPackage).mockRejectedValue(
      new ImportApiError(
        {
          title: 'Package refused',
          status: 422,
          detail: 'Path traversal detected at /../../etc/passwd.',
          code: 'PACKAGE_REJECTED',
        },
        [{ severity: 'error', code: 'PATH_TRAVERSAL', path: '/../../etc/passwd', message: 'Path traversal detected at /../../etc/passwd.' }],
      ),
    );

    render(<ImportPage />);
    chooseFile();
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và kiểm' }));

    expect(await screen.findByText(/Path traversal detected/)).toBeInTheDocument();
    expect(screen.queryByText(/Không thực hiện được/)).not.toBeInTheDocument();
  });

  /**
   * Part C's red-when-removed target: reverting `RejectionPanel`'s
   * `AI_PARSER_UNAVAILABLE` branch to a generic fallback makes this fail —
   * the raw-document-package case must read as "no AI parser wired in", not
   * as an ordinary package rejection.
   */
  it('renders the AI_PARSER_UNAVAILABLE case as its own specific message', async () => {
    const message =
      'AI-assisted parsing of raw exam source documents (docx/pdf/txt) is not wired into this ' +
      'deployment. Upload a package that already contains a single ready exam.json instead, ' +
      'or produce one with the operator CLI (backend/tools/Vni.Ielts.ExamImporter) and upload that.';

    vi.mocked(uploadImportPackage).mockRejectedValue(
      new ImportApiError(
        { title: 'Package refused', status: 422, detail: message, code: 'PACKAGE_REJECTED' },
        [{ severity: 'error', code: 'AI_PARSER_UNAVAILABLE', path: '/', message }],
      ),
    );

    render(<ImportPage />);
    chooseFile('raw-documents.zip');
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và kiểm' }));

    expect(await screen.findByText('Chỉ nhận gói đã có sẵn exam.json.')).toBeInTheDocument();
    expect(screen.getByText(new RegExp(message.slice(0, 40)))).toBeInTheDocument();
  });

  it('blocks a warning override until a reason is typed', async () => {
    vi.mocked(uploadImportPackage).mockResolvedValue({
      operationId: 'op-4',
      definitionId: 'def-1',
      versionNumber: 1,
      stage: 'Done',
      state: 'Completed',
    });
    vi.mocked(getImportJob).mockResolvedValue(
      job({ operationId: 'op-4', stage: 'Done', state: 'Completed', draftId: 'draft-1' }),
    );

    const withWarning = draft({
      warnings: [
        {
          id: 'w1',
          category: 'assetmapping',
          path: '/sections/0',
          message: 'Asset không khớp.',
          resolved: false,
          overrideReason: null,
        },
      ],
    });
    vi.mocked(getImportDraft).mockResolvedValue(withWarning);
    vi.mocked(overrideImportWarning).mockResolvedValue(
      draft({
        warnings: [
          {
            id: 'w1',
            category: 'assetmapping',
            path: '/sections/0',
            message: 'Asset không khớp.',
            resolved: true,
            overrideReason: 'Đã đối chiếu thủ công với file gốc.',
          },
        ],
      }),
    );

    render(<ImportPage />);
    chooseFile();
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và kiểm' }));
    await screen.findByText(/Asset không khớp\./);

    fireEvent.click(screen.getByRole('button', { name: 'Bỏ qua, có lý do' }));

    const confirm = screen.getByRole('dialog').querySelector('.cms-primary');
    expect(confirm).toBeDisabled();

    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: 'Đã đối chiếu thủ công với file gốc.' },
    });
    expect(confirm).toBeEnabled();

    fireEvent.click(confirm!);
    await waitFor(() =>
      expect(overrideImportWarning).toHaveBeenCalledWith(
        'token-1',
        'draft-1',
        'w1',
        'Đã đối chiếu thủ công với file gốc.',
      ),
    );
  });
});
