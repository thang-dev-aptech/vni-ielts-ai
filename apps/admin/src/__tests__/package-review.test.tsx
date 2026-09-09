import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AdminPaths } from '../routes/paths.js';
import type { AdminPackage, ParsedCandidateSummary } from '../lib/adminApi.js';

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({
    accessToken: 'test-token',
    can: (permission: string) => permissions.has(permission),
  }),
}));

const permissions = new Set<string>(['package.read']);

vi.mock('../lib/operator.js', () => ({
  useOperator: () => ({
    can: (key: string) => permissions.has(key),
    isOperator: true,
    name: 'Người vận hành',
    email: 'ops@vni.test',
    previewing: false,
    previewLabel: null,
  }),
}));

const getPackage = vi.fn();
const listPackageCandidates = vi.fn();
const listExams = vi.fn();
const deletePackage = vi.fn();

vi.mock('../lib/adminApi.js', () => ({
  getPackage: (...args: unknown[]) => getPackage(...args),
  listPackageCandidates: (...args: unknown[]) => listPackageCandidates(...args),
  listExams: (...args: unknown[]) => listExams(...args),
  deletePackage: (...args: unknown[]) => deletePackage(...args),
}));

const { PackageReviewPage } = await import('../screens/PackageReviewPage.js');

function packageOf(overrides: Partial<AdminPackage> = {}): AdminPackage {
  return {
    packageId: 'pkg-1',
    sourceKind: 'zip',
    fileName: 'raw.zip',
    status: 'needs-review',
    uploadedByName: 'Người vận hành',
    findings: [],
    entries: [],
    createdVersionIds: [],
    createdAt: '2026-09-07T00:00:00.000Z',
    updatedAt: '2026-09-07T00:00:00.000Z',
    ...overrides,
  };
}

function summaryOf(overrides: Partial<ParsedCandidateSummary> = {}): ParsedCandidateSummary {
  return {
    candidateId: 'cand-1',
    packageId: 'pkg-1',
    title: 'Reading extracted',
    classification: 'reading',
    confidence: 0.7,
    status: 'pending-review',
    version: 1,
    moduleCount: 1,
    questionCount: 3,
    unresolvedCount: 2,
    sources: [],
    ...overrides,
  };
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/packages/pkg-1']}>
      <Routes>
        <Route path={AdminPaths.packagePattern} element={<PackageReviewPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  permissions.clear();
  permissions.add('package.read');
  getPackage.mockReset();
  listPackageCandidates.mockReset();
  listExams.mockReset().mockResolvedValue({ exams: [] });
  deletePackage.mockReset();
});

describe('PackageReviewPage', () => {
  it('lists candidates with unresolved counts and no publish action', async () => {
    getPackage.mockResolvedValue(packageOf());
    listPackageCandidates.mockResolvedValue([
      summaryOf(),
      summaryOf({
        candidateId: 'cand-2',
        title: null,
        classification: 'unclassified',
        unresolvedCount: 4,
      }),
    ]);
    renderPage();

    await waitFor(() => expect(screen.getByText('Reading extracted')).toBeInTheDocument());
    expect(screen.getByText('2 đề đề xuất · 6 mục chưa rõ.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Reading extracted' })).toHaveAttribute(
      'href',
      '/packages/pkg-1/candidates/cand-1',
    );
    expect(screen.getByRole('link', { name: 'Chưa có tiêu đề' })).toHaveAttribute(
      'href',
      '/packages/pkg-1/candidates/cand-2',
    );
    expect(screen.queryByRole('button', { name: 'Xuất bản' })).not.toBeInTheDocument();
    expect(screen.queryByText(/bản nháp/i)).toBeInTheDocument();
  });

  it('tells an author they can read but not review', async () => {
    getPackage.mockResolvedValue(packageOf());
    listPackageCandidates.mockResolvedValue([summaryOf()]);
    renderPage();

    await waitFor(() => expect(screen.getByText('Reading extracted')).toBeInTheDocument());
    expect(screen.getByText(/Thiếu/)).toBeInTheDocument();
    expect(screen.getByText(/exam.review/)).toBeInTheDocument();
  });

  it('shows an empty state when the package has no candidates', async () => {
    getPackage.mockResolvedValue(packageOf());
    listPackageCandidates.mockResolvedValue([]);
    renderPage();

    await waitFor(() => expect(screen.getByText('Trống')).toBeInTheDocument());
  });

  it('renders every finding on a rejected package, including one with only a message', async () => {
    getPackage.mockResolvedValue(
      packageOf({
        status: 'rejected',
        findings: [
          {
            stage: 'inspect',
            code: 'NOT_A_ZIP',
            pointer: '/',
            message: 'The upload is not a ZIP archive.',
          },
          {
            message: 'Parse stopped before a candidate was produced.',
          },
        ],
      }),
    );
    listPackageCandidates.mockResolvedValue([]);
    renderPage();

    await waitFor(() => expect(screen.getByText(/Lý do bị từ chối/)).toBeInTheDocument());
    expect(screen.getByText('inspect')).toBeInTheDocument();
    expect(screen.getByText('NOT_A_ZIP')).toBeInTheDocument();
    expect(screen.getByText('/')).toBeInTheDocument();
    expect(screen.getByText(/The upload is not a ZIP archive/)).toBeInTheDocument();
    expect(screen.getByText(/Parse stopped before a candidate was produced/)).toBeInTheDocument();
    expect(screen.queryByText('Trống')).not.toBeInTheDocument();
  });

  it('keeps the empty wait state when there are no findings yet', async () => {
    getPackage.mockResolvedValue(packageOf({ status: 'scanning', findings: [] }));
    listPackageCandidates.mockResolvedValue([]);
    renderPage();

    await waitFor(() => expect(screen.getByText('Trống')).toBeInTheDocument());
    expect(
      screen.getByText('Gói này chưa có candidate. Worker có thể vẫn đang phân tích.'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Lý do bị từ chối/)).not.toBeInTheDocument();
    expect(screen.queryByText(/parse đã từ chối/)).not.toBeInTheDocument();
  });
});
