import { useState } from 'react';
import { Link } from 'react-router-dom';
import { AdminPaths } from '../routes/paths.js';
import { StatusBadge } from '../components/StatusBadge.js';
import { PreviewNotice } from '../components/PreviewNotice.js';
import { useOperator } from '../lib/operator.js';
import { EXAM_STATES, STATE, type ExamState } from '../lib/lifecycle.js';
import { ownedByMe, useWorkflow } from '../lib/previewStore.js';

/**
 * Màn A1 — the author's own work.
 *
 * <b>One screen with five filters, not five screens.</b> The sidebar shows
 * Nháp · Chờ duyệt · Trả lại · Đã xuất bản as separate entries because that is
 * how an author thinks about their queue, but they are one list filtered five
 * ways. Building five near-identical screens means a column added to one of
 * them is missing from four, and nobody notices which.
 *
 * <b>Ownership is the query, not a column.</b> `exam.read.own` is what this
 * screen runs on: an author sees their own drafts and nobody else's, which is
 * the whole reason the permission model grew a scope. A trưởng chuyên môn
 * looking for everyone's work goes to the review queue instead.
 *
 * <b>Two different empty states.</b> "Chưa soạn đề nào" and "không có đề nào ở
 * trạng thái này" are different facts and suggest different next actions —
 * `cms-spec.md` §11 lists conflating them as the classic mistake on a filtered
 * list.
 */
export function MyExamsPage() {
  const operator = useOperator();
  const { versions } = useWorkflow(operator.name, operator.email);
  const [filter, setFilter] = useState<ExamState | 'all'>('all');

  const mine = versions.filter(ownedByMe);
  const shown = filter === 'all' ? mine : mine.filter((v) => v.state === filter);

  return (
    <>
      <header className="cms-head">
        <h1>Đề của tôi</h1>
        <p>Đề bạn soạn, từ bản nháp tới lúc lên web. Chỉ bạn và trưởng chuyên môn thấy bản nháp.</p>
      </header>

      <PreviewNotice what="Đây là sáu đề mẫu để đi thử vòng đời duyệt." />

      <div className="cms-filters" role="group" aria-label="Lọc theo trạng thái">
        <FilterChip active={filter === 'all'} onClick={() => setFilter('all')} count={mine.length}>
          Tất cả
        </FilterChip>

        {EXAM_STATES.map((state) => (
          <FilterChip
            key={state}
            active={filter === state}
            onClick={() => setFilter(state)}
            count={mine.filter((v) => v.state === state).length}
          >
            {STATE[state].label}
          </FilterChip>
        ))}
      </div>

      {mine.length === 0 && (
        <div className="cms-empty">
          <h3>Bạn chưa soạn đề nào</h3>
          <p>Khi trình soạn đề có mặt, đề mới sẽ bắt đầu ở đây dưới dạng bản nháp.</p>
        </div>
      )}

      {mine.length > 0 && shown.length === 0 && (
        <div className="cms-empty">
          <h3>Không có đề nào ở trạng thái này</h3>
          <p>
            Bạn có {mine.length} đề ở các trạng thái khác.{' '}
            <button type="button" className="cms-link-inline" onClick={() => setFilter('all')}>
              Xem tất cả
            </button>
          </p>
        </div>
      )}

      {shown.length > 0 && (
        <div className="cms-table-wrap">
          <table className="cms-table">
            <thead>
              <tr>
                <th>Tên đề</th>
                <th>Kỹ năng</th>
                <th>Version</th>
                <th>Trạng thái</th>
                <th>Mốc gần nhất</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((version) => (
                <tr key={version.versionId}>
                  <td>
                    <Link to={AdminPaths.workflow(version.versionId)}>{version.title}</Link>
                    <span className="cms-sub">
                      {version.variant} · {version.topic}
                      {version.notes.length > 0 && ` · ${version.notes.length} ghi chú`}
                    </span>
                  </td>
                  <td>
                    <span className="cms-modules">
                      {version.modules.map((m) => (
                        <span className="cms-module" key={m.module}>
                          {m.module}
                          <b className="num">{m.questionCount}</b>
                        </span>
                      ))}
                    </span>
                  </td>
                  <td className="num">v{version.versionNumber}</td>
                  <td>
                    <StatusBadge status={version.state} />
                  </td>
                  <td className="num">
                    {latest(
                      version.publishedAt,
                      version.reviewedAt,
                      version.submittedAt,
                      version.createdAt,
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}

/**
 * The most recent thing that happened, rather than four date columns.
 *
 * A version carries up to four timestamps and only the newest answers "where
 * is this now"; the rest belong on the detail screen, where the timeline is
 * the point.
 */
function latest(...stamps: (string | null)[]): string {
  const newest = stamps.find((s) => s !== null);
  return newest === undefined ? '—' : new Date(newest).toLocaleDateString('vi-VN');
}

function FilterChip({
  active,
  count,
  onClick,
  children,
}: {
  active: boolean;
  count: number;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      className={`cms-chip${active ? ' is-active' : ''}`}
      aria-pressed={active}
      onClick={onClick}
    >
      {children}
      <b className="num">{count}</b>
    </button>
  );
}
