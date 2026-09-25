import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { useOperator } from '../lib/operator.js';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import { GroupPositionEditor } from '../components/GroupPositionEditor.js';
import {
  approveImportDraft,
  downloadImportTemplate,
  getImportDraft,
  getImportJob,
  overrideImportWarning,
  setImportChecklist,
  uploadImportPackage,
  ImportApiError,
  type ImportChecklistCategory,
  type ImportDraft,
  type ImportFinding,
  type ImportJobState,
  type ImportJobView,
  type ImportWarning,
} from '../lib/adminApi.js';
import { reasonOf } from './UserDetailPage.js';
import { cmsBadgeTone } from '../lib/lifecycle.js';

/**
 * Screen 4.1 — bringing an exam package in.
 *
 * <b>The screen says where the package goes before it asks for one.</b> A
 * successful import produces a <i>draft</i>: nobody sits it until a separate
 * publish action, by someone with a separate permission. The specification
 * names this as the thing operators misread most often at this step, so it is
 * the first sentence rather than a footnote.
 *
 * <b>Wired to the real upload endpoint, S6b.</b> The button used to be
 * permanently disabled with a comment saying the ZIP door had not been built —
 * `POST /api/v1/admin/import/packages` now exists and this screen posts the
 * chosen file to it. What has <i>not</i> changed: the hosted API carries no AI
 * provider for parsing raw source documents this session, so only a package
 * that already contains a ready `exam.json` — the structured route — imports
 * over HTTP today. A raw-document ZIP gets a well-typed 422
 * (`AI_PARSER_UNAVAILABLE`) rather than a crash, and this screen shows that
 * refusal as its own specific sentence rather than a generic failure.
 *
 * <b>Nothing is written until the last inspection stage passes.</b> The seven
 * stages below are still the real inline pipeline from
 * `zip-ingestion-security.md`; they describe what happens inside the upload
 * request, before it either enqueues a job or answers `PACKAGE_REJECTED` 422
 * naming which stage refused it.
 *
 * <b>The upload no longer returns a draft — task 9 of the 2026-09-11
 * out-of-band import slice.</b> A Cambridge parse costs real money and takes
 * minutes, so an earlier task in this plan moved the parse itself out of the
 * request: `POST /packages` now answers `202` with an `operationId`, and this
 * screen polls `GET /import/jobs/{operationId}` — the same setInterval /
 * poll-only-while-in-flight shape `WritingResults` already uses for a
 * marking job in the learner app, not a second one — until the job reaches a
 * terminal state, then loads the draft by the id the finished job carries. A
 * failed job shows its own reason rather than a half-built draft, and the
 * poll is bounded rather than running forever — see `IMPORT_POLL_MAX` for why
 * its bound is not the marking screen's number: this job is not that job.
 */

const STAGES = [
  { key: 'magic', label: 'Kiểm chữ ký tệp', note: 'Đúng là ZIP, không phải tệp đổi đuôi' },
  { key: 'limits', label: 'Hạn mức gói', note: 'Số mục, tỉ lệ nén, dung lượng sau giải nén' },
  {
    key: 'paths',
    label: 'Chuẩn hoá đường dẫn',
    note: 'Chặn thoát thư mục và liên kết tượng trưng',
  },
  { key: 'schema', label: 'Đối chiếu schema', note: 'Từng lỗi kèm vị trí trong tệp' },
  {
    key: 'assets',
    label: 'Đối chiếu tài nguyên',
    note: 'Mọi audio và ảnh được tham chiếu đều tồn tại',
  },
  { key: 'media', label: 'Kiểm tra media', note: 'Tệp media đúng là media' },
  { key: 'persist', label: 'Ghi thành bản nháp', note: 'Bước đầu tiên chạm vào cơ sở dữ liệu' },
];

/**
 * What the worker reports as it goes — a different list from `STAGES` above.
 * `STAGES` is the inline ZIP inspection this request still does; this is the
 * out-of-band job an operator is waiting on for several minutes and needs to
 * see move.
 */
const JOB_STAGE_LABELS: Record<string, string> = {
  Extracting: 'Đang giải nén gói',
  Parsing: 'Đang phân tích đề (gọi mô hình AI)',
  Transcribing: 'Đang chuyển băng ghi âm thành văn bản',
  Keying: 'Đang gắn đáp án',
  Checking: 'Đang đối chiếu',
  Explaining: 'Đang tạo giải thích',
  Done: 'Hoàn tất',
};

const JOB_STATE_LABELS: Record<ImportJobState, string> = {
  Pending: 'Đang chờ xử lý',
  Running: 'Đang chạy',
  Retryable: 'Đang chờ thử lại',
  Failed: 'Thất bại',
  Completed: 'Hoàn tất',
};

const JOB_STATE_BADGE: Record<ImportJobState, string> = {
  Pending: 'hold',
  Running: 'hold',
  Retryable: 'hold',
  Failed: 'attention',
  Completed: 'ready',
};

function isJobInFlight(state: ImportJobState): boolean {
  return state === 'Pending' || state === 'Running' || state === 'Retryable';
}

/**
 * How often, and for how long, this screen asks while an import job is in
 * flight.
 *
 * <b>Fix round 1 on this task: these were borrowed from `apps/web`'s
 * `markingStatus.ts` (8s × 40, ~5.3 minutes) and that number belongs to a
 * different kind of job.</b> A Writing mark is one provider call and usually
 * lands well inside that window. An import is `ImportJobStage.Extracting →
 * Parsing → Transcribing → Keying → Checking → Explaining → Done` —
 * `ImportWorker`'s own doc comments put a Cambridge parse at minutes on its
 * own, transcription adds more for a Listening package, and `Explaining`
 * alone is up to forty separate model calls. Exceeding 5.3 minutes was not
 * the worst case for this job, it was the ordinary one — which made the old
 * bound a false alarm on every normal-sized import, not a safety net.
 *
 * 15 seconds × 60 tries = 15 minutes: long enough that the interval elapsing
 * is genuinely unusual rather than routine, short enough that the interval
 * itself (15s, versus the marking screen's 8s) does not multiply the request
 * volume for what is already a slower job. Past the bound the screen does
 * not say the job failed — see the `Đang chạy` branch below — because most
 * of the time it has not; it says the job is still running and offers a way
 * to keep checking, rather than either lying or polling forever.
 */
const IMPORT_POLL_MS = 15_000;
const IMPORT_POLL_MAX = 60;

/** Every blocking (`error`-severity) finding a draft still carries. */
function blockingFindings(draft: ImportDraft): ImportFinding[] {
  return draft.findings.filter((f) => f.severity === 'error');
}

function unresolvedWarnings(draft: ImportDraft): ImportWarning[] {
  return draft.warnings.filter((w) => !w.resolved);
}

/**
 * `ApproveAsync`'s refusal codes, said in a sentence. `RefusedResult` on the
 * server sends the bare code as `problem.detail` — `REVIEWER_IS_AUTHOR` reads
 * clearly enough on its own, the rest do not. The button is not hidden ahead
 * of time for any of these (`canApprove` covers findings/warnings/checklist,
 * but `IMPORT_REVISION_CONFLICT` and the two write-failure codes can only be
 * known by trying), so every code this endpoint can return gets a sentence
 * here rather than leaving some to fall through as the bare string.
 */
const APPROVE_ERROR_TEXT: Record<string, string> = {
  REVIEWER_IS_AUTHOR:
    'Bạn không thể tự duyệt gói mình đã tải lên — cần một người khác duyệt bản nháp này.',
  IMPORT_REVIEW_FORBIDDEN: 'Bạn không có quyền duyệt bản nháp nhập.',
  IMPORT_CHECKLIST_INCOMPLETE: 'Còn mục trong danh sách kiểm tra chưa xác nhận.',
  IMPORT_FINDINGS_BLOCKING: 'Còn finding lỗi chưa xử lý.',
  IMPORT_WARNINGS_UNRESOLVED: 'Còn cảnh báo chưa xử lý.',
  IMPORT_REVISION_CONFLICT: 'Bản nháp đã đổi từ lúc bạn mở trang — tải lại rồi thử lại.',
  IMPORT_ASSET_PROMOTE_FAILED: 'Không đưa được ảnh/audio sang kho công khai — thử lại.',
  IMPORT_CATALOGUE_WRITE_FAILED: 'Đã duyệt nhưng không ghi được vào danh mục đề — thử lại.',
};

function approveErrorText(error: unknown): string {
  if (error instanceof ApiError && error.problem.code !== undefined) {
    const known = APPROVE_ERROR_TEXT[error.problem.code];
    if (known !== undefined) return known;
  }
  return reasonOf(error);
}

/**
 * `ImportReviewCategory`, said in Vietnamese, wire spelling first. Same six
 * categories `ImportReviewPanel.tsx`'s orphaned `REVIEW_CHECKS` names — but
 * these keys are the real wire format (`SetChecklistAsync` compares against
 * `ImportReviewCategory.ToString().ToLowerInvariant()`), not that panel's
 * hyphenated guesses, which never matched anything the server understood.
 */
const CHECKLIST_ITEMS: readonly [ImportChecklistCategory, string][] = [
  ['questions', 'Câu hỏi'],
  ['options', 'Lựa chọn'],
  ['wordlimits', 'Giới hạn từ'],
  ['acceptedvariants', 'Biến thể đáp án'],
  ['transcriptandevidence', 'Transcript và bằng chứng'],
  ['assetmapping', 'Ánh xạ media'],
];

export function ImportPage() {
  const { accessToken } = useAdminAuth();
  const operator = useOperator();
  const { flash, say } = useFlash();
  const [searchParams, setSearchParams] = useSearchParams();

  const [file, setFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [rejection, setRejection] = useState<ImportApiError | null>(null);
  const [draft, setDraft] = useState<ImportDraft | null>(null);
  const [draftLoading, setDraftLoading] = useState(false);

  // The out-of-band job: `operationId` is set the moment the upload is
  // accepted, `job` is filled in by the first poll. Both are cleared on
  // every fresh upload so a second package never shows the first one's
  // stage or error.
  const [operationId, setOperationId] = useState<string | null>(null);
  const [job, setJob] = useState<ImportJobView | null>(null);
  const [jobTimedOut, setJobTimedOut] = useState(false);
  // Fix round 2: a `Completed` job whose draft this account could not load —
  // the upload-only-operator case. Kept separate from `job`/`draft` so the
  // screen can tell "finished, draft not fetched yet" apart from "finished,
  // draft fetch was refused" rather than silently falling through to nothing.
  const [draftLoadFailed, setDraftLoadFailed] = useState<string | null>(null);
  const pollCount = useRef(0);

  const [templateBusy, setTemplateBusy] = useState(false);

  const [overriding, setOverriding] = useState<ImportWarning | null>(null);
  const [overrideReason, setOverrideReason] = useState('');
  const [overrideBusy, setOverrideBusy] = useState(false);

  const [approving, setApproving] = useState(false);
  const [checklistBusy, setChecklistBusy] = useState<ImportChecklistCategory | null>(null);

  const input = useRef<HTMLInputElement>(null);

  /**
   * The draft id lives in the URL (`?draftId=`), not only in React state —
   * a reload wipes state but not the address bar. `replace: true` so
   * checking a package does not fill the back button with one entry per
   * poll tick.
   */
  function openDraft(loadedDraft: ImportDraft) {
    setDraft(loadedDraft);
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        next.set('draftId', loadedDraft.draftId);
        return next;
      },
      { replace: true },
    );
  }

  /**
   * One poll: refresh the job, and load the draft the moment a job is
   * `Completed` and carries one. A `Completed` job with no draft id would be
   * a server bug this screen has no honest way to fix, so it is left as "no
   * draft yet" rather than guessed at.
   */
  async function loadJob(opId: string) {
    if (accessToken === null) return;

    try {
      const latest = await getImportJob(accessToken, opId);
      setJob(latest);

      if (latest.state === 'Completed' && latest.draftId !== null) {
        try {
          const loadedDraft = await getImportDraft(accessToken, latest.draftId);
          openDraft(loadedDraft);
          setDraftLoadFailed(null);
        } catch (error) {
          setDraftLoadFailed(reasonOf(error));
          say({ tone: 'bad', text: reasonOf(error) });
        }
      }
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    }
  }

  /**
   * Reload survival. `ImportPage`'s whole review state — `draft`, `job`,
   * `operationId` — lives in React state and a reload wipes it, even though
   * the draft itself is sitting on the server exactly as it was. The URL's
   * `draftId` is what survives: on mount, if one is present, fetch that
   * draft directly and skip the upload/poll flow entirely.
   */
  useEffect(() => {
    const wanted = searchParams.get('draftId');
    if (wanted === null || accessToken === null) return;

    let cancelled = false;
    setDraftLoading(true);

    getImportDraft(accessToken, wanted)
      .then((loaded) => {
        if (!cancelled) setDraft(loaded);
      })
      .catch((error: unknown) => {
        if (!cancelled) say({ tone: 'bad', text: reasonOf(error) });
      })
      .finally(() => {
        if (!cancelled) setDraftLoading(false);
      });

    return () => {
      cancelled = true;
    };
    // Only on mount / when the account signing in changes — this restores
    // the draft named by the URL once, not every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [accessToken]);

  /*
   * Poll only while the job is alive — a completed or failed job stops.
   * Bounded at `IMPORT_POLL_MAX` tries so a stuck `Running` cannot hammer the
   * API forever; past the bound the screen stops asking automatically and
   * says the job is still running, not that anything failed — for most
   * imports (see `IMPORT_POLL_MAX`'s own comment) the bound elapsing is the
   * expected shape of a normal-sized job, not a fault.
   */
  useEffect(() => {
    if (operationId === null) return;
    if (job !== null && !isJobInFlight(job.state)) {
      pollCount.current = 0;
      return;
    }

    const id = window.setInterval(() => {
      pollCount.current += 1;
      if (pollCount.current > IMPORT_POLL_MAX) {
        window.clearInterval(id);
        setJobTimedOut(true);
        return;
      }
      void loadJob(operationId);
    }, IMPORT_POLL_MS);

    return () => window.clearInterval(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [operationId, job?.state]);

  async function upload() {
    if (accessToken === null || file === null) return;
    setUploading(true);
    setRejection(null);
    setDraft(null);
    setJob(null);
    setOperationId(null);
    setJobTimedOut(false);
    setDraftLoadFailed(null);
    pollCount.current = 0;
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        next.delete('draftId');
        return next;
      },
      { replace: true },
    );

    try {
      const accepted = await uploadImportPackage(accessToken, file);
      say({
        tone: 'ok',
        text: `Đã nhận gói, đang xử lý ngoài luồng — mã theo dõi ${accepted.operationId}.`,
      });
      setOperationId(accepted.operationId);
      await loadJob(accepted.operationId);
    } catch (error) {
      if (error instanceof ImportApiError) {
        setRejection(error);
      } else {
        say({ tone: 'bad', text: reasonOf(error) });
      }
    } finally {
      setUploading(false);
      setFile(null);
      if (input.current !== null) input.current.value = '';
    }
  }

  async function retryCheck() {
    if (operationId === null) return;
    setJobTimedOut(false);
    pollCount.current = 0;
    await loadJob(operationId);
  }

  async function downloadTemplate() {
    if (accessToken === null) return;
    setTemplateBusy(true);

    try {
      const blob = await downloadImportTemplate(accessToken);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'vni-exam-package-template.zip';
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      setTemplateBusy(false);
    }
  }

  async function commitOverride() {
    if (accessToken === null || draft === null || overriding === null) return;
    if (overrideReason.trim() === '') return;
    setOverrideBusy(true);

    try {
      const updated = await overrideImportWarning(
        accessToken,
        draft.draftId,
        overriding.id,
        overrideReason.trim(),
      );
      setDraft(updated);
      say({ tone: 'ok', text: 'Đã bỏ qua cảnh báo, kèm lý do đã ghi vào nhật ký.' });
      setOverriding(null);
      setOverrideReason('');
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      setOverrideBusy(false);
    }
  }

  async function approve() {
    if (accessToken === null || draft === null) return;
    setApproving(true);

    try {
      const updated = await approveImportDraft(accessToken, draft.draftId);
      setDraft(updated);
      say({
        tone: 'ok',
        text: 'Đã duyệt bản nháp. Vẫn cần một thao tác xuất bản riêng để tới học viên.',
      });
    } catch (error) {
      say({ tone: 'bad', text: approveErrorText(error) });
    } finally {
      setApproving(false);
    }
  }

  /**
   * Sends the full next set on every toggle — `SetChecklistAsync` replaces,
   * it does not patch — so this reads the confirmed set fresh off `draft`
   * rather than accumulating local state that could drift from what the
   * server actually holds after a concurrent edit.
   */
  async function toggleChecklistItem(category: ImportChecklistCategory) {
    if (accessToken === null || draft === null) return;
    const current = new Set(draft.checklistConfirmed);
    if (current.has(category)) current.delete(category);
    else current.add(category);

    setChecklistBusy(category);
    try {
      const updated = await setImportChecklist(accessToken, draft.draftId, [
        ...current,
      ] as ImportChecklistCategory[]);
      setDraft(updated);
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      setChecklistBusy(null);
    }
  }

  const blocking = draft === null ? [] : blockingFindings(draft);
  const openWarnings = draft === null ? [] : unresolvedWarnings(draft);
  const alreadyApproved = draft?.approvalState === 'approved';
  const canApprove =
    draft !== null &&
    !alreadyApproved &&
    blocking.length === 0 &&
    openWarnings.length === 0 &&
    draft.checklistComplete;

  return (
    <>
      <header className="cms-page-header">
        <h1 className="cms-page-header__title">Nhập đề</h1>
        <p className="cms-muted">
          Gói nhập thành công sẽ ra <strong>bản nháp</strong> — học viên chưa thấy được. Muốn đưa
          vào sử dụng thì cần một thao tác xuất bản riêng.
        </p>
      </header>

      {flash}

      <article className="cms-card">
        <header className="cms-card-head">
          <div className="cms-card-head__identity">
            <h2 className="cms-card-head__title">Chọn gói</h2>
          </div>
        </header>
        <div className="cms-card-body">
          <p className="cms-muted">
            <code>.zip</code> chứa một đề — hôm nay chỉ nhận gói đã có sẵn một <code>exam.json</code>{' '}
            hoàn chỉnh.
          </p>

          <p className="cms-muted">
            Gói gồm tài liệu thô (.docx/.pdf/.txt theo từng kỹ năng) chưa nhập được: API hiện chưa
            nối nhà cung cấp AI để phân tích tài liệu thô. Dựng gói bằng CLI vận hành (
            <code>backend/tools/Vni.Ielts.ExamImporter</code>) trước, rồi tải file{' '}
            <code>exam.json</code> kết quả lên đây.
          </p>

          <p className="cms-muted">
            Chưa chắc tên thư mục? Tải khung mẫu — đặt sai tên thư mục đáp án là đáp án bị gửi cho mô
            hình AI.{' '}
            <button
              type="button"
              className="cms-button cms-button--secondary"
              disabled={templateBusy}
              onClick={() => void downloadTemplate()}
            >
              {templateBusy ? 'Đang tải mẫu…' : 'Tải mẫu gói (.zip)'}
            </button>
          </p>

          <label className="cms-drop">
            <input
              ref={input}
              type="file"
              accept=".zip,.json"
              disabled={uploading}
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
            <span>{file === null ? 'Chọn tệp gói đề' : file.name}</span>
          </label>

          <div className="cms-version-actions">
            <button
              type="button"
              className="cms-button cms-button--primary"
              disabled={file === null || uploading}
              onClick={() => void upload()}
            >
              {uploading ? 'Đang tải lên và kiểm…' : 'Tải lên và kiểm'}
            </button>
          </div>

          {rejection !== null && <RejectionPanel error={rejection} />}
        </div>
        <footer className="cms-card-foot">
          <div className="cms-metadata">
            <div className="cms-metadata__item">
              <span className="cms-metadata__label">Phiên bản định dạng</span>
              <span className="cms-metadata__value">
                <code>formatVersion 1.0</code>
              </span>
            </div>
            <div className="cms-metadata__item">
              <span className="cms-metadata__label">Dung lượng tối đa</span>
              <span className="cms-metadata__value">200 MB mỗi gói</span>
            </div>
          </div>
        </footer>
      </article>

      {operationId !== null && draft === null && (
        <article className="cms-card">
          <header className="cms-card-head">
            <div className="cms-card-head__identity">
              <h2 className="cms-card-head__title">Mã theo dõi {operationId}</h2>
            </div>
            {job !== null && (
              <div className="cms-card-head__status">
                <span
                  className="cms-status-pill"
                  data-tone={cmsBadgeTone(JOB_STATE_BADGE[job.state])}
                >
                  <span className="cms-status-pill__dot" aria-hidden="true" />
                  {JOB_STATE_LABELS[job.state]}
                </span>
              </div>
            )}
          </header>
          <div className="cms-card-body">
            {job === null && <p className="cms-muted">Đang lấy trạng thái…</p>}

            {job !== null && isJobInFlight(job.state) && (
              <p className="cms-muted" role="status">
                {JOB_STAGE_LABELS[job.stage] ?? job.stage} — lần thử {job.attempts + 1}/
                {job.maxAttempts}. Việc này có thể mất vài phút; trang sẽ tự cập nhật.
              </p>
            )}

            {job !== null && job.state === 'Completed' && job.draftId === null && (
              <p className="cms-muted" role="status">
                Đã xử lý xong nhưng chưa thấy bản nháp — thử kiểm tra lại.
              </p>
            )}

            {/*
             * Fix round 2: the upload-only-operator case. The import finished
             * and a draft exists (`job.draftId !== null`), but this account
             * could not load it — read that as a fact about who is signed in,
             * never as a verdict on whether that is correct: `P-20` splits
             * "may start an import" from "may review one" on purpose, and which
             * side an upload-only account should sit on is a role decision for
             * the product owner, not this screen.
             */}
            {job !== null &&
              job.state === 'Completed' &&
              job.draftId !== null &&
              draft === null &&
              draftLoadFailed !== null && (
                <div className="cms-alert" data-tone="danger" role="alert">
                  <strong>Đã nhập xong, nhưng chưa mở được bản nháp.</strong> Bản nháp{' '}
                  <code>{job.draftId}</code> đã được tạo (mã theo dõi <code>{operationId}</code>),
                  nhưng tài khoản đang đăng nhập không tải được nó — {draftLoadFailed} Cần một tài
                  khoản có quyền xem bản nháp nhập kiểm tra tiếp.{' '}
                  <button type="button" className="cms-button cms-button--secondary" onClick={() => void retryCheck()}>
                    Kiểm tra lại
                  </button>
                </div>
              )}

            {job !== null && job.state === 'Failed' && (
              <div className="cms-alert" data-tone="danger" role="alert">
                <strong>Nhập gói thất bại.</strong>{' '}
                {job.lastError ?? 'Máy chủ không ghi lý do cụ thể.'}
              </div>
            )}

            {jobTimedOut && job !== null && isJobInFlight(job.state) && (
              <p className="cms-muted" role="status">
                Gói vẫn đang chạy ở phía máy chủ — bước phân tích, dịch băng và tạo giải thích cho
                một gói lớn có thể mất nhiều phút. Trang đã ngừng tự động cập nhật; bấm để kiểm tra
                lại bất cứ lúc nào.{' '}
                <button type="button" className="cms-button cms-button--secondary" onClick={() => void retryCheck()}>
                  Kiểm tra lại
                </button>
              </p>
            )}
          </div>
        </article>
      )}

      {draftLoading && draft === null && (
        <article className="cms-card">
          <div className="cms-card-body">
            <p className="cms-muted">Đang mở lại bản nháp từ đường dẫn…</p>
          </div>
        </article>
      )}

      {draft !== null && (
        <article className="cms-card">
          <header className="cms-card-head">
            <div className="cms-card-head__identity">
              <h2 className="cms-card-head__title">Bản nháp {draft.draftId}</h2>
            </div>
            <div className="cms-card-head__status">
              <span className="cms-badge" data-tone={cmsBadgeTone(alreadyApproved ? 'ready' : 'hold')}>
                {alreadyApproved ? 'Đã duyệt' : 'Chờ duyệt'}
              </span>
            </div>
          </header>

          <div className="cms-card-body">
            {draft.findings.length === 0 && draft.warnings.length === 0 && (
              <p className="cms-muted">Gói sạch — không có finding hay cảnh báo nào.</p>
            )}

            {draft.findings.length > 0 && (
              <>
                <h3>Finding ({draft.findings.length})</h3>
                <ul className="cms-notes">
                  {draft.findings.map((finding, i) => (
                    <li key={`${finding.code}-${i}`}>
                      <span
                        className="cms-badge" data-tone={cmsBadgeTone(finding.severity === 'error' ? 'attention' : 'muted')}
                      >
                        {finding.severity}
                      </span>{' '}
                      <strong className="cms-code">{finding.code}</strong>{' '}
                      <span className="cms-code">{finding.path}</span> — {finding.message}
                    </li>
                  ))}
                </ul>
              </>
            )}

            {draft.warnings.length > 0 && (
              <>
                <h3>
                  Cảnh báo ({openWarnings.length} chưa xử lý / {draft.warnings.length})
                </h3>
                <ul className="cms-notes">
                  {draft.warnings.map((warning) => (
                    <li key={warning.id}>
                      <span className="cms-badge" data-tone={cmsBadgeTone(warning.resolved ? 'muted' : 'hold')}>
                        {warning.resolved ? 'đã xử lý' : 'chưa xử lý'}
                      </span>{' '}
                      <strong>{warning.category}</strong>{' '}
                      <span className="cms-code">{warning.path}</span> — {warning.message}
                      {warning.resolved && warning.overrideReason !== null && (
                        <span className="cms-sub"> · Lý do bỏ qua: {warning.overrideReason}</span>
                      )}
                      {!warning.resolved && operator.can('exam.review') && (
                        <button
                          type="button"
                          className="cms-button cms-button--secondary"
                          onClick={() => {
                            setOverriding(warning);
                            setOverrideReason('');
                          }}
                        >
                          Bỏ qua, có lý do
                        </button>
                      )}
                    </li>
                  ))}
                </ul>
              </>
            )}

            {operator.can('package.upload') && draft.groups.length > 0 && accessToken !== null && (
              <>
                <h3>Vị trí trên ảnh ({draft.groups.length})</h3>
                {draft.groups.map((group) => (
                  <GroupPositionEditor
                    key={group.id}
                    accessToken={accessToken}
                    draftId={draft.draftId}
                    group={group}
                    onSaved={setDraft}
                  />
                ))}
              </>
            )}

            {operator.can('exam.review') && (
              <>
                <h3>
                  Danh sách kiểm tra ({draft.checklistConfirmed.length}/{CHECKLIST_ITEMS.length})
                </h3>
                <ul className="cms-notes">
                  {CHECKLIST_ITEMS.map(([category, label]) => (
                    <li key={category}>
                      <label>
                        <input
                          type="checkbox"
                          checked={draft.checklistConfirmed.includes(category)}
                          disabled={checklistBusy !== null}
                          onChange={() => void toggleChecklistItem(category)}
                        />{' '}
                        {label}
                      </label>
                    </li>
                  ))}
                </ul>
              </>
            )}

            {operator.can('exam.review') ? (
              <div className="cms-version-actions">
                <button
                  type="button"
                  className="cms-button cms-button--primary"
                  disabled={!canApprove || approving}
                  onClick={() => void approve()}
                >
                  {approving ? 'Đang duyệt…' : 'Duyệt'}
                </button>
                {!canApprove && !alreadyApproved && (
                  <span className="cms-muted">
                    {blocking.length > 0
                      ? `Còn ${blocking.length} finding lỗi chưa xử lý.`
                      : openWarnings.length > 0
                        ? `Còn ${openWarnings.length} cảnh báo chưa xử lý.`
                        : 'Còn mục trong danh sách kiểm tra chưa xác nhận.'}
                  </span>
                )}
              </div>
            ) : (
              <p className="cms-muted">Bạn không có quyền duyệt bản nháp nhập.</p>
            )}
          </div>

          <footer className="cms-card-foot">
            <div className="cms-metadata">
              <div className="cms-metadata__item">
                <span className="cms-metadata__label">Đường nhập</span>
                <span className="cms-metadata__value">
                  <code>{draft.route}</code>
                </span>
              </div>
              <div className="cms-metadata__item">
                <span className="cms-metadata__label">Kỹ năng có trong gói</span>
                <span className="cms-metadata__value">
                  {draft.presentSkills.length > 0 ? draft.presentSkills.join(' · ') : '—'}
                </span>
              </div>
              <div className="cms-metadata__item">
                <span className="cms-metadata__label">Định danh đề</span>
                <span className="cms-metadata__value">
                  <code>{draft.definitionId}</code> v{draft.versionNumber}
                </span>
              </div>
            </div>
          </footer>
        </article>
      )}

      <article className="cms-card">
        <header className="cms-card-head">
          <div className="cms-card-head__identity">
            <h2 className="cms-card-head__title">Gói đi qua bảy chặng</h2>
          </div>
        </header>
        <div className="cms-card-body">
          <p className="cms-muted">
            Không có gì được ghi vào hệ thống cho tới chặng cuối. Gói bị từ chối ở bất kỳ chặng nào
            đều không để lại dấu vết nào ngoài một dòng lịch sử.
          </p>

          <ol className="cms-stages">
            {STAGES.map((stage, index) => (
              <li className="cms-stage" key={stage.key}>
                <span className="cms-stage-no num">{index + 1}</span>
                <span>
                  <strong>{stage.label}</strong>
                  <span className="cms-sub">{stage.note}</span>
                </span>
              </li>
            ))}
          </ol>
        </div>
      </article>

      <Confirm
        open={overriding !== null}
        title={overriding === null ? '' : `Bỏ qua cảnh báo ở ${overriding.path}?`}
        confirmLabel="Bỏ qua"
        busy={overrideBusy}
        disabled={overrideReason.trim() === ''}
        onCancel={() => {
          setOverriding(null);
          setOverrideReason('');
        }}
        onConfirm={() => void commitOverride()}
        body={
          <>
            <p>{overriding?.message}</p>
            <label className="cms-field">
              <span>Lý do bỏ qua</span>
              <textarea
                rows={3}
                value={overrideReason}
                onChange={(e) => setOverrideReason(e.target.value)}
                placeholder="Vì sao cảnh báo này không cần sửa — lý do vào nhật ký."
              />
            </label>
          </>
        }
      />
    </>
  );
}

/**
 * A `PACKAGE_REJECTED` 422, rendered as its actual finding rather than a
 * generic "Something went wrong".
 *
 * <b>`AI_PARSER_UNAVAILABLE` gets its own heading.</b> It is the one rejection
 * every raw-document upload will hit on this deployment until an AI parser is
 * wired in — an operator who reaches for a `.docx` folder needs to be told
 * that plainly, not handed the same sentence a corrupt ZIP would get.
 */
function RejectionPanel({ error }: { error: ImportApiError }) {
  const unavailable = error.findings.find((f) => f.code === 'AI_PARSER_UNAVAILABLE');

  if (unavailable !== undefined) {
    return (
      <div className="cms-alert" data-tone="danger" role="alert">
        <strong>Chỉ nhận gói đã có sẵn exam.json.</strong> {unavailable.message}
      </div>
    );
  }

  return (
    <div className="cms-alert" data-tone="danger" role="alert">
      <strong>Gói bị từ chối.</strong> {error.problem.detail}
      {error.findings.length > 1 && (
        <ul className="cms-notes">
          {error.findings.map((finding, i) => (
            <li key={`${finding.code}-${i}`}>
              <strong className="cms-code">{finding.code}</strong>{' '}
              <span className="cms-code">{finding.path}</span> — {finding.message}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
