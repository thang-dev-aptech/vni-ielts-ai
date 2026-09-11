import { useCallback, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import { useFlash } from '../chrome/Confirm.js';
import { useAdminAuth } from '../lib/AdminAuth.js';
import {
  listContentSources,
  registerContentSource,
  type AdminContentSource,
  type ContentEnvironmentWire,
} from '../lib/adminApi.js';
import { formatAdminDate } from '../lib/formatAdminDate.js';
import { AdminPaths } from '../routes/paths.js';

const ENV_OPTIONS: { id: ContentEnvironmentWire; label: string; hint: string }[] = [
  { id: 'fixture', label: 'Fixture', hint: 'Dev / demo / test — không tới học viên' },
  {
    id: 'internal-review',
    label: 'Internal review',
    hint: 'Staff VNI xem duyệt — vẫn không tới học viên',
  },
  {
    id: 'learner-production',
    label: 'Learner production',
    hint: 'Học viên có thể nhận đề — bắt buộc có bằng chứng quyền',
  },
];

/**
 * Screen — đăng ký quyền sử dụng nguồn đề.
 *
 * Operators register a source once; publish then consults the registry. This
 * page exists so a new source does not require editing ContentRightsSeed and
 * redeploying.
 */
export function ContentRightsPage() {
  const { accessToken } = useAdminAuth();
  const { flash, say } = useFlash();
  const alive = useRef(true);

  const [sources, setSources] = useState<AdminContentSource[] | null>(null);
  const [note, setNote] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [busy, setBusy] = useState(false);

  const [sourceId, setSourceId] = useState('');
  const [title, setTitle] = useState('');
  const [owner, setOwner] = useState('');
  const [rootPath, setRootPath] = useState('');
  const [environments, setEnvironments] = useState<Set<ContentEnvironmentWire>>(
    () => new Set(['fixture']),
  );
  const [expiresAt, setExpiresAt] = useState('');
  const [proofReference, setProofReference] = useState('');
  const [proofReviewer, setProofReviewer] = useState('');
  const [proofReviewedAt, setProofReviewedAt] = useState('');

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const result = await listContentSources(accessToken);
      if (alive.current) {
        setSources(result.sources);
        setNote(result.note);
        setFailed(false);
      }
    } catch {
      if (alive.current) setFailed(true);
    }
  }, [accessToken]);

  useEffect(() => void load(), [load]);

  const wantsLearners = environments.has('learner-production');

  function toggleEnvironment(id: ContentEnvironmentWire) {
    setEnvironments((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const canSubmit =
    sourceId.trim().length > 0 &&
    title.trim().length > 0 &&
    rootPath.trim().length > 0 &&
    environments.size > 0 &&
    (!wantsLearners ||
      (proofReference.trim().length > 0 &&
        proofReviewer.trim().length > 0 &&
        proofReviewedAt.trim().length > 0));

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (accessToken === null || !canSubmit) return;
    setBusy(true);

    try {
      await registerContentSource(accessToken, {
        sourceId: sourceId.trim(),
        title: title.trim(),
        owner: owner.trim() || null,
        rootPath: rootPath.trim(),
        allowedEnvironments: [...environments],
        expiresAt: expiresAt.trim() ? new Date(expiresAt).toISOString() : null,
        proof: wantsLearners
          ? {
              reference: proofReference.trim(),
              reviewer: proofReviewer.trim(),
              reviewedAt: new Date(proofReviewedAt).toISOString(),
            }
          : null,
      });

      say({ tone: 'ok', text: `Đã đăng ký nguồn «${sourceId.trim()}».` });
      setSourceId('');
      setTitle('');
      setOwner('');
      setRootPath('');
      setEnvironments(new Set(['fixture']));
      setExpiresAt('');
      setProofReference('');
      setProofReviewer('');
      setProofReviewedAt('');
      await load();
    } catch (error) {
      const detail =
        error instanceof ApiError && error.problem.detail
          ? error.problem.detail
          : 'Không đăng ký được nguồn. Kiểm tra mã nguồn và bằng chứng rồi thử lại.';
      say({ tone: 'bad', text: detail });
    } finally {
      if (alive.current) setBusy(false);
    }
  }

  return (
    <>
      <header className="cms-head">
        <h1>Quyền nội dung</h1>
        <p>
          Đăng ký nguồn đề được phép dùng ở từng môi trường. Xuất bản ra học viên chỉ khi nguồn đã
          có bằng chứng quyền rõ ràng.
        </p>
      </header>

      {flash}

      <section className="cms-panel">
        <div className="cms-panel-head">
          <h2>Đăng ký nguồn mới</h2>
        </div>

        <p className="cms-muted">
          Chưa biết <code>sourceId</code> của gói bạn đang có?{' '}
          <Link to={AdminPaths.import}>Vào Nhập đề</Link> → mở gói đã upload → xem lại file{' '}
          <code>exam.json</code>.
        </p>

        <form className="cms-form-stack" onSubmit={(event) => void submit(event)}>
          <label className="cms-field">
            <span>Mã nguồn (sourceId)</span>
            <input
              value={sourceId}
              onChange={(e) => setSourceId(e.target.value)}
              placeholder="vd: cambridge-ielts-16"
              autoComplete="off"
              required
            />
            <p className="cms-muted">
              Bạn tự đặt tên, không lấy tự động từ file. Lấy đúng giá trị{' '}
              <code>contentSourceRef.sourceId</code> bên trong <code>exam.json</code> của gói (giải
              nén zip ra xem). Chỉ chữ thường/số/gạch ngang, tối đa 64 ký tự. Đặt trùng gói nào thì
              gói đó dùng được quyền này — không sửa lại được sau khi đã đăng ký, phải tạo mã mới.
            </p>
          </label>

          <label className="cms-field">
            <span>Tiêu đề</span>
            <input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Tên nguồn để operator nhận ra"
              required
            />
          </label>

          <label className="cms-field">
            <span>Chủ sở hữu / nhà xuất bản (tuỳ chọn)</span>
            <input value={owner} onChange={(e) => setOwner(e.target.value)} />
          </label>

          <label className="cms-field">
            <span>Đường dẫn gốc (rootPath)</span>
            <input
              value={rootPath}
              onChange={(e) => setRootPath(e.target.value)}
              placeholder="vd: Đề IELTS/Đề CAM/…"
              required
            />
            <p className="cms-muted">
              Chỉ để ghi chú cho người đọc sau này — hệ thống không kiểm tra đường dẫn này có thật
              hay không. Điền gì cũng được chấp nhận.
            </p>
          </label>

          <fieldset className="cms-field">
            <legend>Môi trường được phép</legend>
            {ENV_OPTIONS.map((option) => (
              <label key={option.id} className="cms-field-inline" style={{ display: 'flex', gap: 8 }}>
                <input
                  type="checkbox"
                  checked={environments.has(option.id)}
                  onChange={() => toggleEnvironment(option.id)}
                />
                <span>
                  <strong>{option.label}</strong>
                  <div className="cms-muted">{option.hint}</div>
                </span>
              </label>
            ))}
          </fieldset>

          <label className="cms-field">
            <span>Hết hạn (tuỳ chọn)</span>
            <input
              type="date"
              value={expiresAt}
              onChange={(e) => setExpiresAt(e.target.value)}
            />
          </label>

          {wantsLearners && (
            <div className="cms-form-stack" style={{ borderTop: '1px solid var(--border, #ddd)', paddingTop: 12 }}>
              <p className="cms-muted">
                Learner production bắt buộc có bằng chứng quyền — người xác nhận, căn cứ, ngày xác
                nhận.
              </p>
              <label className="cms-field">
                <span>Người xác nhận (reviewer)</span>
                <input
                  value={proofReviewer}
                  onChange={(e) => setProofReviewer(e.target.value)}
                  required={wantsLearners}
                />
              </label>
              <label className="cms-field">
                <span>Căn cứ (reference)</span>
                <input
                  value={proofReference}
                  onChange={(e) => setProofReference(e.target.value)}
                  placeholder="Hợp đồng / email / biên bản…"
                  required={wantsLearners}
                />
              </label>
              <label className="cms-field">
                <span>Ngày xác nhận</span>
                <input
                  type="date"
                  value={proofReviewedAt}
                  onChange={(e) => setProofReviewedAt(e.target.value)}
                  required={wantsLearners}
                />
              </label>
              <p className="cms-muted">
                Hệ thống chỉ kiểm tra không để trống — không xác minh nội dung có đúng thật hay
                không. Đây là nơi ghi trách nhiệm: tên bạn ở đây sẽ lưu vào nhật ký (audit log) như
                người xác nhận nguồn này được phép dùng. Chỉ điền khi bạn thật sự có căn cứ (hợp
                đồng, email xác nhận, quyết định của chủ dự án…) — không điền cho có.
              </p>
            </div>
          )}

          <div className="cms-row-actions">
            <button type="submit" className="cms-primary" disabled={busy || !canSubmit}>
              {busy ? 'Đang đăng ký…' : 'Đăng ký nguồn'}
            </button>
          </div>
        </form>
      </section>

      <section className="cms-panel">
        <div className="cms-panel-head">
          <h2>Nguồn đã đăng ký</h2>
        </div>

        {failed && <p className="cms-alert is-bad">Không tải được danh sách nguồn.</p>}
        {sources === null && !failed && <p className="cms-muted">Đang tải…</p>}
        {note !== null && sources !== null && <p className="cms-muted">{note}</p>}

        {sources !== null && sources.length === 0 && (
          <p className="cms-muted">Chưa có nguồn nào. Đăng ký ở form phía trên.</p>
        )}

        {sources !== null && sources.length > 0 && (
          <div className="cms-table-wrap">
            <table className="cms-table">
              <thead>
                <tr>
                  <th>Mã nguồn</th>
                  <th>Tiêu đề</th>
                  <th>Môi trường</th>
                  <th>Học viên</th>
                  <th>Bằng chứng</th>
                  <th>Hết hạn</th>
                </tr>
              </thead>
              <tbody>
                {sources.map((source) => (
                  <tr key={source.sourceId}>
                    <td>
                      <code>{source.sourceId}</code>
                    </td>
                    <td>
                      <strong>{source.title}</strong>
                      {source.owner ? <div className="cms-muted">{source.owner}</div> : null}
                    </td>
                    <td>
                      {source.allowedEnvironments.map((env) => (
                        <span key={env} className="cms-badge is-draft" style={{ marginRight: 4 }}>
                          {env}
                        </span>
                      ))}
                    </td>
                    <td>
                      {source.mayReachLearners ? (
                        <span className="cms-badge is-published">Được phép</span>
                      ) : (
                        <span className="cms-badge is-draft">Không</span>
                      )}
                    </td>
                    <td>
                      {source.reviewer ? (
                        <>
                          <div>{source.reviewer}</div>
                          <div className="cms-muted">{source.licenceReference}</div>
                        </>
                      ) : (
                        <span className="cms-muted">—</span>
                      )}
                    </td>
                    <td>
                      {source.expiresAt ? formatAdminDate(source.expiresAt) : (
                        <span className="cms-muted">Không</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}
