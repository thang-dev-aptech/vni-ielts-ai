import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import type { ImportFinding, PackageImportHistoryDetail } from '../lib/adminApi.js';
import { AdminPaths } from '../routes/paths.js';

/**
 * One history row — `package-history-ui` / package-verification.
 *
 * Distinguishes every result/stage outcome from the stored result and
 * findings, and never implies a rejected ZIP can be downloaded.
 */

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({ accessToken: 'token-1' }),
}));

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    getPackageHistory: vi.fn(),
  };
});

const { getPackageHistory } = await import('../lib/adminApi.js');
const { PackageHistoryDetailPage } = await import('./PackageHistoryDetailPage.js');

function finding(overrides: Partial<ImportFinding> = {}): ImportFinding {
  return {
    severity: 'error',
    code: 'PATH_ESCAPE',
    path: '/entries/0',
    message: 'A path left the archive root.',
    ...overrides,
  };
}

function detail(overrides: Partial<PackageImportHistoryDetail> = {}): PackageImportHistoryDetail {
  return {
    historyId: 'hist-1',
    operationId: 'op-1',
    actorId: 'actor-9',
    originalFileName: 'cambridge-18.zip',
    definitionId: 'def-1',
    versionNumber: 1,
    sourceSha256: 'abc',
    draftId: null,
    stage: 'Checking',
    result: 'Rejected',
    createdAt: '2026-09-21T08:00:00Z',
    updatedAt: '2026-09-21T08:01:00Z',
    findings: [finding()],
    ...overrides,
  };
}

function renderDetail(historyId = 'hist-1') {
  return render(
    <MemoryRouter initialEntries={[AdminPaths.packageHistory(historyId)]}>
      <Routes>
        <Route path={AdminPaths.packageHistoryPattern} element={<PackageHistoryDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.mocked(getPackageHistory).mockReset();
});

describe('PackageHistoryDetailPage', () => {
  it('names door rejection, worker rejection, blocked draft, completed draft and operational failure apart', async () => {
    const cases: Array<{ row: PackageImportHistoryDetail; title: string }> = [
      { row: detail({ result: 'DoorRejected', stage: null, operationId: null, findings: [finding()] }), title: 'Cửa HTTP từ chối' },
      { row: detail({ result: 'Rejected' }), title: 'Worker từ chối khi kiểm' },
      {
        row: detail({
          result: 'Completed',
          draftId: 'draft-1',
          stage: 'Done',
          findings: [finding({ code: 'IMPORT_FINDINGS_BLOCKING', message: 'Blocking findings remain.' })],
        }),
        title: 'Bản nháp bị chặn',
      },
      {
        row: detail({
          result: 'Completed',
          draftId: 'draft-2',
          stage: 'Done',
          findings: [finding({ severity: 'warning', code: 'UNUSED_ASSET', message: 'An asset is unused.' })],
        }),
        title: 'Đã tạo bản nháp',
      },
      { row: detail({ result: 'Failed', draftId: null, findings: [] }), title: 'Lỗi vận hành' },
    ];

    for (const { row, title } of cases) {
      vi.mocked(getPackageHistory).mockResolvedValueOnce(row);
      const { unmount } = renderDetail();
      expect(await screen.findByRole('heading', { level: 2, name: title })).toBeInTheDocument();
      unmount();
    }
  });

  it('names queued and running in-flight results', async () => {
    const cases: Array<{ row: PackageImportHistoryDetail; title: string; stageLabel: string }> = [
      {
        row: detail({ result: 'Queued', stage: 'Extracting', findings: [], draftId: null }),
        title: 'Đang chờ xử lý',
        stageLabel: 'Giải nén',
      },
      {
        row: detail({ result: 'Running', stage: 'Parsing', findings: [], draftId: null }),
        title: 'Đang chạy',
        stageLabel: 'Phân tích đề',
      },
      {
        row: detail({ result: 'Running', stage: 'Transcribing', findings: [], draftId: null }),
        title: 'Đang chạy',
        stageLabel: 'Chép audio',
      },
      {
        row: detail({ result: 'Running', stage: 'Explaining', findings: [], draftId: null }),
        title: 'Đang chạy',
        stageLabel: 'Tạo giải thích',
      },
    ];

    for (const { row, title, stageLabel } of cases) {
      vi.mocked(getPackageHistory).mockResolvedValueOnce(row);
      const { unmount } = renderDetail();
      expect(await screen.findByRole('heading', { level: 2, name: title })).toBeInTheDocument();
      expect(screen.getByText(stageLabel)).toBeInTheDocument();
      expect(screen.getByText(/Gói không được giữ lại/)).toBeInTheDocument();
      unmount();
    }
  });

  it('renders every persisted finding and does not offer a download on a rejected upload', async () => {
    vi.mocked(getPackageHistory).mockResolvedValue(detail());
    renderDetail();

    expect(await screen.findByText('PATH_ESCAPE')).toBeInTheDocument();
    expect(screen.getByText('error')).toBeInTheDocument();
    expect(screen.getByText('/entries/0')).toBeInTheDocument();
    expect(screen.getByText('A path left the archive root.')).toBeInTheDocument();
    expect(screen.getByText(/Gói không được giữ lại/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /tải/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /tải xuống/i })).not.toBeInTheDocument();
  });

  it('says when findings are empty rather than inventing a table', async () => {
    vi.mocked(getPackageHistory).mockResolvedValue(
      detail({ result: 'Queued', stage: 'Extracting', findings: [] }),
    );
    renderDetail();
    expect(await screen.findByText('Không có finding nào được ghi cho lần tải này.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('says honestly when the uploader or filename is missing', async () => {
    vi.mocked(getPackageHistory).mockResolvedValue(
      detail({ actorId: '', originalFileName: '   ', result: 'Failed', findings: [] }),
    );
    renderDetail();
    expect(await screen.findByRole('heading', { level: 1, name: 'Không có tên tệp' })).toBeInTheDocument();
    expect(screen.getByText('Không rõ người tải')).toBeInTheDocument();
  });

  it('treats a 404 as missing rather than as a generic error', async () => {
    vi.mocked(getPackageHistory).mockRejectedValue(
      new ApiError({
        title: 'Not Found',
        status: 404,
        detail: 'Not found.',
        code: 'NOT_FOUND',
      }),
    );
    renderDetail();
    expect(await screen.findByText('Không tìm thấy lần tải này')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('announces loading and a fetch error', async () => {
    vi.mocked(getPackageHistory).mockReturnValue(new Promise(() => undefined));
    const { unmount } = renderDetail();
    expect(screen.getByText('Đang tải lần tải gói…')).toBeInTheDocument();
    unmount();

    vi.mocked(getPackageHistory).mockRejectedValue(
      new ApiError({
        title: 'Forbidden',
        status: 403,
        detail: 'This account does not hold package.read.',
        code: 'PERMISSION_DENIED',
      }),
    );
    renderDetail();
    expect(await screen.findByRole('alert')).toHaveTextContent('Không đọc được lần tải này');
  });
});
