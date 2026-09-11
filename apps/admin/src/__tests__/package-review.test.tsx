import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
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
const getImportDraft = vi.fn();
const setImportChecklist = vi.fn();
const overrideImportWarning = vi.fn();
const approveImportDraft = vi.fn();

vi.mock('../lib/adminApi.js', () => ({
  getPackage: (...args: unknown[]) => getPackage(...args),
  listPackageCandidates: (...args: unknown[]) => listPackageCandidates(...args),
  listExams: (...args: unknown[]) => listExams(...args),
  deletePackage: (...args: unknown[]) => deletePackage(...args),
  getImportDraft: (...args: unknown[]) => getImportDraft(...args),
  setImportChecklist: (...args: unknown[]) => setImportChecklist(...args),
  overrideImportWarning: (...args: unknown[]) => overrideImportWarning(...args),
  approveImportDraft: (...args: unknown[]) => approveImportDraft(...args),
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
  getImportDraft.mockReset();
  setImportChecklist.mockReset();
  overrideImportWarning.mockReset();
  approveImportDraft.mockReset();
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

  it('renders safe failure reason when package status is failed', async () => {
    getPackage.mockResolvedValue(
      packageOf({
        status: 'failed',
        failureCode: 'INGESTION_FAILED',
        failureDetail: 'An unexpected error occurred during package processing.',
      }),
    );
    listPackageCandidates.mockResolvedValue([]);
    renderPage();

    await waitFor(() => expect(screen.getByText(/Lỗi xử lý gói/)).toBeInTheDocument());
    expect(
      screen.getByText(/An unexpected error occurred during package processing\./),
    ).toBeInTheDocument();
  });

  it('opens linked import draft review when package has importDraftId and allows approval', async () => {
    permissions.add('exam.review');
    getPackage.mockResolvedValue(
      packageOf({
        status: 'needs-review',
        importDraftId: 'draft-abc-123',
      }),
    );
    listPackageCandidates.mockResolvedValue([]);
    getImportDraft.mockResolvedValue({
      draftId: 'draft-abc-123',
      definitionId: 'def-1',
      versionNumber: 1,
      route: 'structuredpackage',
      approvalState: 'reviewrequired',
      presentSkills: ['reading'],
      findings: [],
      warnings: [],
      checklistConfirmed: [
        'questions',
        'options',
        'wordlimits',
        'acceptedvariants',
        'transcriptandevidence',
      ],
      checklistComplete: false,
      checklistRequired: true,
      assetCount: 2,
    });
    setImportChecklist.mockResolvedValue({
      draftId: 'draft-abc-123',
      definitionId: 'def-1',
      versionNumber: 1,
      route: 'structuredpackage',
      approvalState: 'reviewrequired',
      presentSkills: ['reading'],
      findings: [],
      warnings: [],
      checklistConfirmed: [
        'questions',
        'options',
        'wordlimits',
        'acceptedvariants',
        'transcriptandevidence',
        'assetmapping',
      ],
      checklistComplete: true,
      checklistRequired: true,
      assetCount: 2,
    });
    approveImportDraft.mockResolvedValue({
      draftId: 'draft-abc-123',
      approvalState: 'approved',
      examVersionId: 'ev-created-999',
    });

    renderPage();

    await waitFor(() => expect(screen.getByText('Bản nháp draft-abc-123')).toBeInTheDocument());
    expect(screen.getByText(/Tài nguyên/)).toBeInTheDocument();
    expect(screen.getByText('Ánh xạ tài nguyên')).toBeInTheDocument();

    const approveBtn = screen.getByRole('button', { name: 'Duyệt' });
    expect(approveBtn).toBeDisabled();

    // Check the last checklist item
    const lastCheckbox = screen.getByLabelText('Ánh xạ tài nguyên');
    fireEvent.click(lastCheckbox);

    await waitFor(() =>
      expect(setImportChecklist).toHaveBeenCalledWith(
        'test-token',
        'draft-abc-123',
        expect.arrayContaining(['assetmapping']),
      ),
    );
  });

  it('updates from uploaded to needs-review without page reload', async () => {
    getPackage
      .mockResolvedValueOnce(packageOf({ status: 'uploaded' }))
      .mockResolvedValue(
        packageOf({
          status: 'needs-review',
          importDraftId: 'draft-polled',
        }),
      );
    listPackageCandidates.mockResolvedValue([]);
    getImportDraft.mockResolvedValue({
      draftId: 'draft-polled',
      definitionId: 'def-polled',
      versionNumber: 1,
      route: 'structuredpackage',
      approvalState: 'reviewrequired',
      presentSkills: ['reading'],
      findings: [],
      warnings: [],
      checklistConfirmed: [],
      checklistComplete: false,
      checklistRequired: false,
      assetCount: 0,
    });

    renderPage();

    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'raw.zip' })).toBeInTheDocument(),
    );
    expect(screen.queryByText('Bản nháp draft-polled')).not.toBeInTheDocument();

    await waitFor(
      () => expect(screen.getByText('Bản nháp draft-polled')).toBeInTheDocument(),
      { timeout: 3000 },
    );
  });

  it('draft load failure displays retry UI while keeping package details visible', async () => {
    getPackage.mockResolvedValue(
      packageOf({
        status: 'needs-review',
        importDraftId: 'draft-fail-test',
      }),
    );
    listPackageCandidates.mockResolvedValue([]);
    getImportDraft
      .mockRejectedValueOnce(new Error('Network error loading draft'))
      .mockResolvedValueOnce({
        draftId: 'draft-fail-test',
        definitionId: 'def-fail',
        versionNumber: 1,
        route: 'structuredpackage',
        approvalState: 'reviewrequired',
        presentSkills: ['reading'],
        findings: [],
        warnings: [],
        checklistConfirmed: [],
        checklistComplete: false,
        checklistRequired: false,
        assetCount: 0,
      });

    renderPage();

    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'raw.zip' })).toBeInTheDocument(),
    );
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
    expect(
      screen.getByText('Không tải được bản nháp nhập đề. Vui lòng thử lại.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
    // Package details still visible
    expect(screen.getByRole('heading', { name: 'raw.zip' })).toBeInTheDocument();

    // Click retry
    fireEvent.click(screen.getByRole('button', { name: 'Thử lại' }));

    await waitFor(() => expect(screen.getByText('Bản nháp draft-fail-test')).toBeInTheDocument());
    expect(
      screen.queryByText('Không tải được bản nháp nhập đề. Vui lòng thử lại.'),
    ).not.toBeInTheDocument();
  });

  it('route switch ignores slow response from previous package', async () => {
    let resolvePkg1: (pkg: AdminPackage) => void;
    const pkg1Promise = new Promise<AdminPackage>((resolve) => {
      resolvePkg1 = resolve;
    });

    getPackage.mockImplementation((_token: string, id: string) => {
      if (id === 'pkg-1') return pkg1Promise;
      if (id === 'pkg-2') return Promise.resolve(packageOf({ packageId: 'pkg-2', fileName: 'package-2.zip' }));
      return Promise.resolve(packageOf());
    });
    listPackageCandidates.mockResolvedValue([]);

    render(
      <MemoryRouter initialEntries={['/packages/pkg-1', '/packages/pkg-2']} initialIndex={0}>
        <Routes>
          <Route
            path="/packages/:packageId"
            element={
              <>
                <Link to="/packages/pkg-2" data-testid="nav-pkg-2">Go to 2</Link>
                <PackageReviewPage />
              </>
            }
          />
        </Routes>
      </MemoryRouter>,
    );

    // Navigate to pkg-2 before pkg-1 resolves
    fireEvent.click(screen.getByTestId('nav-pkg-2'));

    // Pkg-2 renders
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'package-2.zip' })).toBeInTheDocument(),
    );

    // Now resolve pkg-1
    resolvePkg1!(packageOf({ packageId: 'pkg-1', fileName: 'package-1-late.zip' }));

    // Verify pkg-2 remains on screen and package-1-late.zip never replaces it
    await new Promise((r) => setTimeout(r, 50));
    expect(screen.getByRole('heading', { name: 'package-2.zip' })).toBeInTheDocument();
    expect(screen.queryByText('package-1-late.zip')).not.toBeInTheDocument();
  });

  it('forces draft refresh after IMPORT_REVISION_CONFLICT on approval', async () => {
    const { ApiError } = await import('@vni/auth');
    permissions.add('exam.review');
    getPackage.mockResolvedValue(
      packageOf({
        status: 'needs-review',
        importDraftId: 'draft-conflict-test',
      }),
    );
    listPackageCandidates.mockResolvedValue([]);

    let draftFetchCount = 0;
    getImportDraft.mockImplementation(() => {
      draftFetchCount++;
      return Promise.resolve({
        draftId: 'draft-conflict-test',
        definitionId: 'def-1',
        versionNumber: draftFetchCount,
        route: 'structuredpackage',
        approvalState: 'reviewrequired',
        presentSkills: ['reading'],
        findings: [],
        warnings: [],
        checklistConfirmed: [
          'questions',
          'options',
          'wordlimits',
          'acceptedvariants',
          'transcriptandevidence',
          'assetmapping',
        ],
        checklistComplete: true,
        checklistRequired: true,
        assetCount: 0,
      });
    });

    approveImportDraft.mockRejectedValueOnce(
      new ApiError({
        title: 'Revision conflict',
        status: 409,
        code: 'IMPORT_REVISION_CONFLICT',
        detail: 'Bản nháp đã bị thay đổi bởi phiên làm việc khác.',
      }),
    );

    renderPage();

    await waitFor(() => expect(screen.getByText('Bản nháp draft-conflict-test')).toBeInTheDocument());
    expect(draftFetchCount).toBe(1);

    const approveBtn = screen.getByRole('button', { name: 'Duyệt' });
    expect(approveBtn).not.toBeDisabled();
    fireEvent.click(approveBtn);

    await waitFor(() => expect(draftFetchCount).toBe(2));
    expect(screen.getByText(/Dữ liệu mới nhất đã được tải/)).toBeInTheDocument();
    expect(screen.getByText(/v2/)).toBeInTheDocument();
  });

  it('shows N draft cards with progress 1/3 and two remaining approve buttons', async () => {
    permissions.add('exam.review');
    getPackage.mockResolvedValue(
      packageOf({
        status: 'needs-review',
        importDraftId: 'draft-a',
        importDraftIds: ['draft-a', 'draft-b', 'draft-c'],
      }),
    );
    listPackageCandidates.mockResolvedValue([]);

    const draftOf = (id: string, approved: boolean) => ({
      draftId: id,
      definitionId: `def-${id}`,
      versionNumber: 1,
      route: 'structuredpackage',
      approvalState: approved ? 'approved' : 'reviewrequired',
      presentSkills: ['reading'],
      findings: [],
      warnings: [],
      checklistConfirmed: [],
      checklistComplete: true,
      checklistRequired: false,
      assetCount: 1,
      examVersionId: approved ? `ev-${id}` : null,
    });

    getImportDraft.mockImplementation((_token: string, id: string) =>
      Promise.resolve(draftOf(id, id === 'draft-a')),
    );

    renderPage();

    await waitFor(() => expect(screen.getByTestId('draft-progress')).toHaveTextContent('Đã duyệt 1/3'));
    expect(screen.getByText('Bản nháp draft-a')).toBeInTheDocument();
    expect(screen.getByText('Bản nháp draft-b')).toBeInTheDocument();
    expect(screen.getByText('Bản nháp draft-c')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Duyệt' })).toHaveLength(2);
  });

  it('updates progress to 3/3 after the last draft is approved', async () => {
    permissions.add('exam.review');
    getPackage.mockResolvedValue(
      packageOf({
        status: 'needs-review',
        importDraftId: 'draft-a',
        importDraftIds: ['draft-a', 'draft-b', 'draft-c'],
      }),
    );
    listPackageCandidates.mockResolvedValue([]);

    const state = new Map([
      ['draft-a', 'approved'],
      ['draft-b', 'approved'],
      ['draft-c', 'reviewrequired'],
    ]);

    getImportDraft.mockImplementation((_token: string, id: string) =>
      Promise.resolve({
        draftId: id,
        definitionId: `def-${id}`,
        versionNumber: 1,
        route: 'structuredpackage',
        approvalState: state.get(id)!,
        presentSkills: ['listening'],
        findings: [],
        warnings: [],
        checklistConfirmed: [],
        checklistComplete: true,
        checklistRequired: false,
        assetCount: 1,
        examVersionId: state.get(id) === 'approved' ? `ev-${id}` : null,
      }),
    );

    approveImportDraft.mockImplementation(async (_token: string, id: string) => {
      state.set(id, 'approved');
      return {
        draftId: id,
        definitionId: `def-${id}`,
        versionNumber: 1,
        route: 'structuredpackage',
        approvalState: 'approved',
        presentSkills: ['listening'],
        findings: [],
        warnings: [],
        checklistConfirmed: [],
        checklistComplete: true,
        checklistRequired: false,
        assetCount: 1,
        examVersionId: `ev-${id}`,
      };
    });

    renderPage();

    await waitFor(() => expect(screen.getByTestId('draft-progress')).toHaveTextContent('Đã duyệt 2/3'));
    fireEvent.click(screen.getByRole('button', { name: 'Duyệt' }));

    await waitFor(() =>
      expect(screen.getByTestId('draft-progress')).toHaveTextContent('Đã duyệt 3/3'),
    );
    expect(screen.queryByRole('button', { name: 'Duyệt' })).not.toBeInTheDocument();
  });
});
