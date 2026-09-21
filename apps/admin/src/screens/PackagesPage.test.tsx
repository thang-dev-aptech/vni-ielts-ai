import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import type { PackageImportHistoryPage, PackageImportHistorySummary } from '../lib/adminApi.js';

/**
 * Package history list — `package-history-ui` / package-verification.
 *
 * `useAdminAuth` and the list call are mocked. Route wiring stays with
 * admin-route-composition; these tests prove the list tells the truth about
 * a page the server already returned.
 */

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({ accessToken: 'token-1' }),
}));

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    listPackageHistory: vi.fn(),
  };
});

const { listPackageHistory } = await import('../lib/adminApi.js');
const { PackagesPage } = await import('./PackagesPage.js');

function row(overrides: Partial<PackageImportHistorySummary> = {}): PackageImportHistorySummary {
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
    findingCount: 2,
    createdAt: '2026-09-21T08:00:00Z',
    updatedAt: '2026-09-21T08:01:00Z',
    ...overrides,
  };
}

function page(overrides: Partial<PackageImportHistoryPage> = {}): PackageImportHistoryPage {
  return {
    items: [row()],
    totalCount: 1,
    page: 1,
    pageSize: 50,
    ...overrides,
  };
}

function renderList() {
  return render(
    <MemoryRouter>
      <PackagesPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.mocked(listPackageHistory).mockReset();
});

describe('PackagesPage', () => {
  it('shows time, uploader, filename, result and stage', async () => {
    vi.mocked(listPackageHistory).mockResolvedValue(page());
    renderList();

    expect(await screen.findByRole('link', { name: 'cambridge-18.zip' })).toHaveAttribute(
      'href',
      '/packages/history/hist-1',
    );
    const table = screen.getByRole('table');
    expect(table).toHaveTextContent('actor-9');
    expect(table).toHaveTextContent('Worker từ chối');
    expect(table).toHaveTextContent('Đối chiếu');
    expect(screen.queryByLabelText('Chọn tệp gói đề')).not.toBeInTheDocument();
  });

  it('renders every result and stage filter label the server recognises', async () => {
    vi.mocked(listPackageHistory).mockResolvedValue(page({ items: [] }));
    renderList();
    await screen.findByText('Chưa có lần tải gói nào');

    const resultSelect = screen.getByLabelText('Kết quả');
    expect(resultSelect).toHaveTextContent('Cửa HTTP từ chối');
    expect(resultSelect).toHaveTextContent('Đang chờ');
    expect(resultSelect).toHaveTextContent('Đang chạy');
    expect(resultSelect).toHaveTextContent('Đã tạo bản nháp');
    expect(resultSelect).toHaveTextContent('Worker từ chối');
    expect(resultSelect).toHaveTextContent('Lỗi vận hành');

    const stageSelect = screen.getByLabelText('Chặng');
    expect(stageSelect).toHaveTextContent('Giải nén');
    expect(stageSelect).toHaveTextContent('Phân tích đề');
    expect(stageSelect).toHaveTextContent('Chép audio');
    expect(stageSelect).toHaveTextContent('Gắn đáp án');
    expect(stageSelect).toHaveTextContent('Đối chiếu');
    expect(stageSelect).toHaveTextContent('Tạo giải thích');
    expect(stageSelect).toHaveTextContent('Hoàn tất');
  });

  it('paints every result/stage variant the list can receive', async () => {
    const variants: Array<{ result: string; stage: string | null; resultLabel: string; stageLabel: string }> = [
      { result: 'DoorRejected', stage: null, resultLabel: 'Cửa HTTP từ chối', stageLabel: '—' },
      { result: 'Queued', stage: 'Extracting', resultLabel: 'Đang chờ', stageLabel: 'Giải nén' },
      { result: 'Running', stage: 'Parsing', resultLabel: 'Đang chạy', stageLabel: 'Phân tích đề' },
      { result: 'Completed', stage: 'Done', resultLabel: 'Đã tạo bản nháp', stageLabel: 'Hoàn tất' },
      { result: 'Rejected', stage: 'Checking', resultLabel: 'Worker từ chối', stageLabel: 'Đối chiếu' },
      { result: 'Failed', stage: 'Keying', resultLabel: 'Lỗi vận hành', stageLabel: 'Gắn đáp án' },
      { result: 'Running', stage: 'Transcribing', resultLabel: 'Đang chạy', stageLabel: 'Chép audio' },
      { result: 'Running', stage: 'Explaining', resultLabel: 'Đang chạy', stageLabel: 'Tạo giải thích' },
    ];

    vi.mocked(listPackageHistory).mockResolvedValue(
      page({
        items: variants.map((v, i) =>
          row({
            historyId: `hist-var-${i}`,
            operationId: `op-var-${i}`,
            originalFileName: `file-${i}.zip`,
            result: v.result,
            stage: v.stage,
            findingCount: i === 0 ? 1 : 0,
          }),
        ),
        totalCount: variants.length,
      }),
    );
    renderList();

    expect(await screen.findByRole('link', { name: 'file-0.zip' })).toBeInTheDocument();
    const table = screen.getByRole('table');
    for (const v of variants) {
      expect(table).toHaveTextContent(v.resultLabel);
      expect(table).toHaveTextContent(v.stageLabel);
    }
    expect(table).toHaveTextContent('1 finding');
  });

  it('sends date, result, stage and uploader filters to the server', async () => {
    vi.mocked(listPackageHistory).mockResolvedValue(page({ items: [] }));
    renderList();
    await screen.findByText('Chưa có lần tải gói nào');

    fireEvent.change(screen.getByLabelText('Kết quả'), { target: { value: 'DoorRejected' } });
    fireEvent.change(screen.getByLabelText('Chặng'), { target: { value: 'Extracting' } });
    fireEvent.change(screen.getByLabelText('Mã người tải'), { target: { value: 'actor-9' } });
    fireEvent.change(screen.getByLabelText('Từ ngày'), { target: { value: '2026-09-01' } });
    fireEvent.change(screen.getByLabelText('Đến ngày'), { target: { value: '2026-09-21' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lọc' }));

    expect(listPackageHistory).toHaveBeenLastCalledWith('token-1', {
      page: 1,
      result: 'DoorRejected',
      stage: 'Extracting',
      uploader: 'actor-9',
      from: '2026-09-01',
      to: '2026-09-21',
    });
  });

  it('pages from the server total rather than a client slice', async () => {
    vi.mocked(listPackageHistory).mockResolvedValue(
      page({
        items: [row()],
        totalCount: 51,
        page: 1,
        pageSize: 50,
      }),
    );
    renderList();
    expect(await screen.findByText('1 / 2')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Trang sau' }));
    expect(listPackageHistory).toHaveBeenLastCalledWith('token-1', { page: 2 });
  });

  it('names unknown uploaders and missing filenames instead of inventing them', async () => {
    vi.mocked(listPackageHistory).mockResolvedValue(
      page({
        items: [row({ actorId: '  ', originalFileName: '', historyId: 'hist-empty' })],
      }),
    );
    renderList();
    expect(await screen.findByText('Không rõ người tải')).toBeInTheDocument();
    expect(screen.getByText('Không có tên tệp')).toBeInTheDocument();
  });

  it('announces loading, empty and error states', async () => {
    vi.mocked(listPackageHistory).mockReturnValue(new Promise(() => undefined));
    const { unmount } = renderList();
    expect(screen.getByText('Đang tải lịch sử gói…')).toBeInTheDocument();
    unmount();

    vi.mocked(listPackageHistory).mockResolvedValue(page({ items: [], totalCount: 0 }));
    renderList();
    expect(await screen.findByText('Chưa có lần tải gói nào')).toBeInTheDocument();
  });

  it('shows a filtered-empty message rather than the never-uploaded copy', async () => {
    vi.mocked(listPackageHistory).mockResolvedValue(page({ items: [], totalCount: 0 }));
    renderList();
    await screen.findByText('Chưa có lần tải gói nào');

    fireEvent.change(screen.getByLabelText('Kết quả'), { target: { value: 'Failed' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lọc' }));

    expect(await screen.findByText('Không có lần tải nào khớp bộ lọc')).toBeInTheDocument();
  });

  it('does not paint an empty table when the fetch fails', async () => {
    vi.mocked(listPackageHistory).mockRejectedValue(
      new ApiError({
        title: 'Forbidden',
        status: 403,
        detail: 'This account does not hold package.read.',
        code: 'PERMISSION_DENIED',
      }),
    );
    renderList();
    expect(await screen.findByRole('alert')).toHaveTextContent('Không đọc được lịch sử gói');
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });
});
