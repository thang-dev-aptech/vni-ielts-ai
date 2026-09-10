import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

const navigateMock = vi.fn();

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return {
    ...actual,
    useNavigate: () => navigateMock,
  };
});

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({ accessToken: 'token-1' }),
}));

const operatorCan = vi.fn((p: string) => p === 'package.upload' || p === 'exam.create');

vi.mock('../lib/operator.js', () => ({
  useOperator: () => ({
    can: (p: string) => operatorCan(p),
    isOperator: true,
    name: 'Người vận hành',
    email: 'ops@vni.test',
    previewing: false,
    previewLabel: null,
  }),
}));

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    uploadPackage: vi.fn(),
  };
});

const { uploadPackage, ImportApiError } = await import('../lib/adminApi.js');
const { ImportPage } = await import('../screens/ImportPage.js');

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
  beforeEach(() => {
    navigateMock.mockReset();
    vi.mocked(uploadPackage).mockReset();
    operatorCan.mockImplementation((p: string) => p === 'package.upload' || p === 'exam.create');
  });

  it('does not render the "Bản nháp đã nhập" draft table', () => {
    renderPage();
    expect(screen.queryByText('Bản nháp đã nhập')).not.toBeInTheDocument();
    expect(screen.queryByText(/Danh sách này là draft/)).not.toBeInTheDocument();
  });

  it('uploads chosen package via uploadPackage and navigates to package detail', async () => {
    vi.mocked(uploadPackage).mockResolvedValue({
      packageId: 'pkg-durable-123',
      status: 'uploaded',
    });

    renderPage();
    chooseFile('cambridge19.zip');
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và xử lý' }));

    await waitFor(() => {
      expect(uploadPackage).toHaveBeenCalledWith('token-1', expect.any(File));
    });
    expect(navigateMock).toHaveBeenCalledWith('/packages/pkg-durable-123');
  });

  it('renders the specific PACKAGE_REJECTED message, not a generic failure', async () => {
    vi.mocked(uploadPackage).mockRejectedValue(
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
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và xử lý' }));

    expect(await screen.findByText(/Path traversal detected/)).toBeInTheDocument();
    expect(screen.queryByText(/Không thực hiện được/)).not.toBeInTheDocument();
  });

  it('renders the AI_PARSER_UNAVAILABLE case as its own specific message', async () => {
    const message =
      'AI-assisted parsing of raw exam source documents (docx/pdf/txt) is not wired into this ' +
      'deployment. Upload a package that already contains a single ready exam.json instead, ' +
      'or produce one with the operator CLI (backend/tools/Vni.Ielts.ExamImporter) and upload that.';

    vi.mocked(uploadPackage).mockRejectedValue(
      new ImportApiError(
        { title: 'Package refused', status: 422, detail: message, code: 'PACKAGE_REJECTED' },
        [{ severity: 'error', code: 'AI_PARSER_UNAVAILABLE', path: '/', message }],
      ),
    );

    renderPage();
    chooseFile('raw-documents.zip');
    fireEvent.click(screen.getByRole('button', { name: 'Tải lên và xử lý' }));

    expect(await screen.findByText('Chỉ nhận gói đã có sẵn exam.json.')).toBeInTheDocument();
    expect(screen.getByText(new RegExp(message.slice(0, 40)))).toBeInTheDocument();
  });

  it('disables upload button when operator lacks exam.create or package.upload', () => {
    operatorCan.mockImplementation((p: string) => p === 'package.upload'); // lacks exam.create

    renderPage();
    chooseFile();

    const uploadBtn = screen.getByRole('button', { name: 'Tải lên và xử lý' });
    expect(uploadBtn).toBeDisabled();
    expect(screen.getByRole('status')).toHaveTextContent(
      /Bạn cần cả hai quyền package\.upload và exam\.create để tải gói lên\./,
    );
  });
});
