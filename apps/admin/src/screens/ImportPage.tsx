import { useEffect, useRef, useState } from 'react';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { useOperator } from '../lib/operator.js';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import {
  approveImportDraft,
  downloadImportTemplate,
  getImportDraft,
  getImportJob,
  overrideImportWarning,
  uploadImportPackage,
  ImportApiError,
  type ImportDraft,
  type ImportFinding,
  type ImportJobState,
  type ImportJobView,
  type ImportWarning,
} from '../lib/adminApi.js';
import { reasonOf } from './UserDetailPage.js';

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
 * screen polls `GET /import/jobs/{operationId}` — the same idiom
 * `WritingResults` already uses for a marking job in the learner app, not a
 * second one — until the job reaches a terminal state, then loads the draft
 * by the id the finished job carries. A failed job shows its own reason
 * rather than a half-built draft, and the poll is bounded rather than
 * running forever.
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
 * flight — the exact values `apps/web`'s `markingStatus.ts` uses for polling
 * a Writing marking job, reused rather than reinvented. `IMPORT_POLL_MAX`
 * bounds it at ~5.3 minutes of polling; past that the screen stops asking
 * automatically and says so, with a button to ask once more, rather than
 * polling forever.
 */
const IMPORT_POLL_MS = 8_000;
const IMPORT_POLL_MAX = 40;

/** Every blocking (`error`-severity) finding a draft still carries. */
function blockingFindings(draft: ImportDraft): ImportFinding[] {
  return draft.findings.filter((f) => f.severity === 'error');
}

function unresolvedWarnings(draft: ImportDraft): ImportWarning[] {
  return draft.warnings.filter((w) => !w.resolved);
}

export function ImportPage() {
  const { accessToken } = useAdminAuth();
  const operator = useOperator();
  const { flash, say } = useFlash();

  const [file, setFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [rejection, setRejection] = useState<ImportApiError | null>(null);
  const [draft, setDraft] = useState<ImportDraft | null>(null);

  // The out-of-band job: `operationId` is set the moment the upload is
  // accepted, `job` is filled in by the first poll. Both are cleared on
  // every fresh upload so a second package never shows the first one's
  // stage or error.
  const [operationId, setOperationId] = useState<string | null>(null);
  const [job, setJob] = useState<ImportJobView | null>(null);
  const [jobTimedOut, setJobTimedOut] = useState(false);
  const pollCount = useRef(0);

  const [templateBusy, setTemplateBusy] = useState(false);

  const [overriding, setOverriding] = useState<ImportWarning | null>(null);
  const [overrideReason, setOverrideReason] = useState('');
  const [overrideBusy, setOverrideBusy] = useState(false);

  const [approving, setApproving] = useState(false);

  const input = useRef<HTMLInputElement>(null);

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
          setDraft(loadedDraft);
        } catch (error) {
          say({ tone: 'bad', text: reasonOf(error) });
        }
      }
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    }
  }

  /*
   * Poll only while the job is alive — a completed or failed job stops.
   * Bounded at `IMPORT_POLL_MAX` tries so a stuck `Running` cannot hammer
   * the API forever; past the bound the screen says so and offers a manual
   * check instead of guessing when to give up.
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
    pollCount.current = 0;

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
      say({ tone: 'ok', text: 'Đã duyệt bản nháp. Vẫn cần một thao tác xuất bản riêng để tới học viên.' });
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      setApproving(false);
    }
  }

  const blocking = draft === null ? [] : blockingFindings(draft);
  const openWarnings = draft === null ? [] : unresolvedWarnings(draft);
  const alreadyApproved = draft?.approvalState === 'approved';
  const canApprove =
    draft !== null && !alreadyApproved && blocking.length === 0 && openWarnings.length === 0;

  return (
    <>
      <header className="cms-head">
        <h1>Nhập đề</h1>
        <p>
          Gói nhập thành công sẽ ra <strong>bản nháp</strong> — học viên chưa thấy được. Muốn đưa
          vào sử dụng thì cần một thao tác xuất bản riêng.
        </p>
      </header>

      {flash}

      <section className="cms-panel">
        <h2>Chọn gói</h2>

        <dl className="cms-facts">
          <div>
            <dt>Định dạng</dt>
            <dd>
              <code>.zip</code> chứa một đề — hôm nay chỉ nhận gói đã có sẵn một <code>exam.json</code>{' '}
              hoàn chỉnh
            </dd>
          </div>
          <div>
            <dt>Phiên bản định dạng</dt>
            <dd>
              <code>formatVersion 1.0</code>
            </dd>
          </div>
          <div>
            <dt>Dung lượng tối đa</dt>
            <dd>200 MB mỗi gói</dd>
          </div>
        </dl>

        <p className="cms-muted">
          Gói gồm tài liệu thô (.docx/.pdf/.txt theo từng kỹ năng) chưa nhập được: API hiện chưa nối
          nhà cung cấp AI để phân tích tài liệu thô. Dựng gói bằng CLI vận hành (
          <code>backend/tools/Vni.Ielts.ExamImporter</code>) trước, rồi tải file <code>exam.json</code>{' '}
          kết quả lên đây.
        </p>

        <p className="cms-muted">
          Chưa chắc tên thư mục? Tải khung mẫu — đặt sai tên thư mục đáp án là đáp án bị gửi cho mô
          hình AI.{' '}
          <button
            type="button"
            className="cms-secondary"
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
            className="cms-primary"
            disabled={file === null || uploading}
            onClick={() => void upload()}
          >
            {uploading ? 'Đang tải lên và kiểm…' : 'Tải lên và kiểm'}
          </button>
        </div>

        {rejection !== null && <RejectionPanel error={rejection} />}
      </section>

      {operationId !== null && draft === null && (
        <section className="cms-panel">
          <div className="cms-panel-head">
            <h2>Mã theo dõi {operationId}</h2>
            {job !== null && (
              <span className={`cms-badge is-${JOB_STATE_BADGE[job.state]}`}>
                {JOB_STATE_LABELS[job.state]}
              </span>
            )}
          </div>

          {job === null && <p className="cms-muted">Đang lấy trạng thái…</p>}

          {job !== null && isJobInFlight(job.state) && (
            <p className="cms-muted" role="status">
              {(JOB_STAGE_LABELS[job.stage] ?? job.stage)} — lần thử {job.attempts + 1}/
              {job.maxAttempts}. Việc này có thể mất vài phút; trang sẽ tự cập nhật.
            </p>
          )}

          {job !== null && job.state === 'Completed' && job.draftId === null && (
            <p className="cms-muted" role="status">
              Đã xử lý xong nhưng chưa thấy bản nháp — thử kiểm tra lại.
            </p>
          )}

          {job !== null && job.state === 'Failed' && (
            <div className="cms-alert is-bad" role="alert">
              <strong>Nhập gói thất bại.</strong>{' '}
              {job.lastError ?? 'Máy chủ không ghi lý do cụ thể.'}
            </div>
          )}

          {jobTimedOut && job !== null && isJobInFlight(job.state) && (
            <p className="cms-muted" role="status">
              Đã theo dõi quá lâu — ngừng tự động kiểm tra. Gói vẫn có thể đang chạy ở phía máy chủ.{' '}
              <button type="button" className="cms-secondary" onClick={() => void retryCheck()}>
                Kiểm tra lại
              </button>
            </p>
          )}
        </section>
      )}

      {draft !== null && (
        <section className="cms-panel">
          <div className="cms-panel-head">
            <h2>Bản nháp {draft.draftId}</h2>
            <span className={`cms-badge is-${alreadyApproved ? 'ready' : 'hold'}`}>
              {alreadyApproved ? 'Đã duyệt' : 'Chờ duyệt'}
            </span>
          </div>

          <dl className="cms-facts">
            <div>
              <dt>Đường nhập</dt>
              <dd>
                <code>{draft.route}</code>
              </dd>
            </div>
            <div>
              <dt>Kỹ năng có trong gói</dt>
              <dd>{draft.presentSkills.length > 0 ? draft.presentSkills.join(' · ') : '—'}</dd>
            </div>
            <div>
              <dt>Định danh đề</dt>
              <dd>
                <code>{draft.definitionId}</code> v{draft.versionNumber}
              </dd>
            </div>
          </dl>

          {draft.findings.length === 0 && draft.warnings.length === 0 && (
            <p className="cms-muted">Gói sạch — không có finding hay cảnh báo nào.</p>
          )}

          {draft.findings.length > 0 && (
            <>
              <h3>Finding ({draft.findings.length})</h3>
              <ul className="cms-notes">
                {draft.findings.map((finding, i) => (
                  <li key={`${finding.code}-${i}`}>
                    <span className={`cms-badge is-${finding.severity === 'error' ? 'attention' : 'muted'}`}>
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
              <h3>Cảnh báo ({openWarnings.length} chưa xử lý / {draft.warnings.length})</h3>
              <ul className="cms-notes">
                {draft.warnings.map((warning) => (
                  <li key={warning.id}>
                    <span className={`cms-badge is-${warning.resolved ? 'muted' : 'hold'}`}>
                      {warning.resolved ? 'đã xử lý' : 'chưa xử lý'}
                    </span>{' '}
                    <strong>{warning.category}</strong> <span className="cms-code">{warning.path}</span> —{' '}
                    {warning.message}
                    {warning.resolved && warning.overrideReason !== null && (
                      <span className="cms-sub"> · Lý do bỏ qua: {warning.overrideReason}</span>
                    )}
                    {!warning.resolved && operator.can('exam.review') && (
                      <button
                        type="button"
                        className="cms-secondary"
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

          {operator.can('exam.review') ? (
            <div className="cms-version-actions">
              <button
                type="button"
                className="cms-primary"
                disabled={!canApprove || approving}
                onClick={() => void approve()}
              >
                {approving ? 'Đang duyệt…' : 'Duyệt'}
              </button>
              {!canApprove && !alreadyApproved && (
                <span className="cms-muted">
                  {blocking.length > 0
                    ? `Còn ${blocking.length} finding lỗi chưa xử lý.`
                    : `Còn ${openWarnings.length} cảnh báo chưa xử lý.`}
                </span>
              )}
            </div>
          ) : (
            <p className="cms-muted">Bạn không có quyền duyệt bản nháp nhập.</p>
          )}
        </section>
      )}

      <section className="cms-panel">
        <h2>Gói đi qua bảy chặng</h2>
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
      </section>

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
      <div className="cms-alert is-bad" role="alert">
        <strong>Chỉ nhận gói đã có sẵn exam.json.</strong> {unavailable.message}
      </div>
    );
  }

  return (
    <div className="cms-alert is-bad" role="alert">
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
