import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({
    accessToken: 'test-token',
    can: (permission: string) => permission === 'package.delete',
  }),
}));

const listPackages = vi.fn();
const listExams = vi.fn();
const deletePackage = vi.fn();

vi.mock('../lib/adminApi.js', () => ({
  listPackages: (...args: unknown[]) => listPackages(...args),
  listExams: (...args: unknown[]) => listExams(...args),
  deletePackage: (...args: unknown[]) => deletePackage(...args),
}));

const { PackagesPage } = await import('../screens/PackagesPage.js');

function renderPage() {
  return render(
    <MemoryRouter>
      <PackagesPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  listPackages.mockReset();
  listExams.mockReset().mockResolvedValue({ exams: [] });
  deletePackage.mockReset();
});

describe('PackagesPage', () => {
  it('shows an empty state when nothing has been uploaded', async () => {
    listPackages.mockResolvedValue([]);
    renderPage();

    await waitFor(() => expect(screen.getByText('Chưa có gói nào')).toBeInTheDocument());
  });

  it('shows an error state when the list fails to load', async () => {
    listPackages.mockRejectedValue(new Error('network down'));
    renderPage();

    await waitFor(() => expect(screen.getByText('Không tải được lịch sử gói.')).toBeInTheDocument());
  });

  it('lists packages with their status and outcome', async () => {
    listPackages.mockResolvedValue([
      {
        packageId: 'pkg-1',
        sourceKind: 'zip',
        fileName: 'two-exams.zip',
        status: 'imported',
        uploadedByName: 'Người vận hành',
        findings: [],
        entries: [],
        createdVersionIds: ['ev-1', 'ev-2'],
        createdAt: '2026-08-28T00:00:00.000Z',
        updatedAt: '2026-08-28T00:00:00.000Z',
      },
    ]);
    renderPage();

    await waitFor(() => expect(screen.getByText('two-exams.zip')).toBeInTheDocument());
    expect(screen.getByText('Đã tạo bản nháp')).toBeInTheDocument();
    expect(screen.getByText('2 bản nháp')).toBeInTheDocument();
  });

  it('labels parsing and needs-review without inventing a second palette', async () => {
    listPackages.mockResolvedValue([
      {
        packageId: 'pkg-parse',
        sourceKind: 'zip',
        fileName: 'raw.zip',
        status: 'parsing',
        uploadedByName: 'Người vận hành',
        findings: [],
        entries: [],
        createdVersionIds: [],
        createdAt: '2026-08-28T00:00:00.000Z',
        updatedAt: '2026-08-28T00:00:00.000Z',
      },
      {
        packageId: 'pkg-review',
        sourceKind: 'zip',
        fileName: 'raw-2.zip',
        status: 'needs-review',
        uploadedByName: 'Người vận hành',
        findings: [],
        entries: [],
        createdVersionIds: [],
        createdAt: '2026-08-28T00:00:00.000Z',
        updatedAt: '2026-08-28T00:00:00.000Z',
      },
    ]);
    renderPage();

    await waitFor(() => expect(screen.getByText('raw.zip')).toBeInTheDocument());
    expect(screen.getByText('Đang phân tích')).toBeInTheDocument();
    expect(screen.getByText('Cần rà soát')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'raw-2.zip' })).toHaveAttribute(
      'href',
      '/packages/pkg-review',
    );
    expect(screen.getByRole('link', { name: 'Rà soát' })).toHaveAttribute(
      'href',
      '/packages/pkg-review',
    );
  });
});
