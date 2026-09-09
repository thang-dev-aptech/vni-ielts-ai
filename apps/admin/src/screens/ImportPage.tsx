import { useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { useOperator } from '../lib/operator.js';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import {
  approveImportDraft,
  overrideImportWarning,
  setImportChecklist,
  uploadImportPackage,
  ImportApiError,
  type ImportDraft,
  type ImportFinding,
  type ImportWarning,
} from '../lib/adminApi.js';
import { AdminPaths } from '../routes/paths.js';
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
 * <b>Nothing is written until the last stage passes.</b> The seven stages
 * below are still the real pipeline from `zip-ingestion-security.md`; they
 * describe what happens before the request that either returns a draft or a
 * `PACKAGE_REJECTED` 422 naming which stage refused it.
 */

const CHECKLIST_ITEMS: Array<[key: string, label: string]> = [
  ['questions', 'Câu hỏi'],
  ['options', 'Lựa chọn'],
  ['wordlimits', 'Giới hạn từ'],
  ['acceptedvariants', 'Biến thể đáp án chấp nhận'],
  ['transcriptandevidence', 'Transcript và bằng chứng'],
  ['assetmapping', 'Ánh xạ tài nguyên'],
];

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
  const [checklistRequired, setChecklistRequired] = useState(true);

  const [overriding, setOverriding] = useState<ImportWarning | null>(null);
  const [overrideReason, setOverrideReason] = useState('');
  const [overrideBusy, setOverrideBusy] = useState(false);

  const [approving, setApproving] = useState(false);
  const [checklistBusy, setChecklistBusy] = useState(false);

  const input = useRef<HTMLInputElement>(null);

  async function upload() {
    if (accessToken === null || file === null) return;
    setUploading(true);
    setRejection(null);

    try {
      const created = await uploadImportPackage(accessToken, file, { checklistRequired });
      setDraft(created);
      say({ tone: 'ok', text: `Đã nhận gói. Draft ${created.draftId} — bản nháp, chưa xuất bản.` });
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

  async function toggleChecklist(key: string, checkedNow: boolean) {
    if (accessToken === null || draft === null) return;
    const next = new Set(draft.checklistConfirmed);
    if (checkedNow) next.add(key);
    else next.delete(key);

    setChecklistBusy(true);
    try {
      const updated = await setImportChecklist(accessToken, draft.draftId, [...next]);
      setDraft(updated);
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      setChecklistBusy(false);
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
    draft !== null &&
    !alreadyApproved &&
    blocking.length === 0 &&
    openWarnings.length === 0 &&
    (!draft.checklistRequired || draft.checklistComplete);

  return (
    <>
      <header className="cms-head">
        <h1>Nhập đề</h1>
        <p>
          Gói nhập thành công sẽ ra <strong>bản nháp</strong> — học viên chưa thấy được. Muốn đưa
          vào sử dụng thì cần một thao tác xuất bản riêng. Nguồn đề chưa có quyền tới học viên thì
          đăng ký tại{' '}
          <Link to={AdminPaths.contentRights}>Quyền nội dung</Link> trước khi xuất bản.
        </p>
      </header>

      {flash}

      <section className="cms-panel">
        <h2>Chọn gói</h2>

        <details>
          <summary>Định dạng &amp; mẹo đóng gói</summary>

          <dl className="cms-facts">
            <div>
              <dt>Định dạng</dt>
              <dd>
                <code>.zip</code> chứa <code>manifest.json</code> khai báo một hoặc nhiều{' '}
                <code>exam.json</code>, hoặc một <code>exam.json</code> đơn trong thư mục kỹ năng (
                <code>reading/</code>, <code>listening/</code>, <code>writing/</code>,{' '}
                <code>speaking/</code>)
              </dd>
            </div>
            <div>
              <dt>Dung lượng tối đa</dt>
              <dd>200 MB mỗi gói</dd>
            </div>
          </dl>

          <p className="cms-muted">
            Gói gồm tài liệu thô (.docx/.pdf/.txt theo từng kỹ năng) chưa nhập được: API hiện chưa
            nối nhà cung cấp AI để phân tích tài liệu thô. Dựng gói bằng CLI vận hành (
            <code>backend/tools/Vni.Ielts.ExamImporter</code>) trước, rồi tải file{' '}
            <code>exam.json</code> kết quả lên đây.
          </p>

          <p className="cms-muted">
            Chưa biết cấu trúc gói?{' '}
            <a className="cms-link-button" href="/templates/exam-package-template.zip" download>
              Tải gói mẫu (.zip)
            </a>{' '}
            — có sẵn <code>manifest.json</code> + <code>exam.json</code> đúng định dạng, sửa nội
            dung rồi tải lên lại.
          </p>
        </details>

        <label className="cms-field">
          <input
            type="checkbox"
            checked={checklistRequired}
            disabled={uploading}
            onChange={(e) => setChecklistRequired(e.target.checked)}
          />{' '}
          Cần rà soát trước khi công bố (khuyến nghị cho nội dung mới hoặc chưa có ai kiểm tra)
        </label>
        <span className="cms-sub">
          Gói đã qua kiểm tra thủ công từ trước (ví dụ AI parse lại nội dung đã duyệt) có thể bỏ
          tick để nhập thẳng.
        </span>

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

          {operator.can('exam.review') && draft.checklistRequired && (
            <>
              <h3>
                Checklist chuyên môn ({draft.checklistConfirmed.length}/{CHECKLIST_ITEMS.length})
              </h3>
              <ul className="cms-notes">
                {CHECKLIST_ITEMS.map(([key, label]) => (
                  <li key={key}>
                    <label>
                      <input
                        type="checkbox"
                        checked={draft.checklistConfirmed.includes(key)}
                        disabled={checklistBusy || alreadyApproved}
                        onChange={(e) => void toggleChecklist(key, e.target.checked)}
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
                    : openWarnings.length > 0
                      ? `Còn ${openWarnings.length} cảnh báo chưa xử lý.`
                      : draft.checklistRequired
                        ? `Còn ${CHECKLIST_ITEMS.length - draft.checklistConfirmed.length} mục checklist chưa xác nhận.`
                        : null}
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
