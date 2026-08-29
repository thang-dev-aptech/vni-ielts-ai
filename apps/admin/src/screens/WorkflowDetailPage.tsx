import { Link, useParams } from 'react-router-dom';
import { AdminPaths } from '../routes/paths.js';
import { StatusBadge } from '../components/StatusBadge.js';
import { PreviewNotice } from '../components/PreviewNotice.js';
import { TransitionBar } from '../components/TransitionBar.js';
import { useOperator } from '../lib/operator.js';
import { STATE } from '../lib/lifecycle.js';
import { authorName, objectUrlFor, ownedByMe, useWorkflow } from '../lib/previewStore.js';
import {
  KIND_LABEL,
  formatBytes,
  formatDuration,
  missingAssets,
  publishBlockers,
} from '../lib/media.js';
import { AdminPaths as Paths } from '../routes/paths.js';

/**
 * Màn B2 / C2 — one version, everything that has happened to it, and what may
 * happen next.
 *
 * <b>The same screen for every role.</b> An author opens it to read why their
 * exam came back; a trưởng chuyên môn opens it to approve or return; an
 * administrator opens it to publish. Splitting it per role would produce three
 * screens that drift, and the differences between the roles are already
 * expressed exactly once — in which buttons the transition table hands back.
 *
 * <b>The timeline is the record, and it reads in one direction.</b> Created,
 * submitted, reviewed, published: the same four facts the server will carry as
 * `createdBy`, `submittedBy`, `reviewedBy` and `publishedAt`, in the order
 * they happen, so the question "who signed off on this" has one place to look.
 *
 * <b>Returning notes stay after the exam moves on.</b> A note is not a message
 * that gets consumed — it is the reason a version looks the way it does, and
 * an author fixing round three wants round one still readable.
 */
export function WorkflowDetailPage() {
  const { versionId } = useParams<{ versionId: string }>();
  const operator = useOperator();
  const { versions, media, apply } = useWorkflow(operator.name, operator.email);

  const version = versions.find((v) => v.versionId === versionId);

  if (version === undefined) {
    return (
      <>
        <header className="cms-head">
          <h1>Không tìm thấy đề này</h1>
          <p>
            Đề có thể đã bị xoá, hoặc dữ liệu xem trước đã được đặt lại.{' '}
            <Link to={AdminPaths.myExams}>Về danh sách đề của tôi</Link>.
          </p>
        </header>
      </>
    );
  }

  const face = STATE[version.state];
  const mine = ownedByMe(version);
  const missing = missingAssets(version);
  const blockers = publishBlockers(version);

  return (
    <>
      <nav className="cms-crumbs" aria-label="Đường dẫn">
        <Link to={backTo(version.state)}>{backLabel(version.state)}</Link>
        <span aria-hidden="true">›</span>
        <span>{version.title}</span>
      </nav>

      <header className="cms-head">
        <h1>{version.title}</h1>
        <p>
          <StatusBadge status={version.state} /> <span className="cms-state-hint">{face.hint}</span>
        </p>
      </header>

      <PreviewNotice what="Đề mẫu, không có nội dung câu hỏi thật." />

      <TransitionBar
        version={version}
        onApply={(transition, note) => apply(version.versionId, transition, note)}
        blockedBy={(transition) => {
          /*
           * Only the two transitions that move content toward a learner.
           *
           * Submitting an incomplete draft is the author's business — they may
           * well be asking for a read on the questions while the recording is
           * still being cut. Approving one, or publishing one, is how an exam
           * with no sound reaches a candidate, and the ZIP pipeline already
           * refuses a package whose assets do not resolve. This is the same
           * refusal, at the other door.
           */
          if (transition.id !== 'approve' && transition.id !== 'publish') return null;
          return blockers[0] ?? null;
        }}
      />

      <section className="cms-panel">
        <div className="cms-panel-head">
          <h2>Thông tin đề</h2>
        </div>

        <dl className="cms-facts">
          <Fact
            label="Người soạn"
            value={`${authorName(version, operator.name)}${mine ? ' (bạn)' : ''}`}
          />
          <Fact label="Version" value={`v${version.versionNumber}`} />
          <Fact
            label="Loại đề"
            value={version.variant === 'academic' ? 'Academic' : 'General Training'}
          />
          <Fact label="Chủ đề" value={version.topic} />
          <Fact label="Độ khó khai báo" value={version.difficultyAuthored} />
          <Fact
            label="Độ khó quan sát"
            value="Chưa đủ dữ liệu"
            note="Tính từ lượt làm bài thật. Chưa có lượt nào."
          />
          <Fact
            label="Kỹ năng"
            value={version.modules.map((m) => `${m.module} (${m.questionCount} câu)`).join(' · ')}
          />
        </dl>
      </section>

      <section className="cms-panel">
        <div className="cms-panel-head">
          <h2>Dòng thời gian</h2>
        </div>

        <ol className="cms-steps">
          <Step
            label="Tạo bản nháp"
            at={version.createdAt}
            who={authorName(version, operator.name)}
          />
          <Step
            label="Nộp duyệt"
            at={version.submittedAt}
            who={authorName(version, operator.name)}
          />
          <Step label="Duyệt chuyên môn" at={version.reviewedAt} who={version.reviewedByName} />
          <Step label="Xuất bản" at={version.publishedAt} who={null} />
        </ol>
      </section>

      <section className="cms-panel">
        <div className="cms-panel-head">
          <h2>Media của đề</h2>
          <Link className="cms-link-button" to={Paths.media}>
            Mở kho media
          </Link>
        </div>

        {version.assets.length === 0 && (
          <p className="cms-muted">
            Version này không tham chiếu tệp media nào. Với Reading và Writing thì đó là bình
            thường; với Listening thì không.
          </p>
        )}

        {missing.length > 0 && (
          <p className="cms-alert is-bad" role="alert">
            <strong>Thiếu {missing.length} tệp.</strong> Học viên sẽ gặp phần không phát được. Đề
            không duyệt và không xuất bản được cho tới khi đủ.
          </p>
        )}

        {version.assets.length > 0 && (
          <ul className="cms-assets">
            {version.assets.map((asset) => {
              const file = media.find((m) => m.mediaId === asset.mediaId) ?? null;
              const url = asset.mediaId === null ? null : objectUrlFor(asset.mediaId);

              return (
                <li key={asset.ref} className={file === null ? 'is-missing' : 'is-present'}>
                  <div className="cms-asset-head">
                    <strong>{asset.usedAt}</strong>
                    <span className="cms-muted">{KIND_LABEL[asset.kind]}</span>
                  </div>

                  {file === null ? (
                    <>
                      <p className="cms-asset-bad">Không tìm thấy tệp cho tham chiếu này.</p>
                      <span className="cms-code">{asset.ref}</span>
                    </>
                  ) : (
                    <>
                      <p>{file.fileName}</p>
                      <span className="cms-muted">
                        {formatBytes(file.bytes)}
                        {file.kind === 'audio' && ` · ${formatDuration(file.durationMs)}`} ·{' '}
                        <span className="cms-code">{file.checksum.slice(0, 12)}</span>
                      </span>
                      {file.kind === 'audio' && url !== null && (
                        // Byte-sniffed browser blob URL only. codeql[js/xss-through-dom]
                        <audio controls src={url} preload="metadata" />
                      )}
                    </>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </section>

      <section className="cms-panel">
        <div className="cms-panel-head">
          <h2>Ghi chú duyệt</h2>
          <span className="cms-muted num">{version.notes.length}</span>
        </div>

        {version.notes.length === 0 && (
          <p className="cms-muted">Chưa có ghi chú nào cho version này.</p>
        )}

        {version.notes.length > 0 && (
          <ul className="cms-review-notes">
            {version.notes.map((note) => (
              <li key={note.id}>
                <div className="cms-note-head">
                  <strong>{note.authorName}</strong>
                  {note.anchor !== null && <span className="cms-anchor">{note.anchor}</span>}
                  <span className="cms-muted num">{new Date(note.at).toLocaleString('vi-VN')}</span>
                </div>
                <p>{note.body}</p>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="cms-panel">
        <div className="cms-panel-head">
          <h2>Xem thử như học viên</h2>
        </div>

        {!operator.can('exam.preview') ? (
          <p className="cms-muted">Bạn không có quyền xem thử đề.</p>
        ) : (
          <>
            <button type="button" className="cms-secondary" disabled>
              Mở bản xem thử
            </button>
            <p className="cms-muted">
              Chưa mở được: version này chưa có nội dung câu hỏi. Bản xem thử chạy đúng bộ dựng của
              học viên, không tính giờ và không lưu kết quả — `M-18`.
            </p>
          </>
        )}
      </section>
    </>
  );
}

/** Where "back" goes depends on which queue this version belongs to. */
function backTo(state: string): string {
  if (state === 'in-review') return AdminPaths.reviewQueue;
  if (state === 'approved') return AdminPaths.pendingPublish;
  return AdminPaths.myExams;
}

function backLabel(state: string): string {
  if (state === 'in-review') return 'Hàng chờ duyệt';
  if (state === 'approved') return 'Chờ xuất bản';
  return 'Đề của tôi';
}

function Fact({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="cms-fact">
      <dt>{label}</dt>
      <dd>
        {value}
        {note !== undefined && <span className="cms-sub">{note}</span>}
      </dd>
    </div>
  );
}

/**
 * A step that has not happened says so, rather than being hidden.
 *
 * The gap is the information on this screen: an operator scanning the timeline
 * is asking which signature is missing, and a list that only shows completed
 * steps cannot answer that.
 */
function Step({ label, at, who }: { label: string; at: string | null; who: string | null }) {
  return (
    <li className={at === null ? 'is-pending' : 'is-done'}>
      <span className="cms-step-label">{label}</span>
      {at === null ? (
        <span className="cms-muted">Chưa diễn ra</span>
      ) : (
        <span className="cms-step-when">
          <span className="num">{new Date(at).toLocaleString('vi-VN')}</span>
          {who !== null && <span className="cms-muted"> · {who}</span>}
        </span>
      )}
    </li>
  );
}
