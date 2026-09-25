import { Fragment, useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { getEvaluation, type AdminEvaluationAttempt, type AdminEvaluationDetail } from '../lib/adminApi.js';
import { reasonOf } from './UserDetailPage.js';
import { flagLabel, formatBand, formatWhen, moduleLabel } from './EvaluationsPage.js';

/**
 * Screen 5.2 — one marking, its criteria, and the attempts that produced it.
 *
 * <b>This is the only surface that may show a rejected model body.</b> A band
 * outside the half-band scale is a fault, not a value to clamp; the raw text
 * is labeled as rejected output so it cannot be read as a score.
 *
 * <b>Learner text is not fetched by opening the page.</b> The default GET omits
 * evidence, the submission, and raw output. Asking for them is an audited
 * `includeContent` call this screen does not make: a speculative fetch would
 * both pull protected content and imply the operator is entitled to it.
 * When those fields are absent, the blocks stay absent — not a locked control.
 */

export function EvaluationDetailPage() {
  const { sessionId = '', markingId = '' } = useParams();
  const { accessToken } = useAdminAuth();

  const [detail, setDetail] = useState<AdminEvaluationDetail | null>(null);
  const [missing, setMissing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      // Default `includeContent` stays false. Do not pass true from this screen.
      const found = await getEvaluation(accessToken, sessionId, markingId);
      if (!alive.current) return;
      setDetail(found);
      setMissing(false);
      setError(null);
    } catch (caught) {
      if (!alive.current) return;
      const status = statusOf(caught);
      if (status === 404) {
        setMissing(true);
        setDetail(null);
        setError(null);
        return;
      }
      setError(reasonOf(caught));
    }
  }, [accessToken, markingId, sessionId]);

  useEffect(() => void load(), [load]);

  if (missing) {
    return (
      <div className="cms-empty">
        <h3>Không tìm thấy đánh giá này</h3>
        <p>
          Mã trong địa chỉ có thể sai, hoặc lần chấm đã không còn.{' '}
          <Link to={AdminPaths.evaluations}>Về danh sách đánh giá</Link>
        </p>
      </div>
    );
  }

  const shown =
    detail !== null && detail.sessionId === sessionId && detail.markingId === markingId
      ? detail
      : null;

  if (shown === null && error === null) return <p className="cms-muted">Đang tải…</p>;

  if (shown === null) {
    return (
      <p className="cms-alert" data-tone="danger" role="alert">
        {error}
      </p>
    );
  }

  const view = shown;
  const mismatch =
    view.reportedBand !== null && view.reportedBand !== view.recomputedBand;
  const evidenceOmitted = view.criteria.some((c) => c.evidence === undefined);
  const submission = view.learnerSubmission;
  const ungrounded = asLines(view.ungroundedEvidence);

  return (
    <>
      <nav className="cms-crumbs" aria-label="Đường dẫn">
        <Link to={AdminPaths.evaluations}>Đánh giá AI</Link>
        <span aria-hidden="true">›</span>
        <span>{moduleLabel(view.module)}</span>
      </nav>

      <header className="cms-head">
        <h1>
          {moduleLabel(view.module)}
          {view.taskNumber !== null ? ` · Task ${view.taskNumber}` : ''}
        </h1>
        <p>
          Điểm đang dùng là điểm tính lại từ từng tiêu chí. Điểm mô hình tự báo chỉ để đối chiếu —
          không được kẹp, không được thay.
        </p>
      </header>

      {error !== null && (
        <p className="cms-alert" data-tone="danger" role="alert">
          {error}
        </p>
      )}

      <div className="cms-columns">
        <section className="cms-panel">
          <h2>Điểm</h2>
          <dl className="cms-detail-list">
            <dt>Điểm tính lại</dt>
            <dd className="num">{formatBand(view.recomputedBand)}</dd>
            <dt>Điểm mô hình báo</dt>
            <dd className="num">
              {view.reportedBand === null ? '—' : formatBand(view.reportedBand)}
              {mismatch && (
                <>
                  {' '}
                  <span className="cms-badge" data-tone="warning">Khác điểm tính lại</span>
                </>
              )}
            </dd>
            <dt>Phiên bản</dt>
            <dd>
              <span className="cms-badge" data-tone={view.isCurrent  ? "ok" : "warning"}>
                {view.isCurrent ? 'Đang dùng' : 'Đã thay'}
              </span>
              <span className="cms-sub num">v{view.version}</span>
            </dd>
            <dt>Rubric</dt>
            <dd>{view.rubricVersion}</dd>
            <dt>Chấm lúc</dt>
            <dd className="num">{formatWhen(view.markedAt)}</dd>
            <dt>Phiên thi</dt>
            <dd className="num">{view.sessionId}</dd>
            <dt>Mã lần chấm</dt>
            <dd className="num">{view.markingId}</dd>
          </dl>
        </section>

        <section className="cms-panel">
          <h2>Lịch sử thay thế</h2>
          <p className="cms-muted">
            Mỗi lần chấm lại tạo bản mới. Bản đang dùng là bản học viên thấy; bản đã thay vẫn đọc
            được để đối chiếu.
          </p>
          <dl className="cms-detail-list">
            <dt>Thay cho</dt>
            <dd>
              {view.supersedesId ? (
                <Link to={AdminPaths.evaluation(view.sessionId, view.supersedesId)}>
                  {view.supersedesId}
                </Link>
              ) : (
                '—'
              )}
            </dd>
            <dt>Bị thay bởi</dt>
            <dd>
              {view.supersededById ? (
                <Link to={AdminPaths.evaluation(view.sessionId, view.supersededById)}>
                  {view.supersededById}
                </Link>
              ) : (
                '—'
              )}
            </dd>
          </dl>
        </section>
      </div>

      <section className="cms-panel">
        <h2>Cờ và khuyến nghị</h2>
        {view.flags.length === 0 ? (
          <p className="cms-muted">Không có cờ.</p>
        ) : (
          <p>
            <span className="cms-modules">
              {view.flags.map((flag) => (
                <span className="cms-badge" data-tone="warning" key={flag}>
                  {flagLabel(flag)}
                </span>
              ))}
            </span>
          </p>
        )}
        {view.advisories.length === 0 ? (
          <p className="cms-muted">Không có khuyến nghị.</p>
        ) : (
          <ul className="cms-notes">
            {view.advisories.map((advisory) => (
              <li key={advisory}>{advisory}</li>
            ))}
          </ul>
        )}
      </section>

      <section className="cms-panel">
        <h2>Nguồn gốc lần chấm</h2>
        <ProvenanceList provenance={view.provenance} />
      </section>

      <section className="cms-panel">
        <h2>Từng tiêu chí</h2>
        <div className="cms-table-wrap">
          <table className="cms-table">
            <caption className="cms-muted">Band, nhận xét và trích dẫn theo từng tiêu chí.</caption>
            <thead>
              <tr>
                <th scope="col">Tiêu chí</th>
                <th scope="col">Band</th>
                <th scope="col">Nhận xét</th>
                <th scope="col">Trích dẫn</th>
              </tr>
            </thead>
            <tbody>
              {view.criteria.map((criterion) => (
                <tr key={criterion.criterion}>
                  <td>{criterion.criterion}</td>
                  <td className="num">{formatBand(criterion.band)}</td>
                  <td>{criterion.feedback}</td>
                  <td>
                    {criterion.evidence === undefined ? (
                      <span className="cms-muted">Không có trong phản hồi này</span>
                    ) : (
                      <EvidenceList value={criterion.evidence} />
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {evidenceOmitted && (
          <p className="cms-muted">Trích dẫn từ bài làm không có trong phản hồi này.</p>
        )}
      </section>

      {ungrounded !== null && (
        <section className="cms-panel">
          <h2>Trích dẫn không bám bài</h2>
          {ungrounded.length === 0 ? (
            <p className="cms-muted">Không có.</p>
          ) : (
            <ul className="cms-notes">
              {ungrounded.map((line) => (
                <li key={line}>{line}</li>
              ))}
            </ul>
          )}
        </section>
      )}

      {submission !== undefined && (
        <section className="cms-panel">
          <h2>Bài làm</h2>
          <dl className="cms-detail-list">
            {Object.entries(submission).map(([slot, text]) => (
              <Fragment key={slot}>
                <dt>{slot}</dt>
                <dd>{text === null || text === '' ? '—' : text}</dd>
              </Fragment>
            ))}
          </dl>
        </section>
      )}

      <section className="cms-panel">
        <h2>Lần gọi mô hình</h2>
        {view.attempts.length === 0 ? (
          <p className="cms-muted">Không có lần gọi nào được ghi cho lần chấm này.</p>
        ) : (
          <div className="cms-table-wrap">
            <table className="cms-table">
              <caption className="cms-muted">
                Lịch sử gọi nhà cung cấp. Đầu ra thô bị từ chối không phải là điểm.
              </caption>
              <thead>
                <tr>
                  <th scope="col">Bắt đầu</th>
                  <th scope="col">Kết thúc</th>
                  <th scope="col">Kết quả</th>
                  <th scope="col">Nhà cung cấp</th>
                  <th scope="col">Lỗi</th>
                </tr>
              </thead>
              <tbody>
                {view.attempts.map((attempt) => (
                  <AttemptRows key={attempt.id} attempt={attempt} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}

function AttemptRows({ attempt }: { attempt: AdminEvaluationAttempt }) {
  const rejected = attempt.outcome === 'rejected';
  const hasRaw = attempt.rawOutput !== undefined;

  return (
    <>
      <tr>
        <td className="num cms-nowrap">{formatWhen(attempt.startedAt)}</td>
        <td className="num cms-nowrap">{formatWhen(attempt.finishedAt)}</td>
        <td>
          <span className="cms-badge" data-tone={outcomeTone(attempt.outcome)}>
            {outcomeLabel(attempt.outcome)}
          </span>
        </td>
        <td>
          {attempt.provider ?? '—'}
          {attempt.model !== null && <span className="cms-sub">{attempt.model}</span>}
          {attempt.requestId !== null && <span className="cms-sub num">{attempt.requestId}</span>}
        </td>
        <td>
          {attempt.errorCode ?? attempt.errorMessage ? (
            <>
              {attempt.errorCode}
              {attempt.errorMessage !== null && (
                <span className="cms-sub">{attempt.errorMessage}</span>
              )}
            </>
          ) : (
            '—'
          )}
        </td>
      </tr>
      {(rejected || hasRaw) && (
        <tr>
          <td colSpan={5}>
            <div className="cms-alert" data-tone="danger" role="note">
              <p>
                <strong>Đầu ra thô bị từ chối — không dùng làm điểm.</strong> Band ngoài thang nửa
                bậc là hỏng; hệ thống không kẹp về giá trị gần nhất.
              </p>
              {hasRaw ? (
                <pre>{attempt.rawOutput}</pre>
              ) : (
                <p>Phần thân đầu ra không có trong phản hồi này.</p>
              )}
              {attempt.rawOutputTruncated && <p>Nội dung đã được cắt ngắn khi lưu.</p>}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

function ProvenanceList({ provenance }: { provenance: Record<string, unknown> | null | undefined }) {
  const entries = Object.entries(provenance ?? {});
  if (entries.length === 0) return <p className="cms-muted">Không có thông tin nguồn gốc.</p>;

  return (
    <dl className="cms-detail-list">
      {entries.map(([key, value]) => (
        <Fragment key={key}>
          <dt>{provenanceLabel(key)}</dt>
          <dd className={typeof value === 'boolean' || typeof value === 'number' ? 'num' : undefined}>
            {formatProvenance(value)}
          </dd>
        </Fragment>
      ))}
    </dl>
  );
}

function EvidenceList({ value }: { value: unknown }) {
  const lines = asLines(value) ?? [];
  if (lines.length === 0) return <span className="cms-muted">Không có trích dẫn</span>;
  return (
    <ul className="cms-notes">
      {lines.map((line) => (
        <li key={line}>{line}</li>
      ))}
    </ul>
  );
}

function asLines(value: unknown): string[] | null {
  if (value === undefined) return null;
  if (value === null) return [];
  if (Array.isArray(value)) return value.map((item) => String(item));
  return [String(value)];
}

function provenanceLabel(key: string) {
  if (key === 'promptVersion') return 'Phiên bản prompt';
  if (key === 'providerSection') return 'Nhà cung cấp';
  if (key === 'modelRequested') return 'Mô hình yêu cầu';
  if (key === 'modelReported') return 'Mô hình thực phục vụ';
  if (key === 'modelMismatch') return 'Lệch tên mô hình';
  if (key === 'requestId') return 'Mã yêu cầu';
  return key;
}

function formatProvenance(value: unknown) {
  if (typeof value === 'boolean') return value ? 'Có' : 'Không';
  if (value === null || value === undefined || value === '') return '—';
  if (typeof value === 'string' || typeof value === 'number') return String(value);
  return JSON.stringify(value);
}

function outcomeLabel(outcome: string) {
  if (outcome === 'succeeded') return 'Thành công';
  if (outcome === 'rejected') return 'Bị từ chối';
  if (outcome === 'providererror') return 'Lỗi nhà cung cấp';
  return outcome;
}

function outcomeTone(outcome: string): 'ok' | 'warning' | 'danger' {
  if (outcome === 'succeeded') return 'ok';
  if (outcome === 'rejected') return 'danger';
  return 'warning';
}

function statusOf(error: unknown) {
  return error instanceof ApiError ? error.problem.status : undefined;
}
