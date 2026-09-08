import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ImportDraft } from '../lib/adminApi.js';

/**
 * `ImportPage`, wired to the real `POST /api/v1/admin/import/packages` this
 * session — the button used to be permanently `disabled` with a comment
 * saying the ZIP door had not been built.
 *
 * <b>`ImportApiError` is the real class, not a mock.</b> `importOriginal` pulls
 * it through unmocked so `error instanceof ImportApiError` in `ImportPage`
 * still works against the errors these tests construct — only the four
 * network-calling functions are replaced.
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
    overrideImportWarning: vi.fn(),
    setImportChecklist: vi.fn(),
    approveImportDraft: vi.fn(),
  };
});

const { uploadImportPackage, overrideImportWarning, setImportChecklist, ImportApiError } =
  await import('../lib/adminApi.js');
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

function chooseFile(name = 'package.zip') {
  const file = new File(['zip bytes'], name, { type: 'application/zip' });
  const input = document.querySelector('input[type="file"]') as HTMLInputElement;
  fireEvent.change(input, { target: { files: [file] } });
}

function renderPage() {
  return render(
    <MemoryRouter>
      <ImportPage />
    </MemoryRouter>,
  );
}

describe('ImportPage', () => {
  const checklistLabels = [
    'Câu hỏi',
    'Lựa chọn',
    'Giới hạn từ',
    'Biến thể đáp án chấp nhận',
    'Transcript và bằng chứng',
    'Ánh xạ tài nguyên',
  ];

  beforeEach(() => {
    vi.mocked(uploadImportPackage).mockReset();
    vi.mocked(overrideImportWarning).mockReset();
    vi.mocked(setImportChecklist).mockReset();
  });

  it('renders the returned draft\'s findings and warnings on a successful upload', async () => {
    vi.mocked(uploadImportPackage).mockResolvedValue(
      draft({
        warnings: [
          {
            id: 'w1',
            category: 'transcriptandevidence',
            path: '/sections/0/parts/1',
            message: 'Thiếu transcript.',
            resolved: false,
            overrideReason: null,
          },
        ],
      }),
    );

    renderPage();
    chooseFile();
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và kiểm' }));

    expect(await screen.findByText('Bản nháp draft-1')).toBeInTheDocument();
    expect(screen.getByText(/Thiếu transcript\./)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Bỏ qua, có lý do' })).toBeInTheDocument();
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
        [
          {
            severity: 'error',
            code: 'PATH_TRAVERSAL',
            path: '/../../etc/passwd',
            message: 'Path traversal detected at /../../etc/passwd.',
          },
        ],
      ),
    );

    renderPage();
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

    renderPage();
    chooseFile('raw-documents.zip');
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và kiểm' }));

    expect(await screen.findByText('Chỉ nhận gói đã có sẵn exam.json.')).toBeInTheDocument();
    expect(screen.getByText(new RegExp(message.slice(0, 40)))).toBeInTheDocument();
  });

  it('blocks a warning override until a reason is typed', async () => {
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
    vi.mocked(uploadImportPackage).mockResolvedValue(withWarning);
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

    renderPage();
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

  it('enables Duyệt after every checklist item is confirmed', async () => {
    vi.mocked(uploadImportPackage).mockResolvedValue(draft({ checklistComplete: false }));
    vi.mocked(setImportChecklist).mockImplementation(async (_token, _draftId, confirmed) =>
      draft({
        checklistConfirmed: confirmed,
        checklistComplete: confirmed.length === 6,
      }),
    );

    renderPage();
    chooseFile();
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và kiểm' }));
    await screen.findByText('Bản nháp draft-1');

    expect(screen.getByRole('button', { name: 'Duyệt' })).toBeDisabled();

    for (const label of checklistLabels) {
      fireEvent.click(screen.getByRole('checkbox', { name: label }));
      await waitFor(() => expect(screen.getByRole('checkbox', { name: label })).toBeChecked());
    }

    await waitFor(() => expect(screen.getByRole('button', { name: 'Duyệt' })).toBeEnabled());
    const lastConfirmed = vi.mocked(setImportChecklist).mock.calls.at(-1)?.[2] ?? [];
    expect(lastConfirmed).toHaveLength(6);
    expect(lastConfirmed).toEqual(
      expect.arrayContaining([
        'questions',
        'options',
        'wordlimits',
        'acceptedvariants',
        'transcriptandevidence',
        'assetmapping',
      ]),
    );
  });

  it('keeps Duyệt disabled and reports remaining checklist items', async () => {
    vi.mocked(uploadImportPackage).mockResolvedValue(draft({ checklistComplete: false }));
    vi.mocked(setImportChecklist).mockResolvedValue(
      draft({ checklistConfirmed: ['questions'], checklistComplete: false }),
    );

    renderPage();
    chooseFile();
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và kiểm' }));
    await screen.findByText('Bản nháp draft-1');

    expect(screen.getByText('Còn 6 mục checklist chưa xác nhận.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Duyệt' })).toBeDisabled();

    fireEvent.click(screen.getByRole('checkbox', { name: 'Câu hỏi' }));

    expect(await screen.findByText('Còn 5 mục checklist chưa xác nhận.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Duyệt' })).toBeDisabled();
    expect(setImportChecklist).toHaveBeenCalledWith('token-1', 'draft-1', ['questions']);
  });
});
