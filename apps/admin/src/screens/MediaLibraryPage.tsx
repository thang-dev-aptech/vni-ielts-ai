import { useRef, useState } from 'react';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import { PreviewNotice } from '../components/PreviewNotice.js';
import { useOperator } from '../lib/operator.js';
import {
  ASSET_STATE,
  KIND_LABEL,
  MAX_BYTES,
  REJECTION,
  assetState,
  checksumOf,
  formatBytes,
  formatDuration,
  inspect,
  mayDelete,
  mayRetire,
  probeDuration,
  usedBy,
  type MediaAsset,
  type MediaKind,
} from '../lib/media.js';
import { objectUrlFor, rememberObjectUrl, uploadedHere, useWorkflow } from '../lib/previewStore.js';

/**
 * Màn D1 — the media library.
 *
 * <b>Why the CMS needs this before it needs an editor.</b> A Listening section
 * is audio. Until there is somewhere to put an mp3, an author composing one has
 * written a question about a sound the system cannot play — and the exam looks
 * finished right up to the moment a candidate presses play.
 *
 * <b>The column that matters most is "đang dùng ở đâu".</b> A library without
 * it is a folder: nobody can tell what is safe to remove, so nothing is ever
 * removed and it fills with near-duplicates of the same recording.
 *
 * <b>What the screen refuses is as designed as what it accepts.</b> Content
 * behind a published version cannot be replaced or deleted — replacing the file
 * under a live reference changes what candidates hear while the version number
 * says nothing happened. Retiring is the way out: it takes the asset out of the
 * picker and leaves everything already using it alone.
 */
export function MediaLibraryPage() {
  const operator = useOperator();
  const { versions, media, addMedia, retireMedia, deleteMedia } = useWorkflow(
    operator.name,
    operator.email,
  );

  const [kind, setKind] = useState<MediaKind | 'all'>('all');
  const [busy, setBusy] = useState(false);
  const [rejected, setRejected] = useState<{ code: string; text: string; file: string } | null>(
    null,
  );
  const [pending, setPending] = useState<{ asset: MediaAsset; action: 'retire' | 'delete' } | null>(
    null,
  );
  const input = useRef<HTMLInputElement>(null);
  const { flash, say } = useFlash();

  async function take(file: File) {
    setBusy(true);
    setRejected(null);

    try {
      const bytes = await file.arrayBuffer();
      const head = new Uint8Array(bytes, 0, Math.min(16, bytes.byteLength));
      const verdict = inspect(head, file.size);

      if (typeof verdict === 'string') {
        setRejected({ code: verdict, text: REJECTION[verdict], file: file.name });
        return;
      }

      // Do not let the DOM-supplied filename or MIME type reach a browser
      // renderer. `inspect` derives a canonical type from magic bytes, and the
      // preview URL names a fresh Blob carrying only that type and those bytes.
      const url = URL.createObjectURL(new Blob([bytes], { type: verdict.contentType }));
      const mediaId = crypto.randomUUID();

      const asset: MediaAsset = {
        mediaId,
        kind: verdict.kind,
        fileName: file.name,
        contentType: verdict.contentType,
        bytes: file.size,
        durationMs: verdict.kind === 'audio' ? await probeDuration(url) : null,
        checksum: await checksumOf(bytes),
        uploadedByName: operator.name,
        uploadedAt: new Date().toISOString(),
        retired: false,
      };

      rememberObjectUrl(mediaId, url);
      addMedia(asset);
      say({ tone: 'ok', text: `Đã nhận ${file.name}.` });
    } finally {
      setBusy(false);
      if (input.current !== null) input.current.value = '';
    }
  }

  const shown = kind === 'all' ? media : media.filter((m) => m.kind === kind);
  const mayUpload = operator.can('media.upload');

  return (
    <>
      <header className="cms-head">
        <h1>Kho media</h1>
        <p>
          Âm thanh, hình ảnh và tệp dùng trong đề. Một tệp dùng được cho nhiều đề — và một tệp đã ra
          tới học viên thì không thay được nữa.
        </p>
      </header>

      <PreviewNotice what="Kho dưới đây có sẵn bốn tệp mẫu." />

      {flash}

      {mayUpload && (
        <section className="cms-panel">
          <div className="cms-panel-head">
            <h2>Tải tệp lên</h2>
          </div>

          {/* Limits before the choice, not after it. The refusal message still
              names only the category — the thresholds themselves stay off the
              error path, the same rule the ZIP pipeline follows. */}
          <p className="cms-muted">
            Âm thanh mp3 · m4a · wav · ogg, tối đa {formatBytes(MAX_BYTES.audio)}. Hình ảnh png ·
            jpg · webp, tối đa {formatBytes(MAX_BYTES.image)}. Tài liệu pdf, tối đa{' '}
            {formatBytes(MAX_BYTES.file)}.
          </p>

          <label className="cms-drop">
            <input
              ref={input}
              type="file"
              disabled={busy}
              onChange={(event) => {
                const file = event.target.files?.[0];
                if (file !== undefined) void take(file);
              }}
            />
            <span>{busy ? 'Đang đọc tệp…' : 'Chọn tệp'}</span>
          </label>

          {rejected !== null && (
            <div className="cms-alert is-bad" role="alert">
              <strong className="cms-code">{rejected.code}</strong> {rejected.text}{' '}
              <span className="cms-muted">({rejected.file})</span>
            </div>
          )}

          <p className="cms-muted">
            Kiểm tra ở đây đọc magic bytes của tệp, không tin phần đuôi tên — nhưng nó là để báo sớm
            cho bạn, <strong>không phải hàng rào an toàn</strong>. Máy chủ sẽ kiểm lại từ đầu khi
            đường tải lên thật có mặt.
          </p>
        </section>
      )}

      <div className="cms-filters" role="group" aria-label="Lọc theo loại">
        <Chip active={kind === 'all'} onClick={() => setKind('all')} count={media.length}>
          Tất cả
        </Chip>
        {(['audio', 'image', 'file'] as MediaKind[]).map((k) => (
          <Chip
            key={k}
            active={kind === k}
            onClick={() => setKind(k)}
            count={media.filter((m) => m.kind === k).length}
          >
            {KIND_LABEL[k]}
          </Chip>
        ))}
      </div>

      {shown.length === 0 && (
        <div className="cms-empty">
          <h3>{media.length === 0 ? 'Kho đang trống' : 'Không có tệp nào thuộc loại này'}</h3>
          <p>
            {media.length === 0
              ? 'Tải một tệp âm thanh lên để bắt đầu.'
              : 'Đổi bộ lọc để xem các loại khác.'}
          </p>
        </div>
      )}

      {shown.length > 0 && (
        <div className="cms-table-wrap">
          <table className="cms-table">
            <thead>
              <tr>
                <th>Tệp</th>
                <th>Dung lượng</th>
                <th>Thời lượng</th>
                <th>Trạng thái</th>
                <th>Đang dùng ở</th>
                <th>Hành động</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((asset) => {
                const users = usedBy(asset, versions);
                const state = assetState(asset, versions);
                const url = objectUrlFor(asset.mediaId);

                return (
                  <tr key={asset.mediaId}>
                    <td>
                      {asset.fileName}
                      <span className="cms-sub">
                        {KIND_LABEL[asset.kind]} · {asset.contentType} ·{' '}
                        <span className="cms-code">{asset.checksum.slice(0, 12)}</span>
                      </span>
                      {asset.kind === 'audio' && url !== null && (
                        // Byte-sniffed browser blob URL only. codeql[js/xss-through-dom]
                        <audio controls src={url} preload="metadata" />
                      )}
                      {asset.kind === 'audio' && url === null && (
                        <span className="cms-sub">
                          {uploadedHere(asset.mediaId)
                            ? 'Tệp chỉ tồn tại trong phiên đã tải lên — nạp lại trang là mất phần phát thử.'
                            : 'Tệp mẫu — không có nội dung thật để phát.'}
                        </span>
                      )}
                    </td>
                    <td className="num">{formatBytes(asset.bytes)}</td>
                    <td className="num">{formatDuration(asset.durationMs)}</td>
                    <td>
                      <span
                        className={`cms-badge is-${badgeTone(state)}`}
                        title={ASSET_STATE[state].hint}
                      >
                        {ASSET_STATE[state].label}
                      </span>
                    </td>
                    <td>
                      {users.length === 0 ? (
                        <span className="cms-muted">—</span>
                      ) : (
                        <ul className="cms-usedby">
                          {users.map((v) => (
                            <li key={v.versionId}>{v.title}</li>
                          ))}
                        </ul>
                      )}
                    </td>
                    <td>
                      <div className="cms-row-actions">
                        {mayRetire(asset, versions) && operator.can('media.retire') && (
                          <button
                            type="button"
                            className="cms-secondary"
                            onClick={() => setPending({ asset, action: 'retire' })}
                          >
                            Gỡ khỏi bộ chọn
                          </button>
                        )}
                        {mayDelete(asset, versions) && operator.can('media.retire') && (
                          <button
                            type="button"
                            className="cms-danger"
                            onClick={() => setPending({ asset, action: 'delete' })}
                          >
                            Xoá
                          </button>
                        )}
                        {state === 'locked' && (
                          <span className="cms-muted">
                            Không sửa được — đề đã xuất bản đang dùng
                          </span>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      <Confirm
        open={pending !== null}
        title={pending?.action === 'delete' ? 'Xoá tệp này khỏi kho?' : 'Gỡ tệp này khỏi bộ chọn?'}
        confirmLabel={pending?.action === 'delete' ? 'Xoá' : 'Gỡ'}
        tone={pending?.action === 'delete' ? 'danger' : 'normal'}
        busy={false}
        onCancel={() => setPending(null)}
        onConfirm={() => {
          if (pending === null) return;
          if (pending.action === 'delete') deleteMedia(pending.asset.mediaId);
          else retireMedia(pending.asset.mediaId);
          say({
            tone: 'ok',
            text:
              pending.action === 'delete'
                ? `Đã xoá ${pending.asset.fileName}.`
                : `Đã gỡ ${pending.asset.fileName} khỏi bộ chọn.`,
          });
          setPending(null);
        }}
        body={
          <ul className="cms-consequences">
            {pending?.action === 'delete' ? (
              <>
                <li>Tệp biến mất khỏi kho và không lấy lại được.</li>
                <li>Chưa có đề nào tham chiếu tới tệp này, nên không đề nào bị ảnh hưởng.</li>
              </>
            ) : (
              <>
                <li>Không ai chọn được tệp này cho đề mới nữa.</li>
                <li>Đề đang dùng nó vẫn phát bình thường — không có gì đổi với học viên.</li>
              </>
            )}
          </ul>
        }
      />
    </>
  );
}

function badgeTone(state: string): string {
  if (state === 'locked') return 'live';
  if (state === 'in-use') return 'hold';
  if (state === 'retired') return 'muted';
  return 'neutral';
}

function Chip({
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
