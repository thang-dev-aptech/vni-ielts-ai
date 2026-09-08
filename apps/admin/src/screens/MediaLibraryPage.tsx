import { useCallback, useEffect, useRef, useState } from 'react';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import { useAdminAuth } from '../lib/AdminAuth.js';
import {
  deleteMedia as deleteMediaApi,
  listMedia,
  retireMedia as retireMediaApi,
  uploadMedia,
} from '../lib/adminApi.js';
import { useOperator } from '../lib/operator.js';
import {
  ASSET_STATE,
  KIND_LABEL,
  MAX_BYTES,
  REJECTION,
  assetState,
  formatBytes,
  formatDuration,
  inspect,
  mayDelete,
  mayRetire,
  usedBy,
  type MediaAsset,
  type MediaKind,
  type ReferencingVersion,
} from '../lib/media.js';
import { reasonOf } from './UserDetailPage.js';

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
 *
 * Wired to `GET/POST /api/v1/admin/media` and retire/delete. Client-side
 * `inspect` still runs first so the operator hears about a bad file before the
 * round-trip; the server re-validates from scratch. Usage columns stay empty
 * until a reference inventory lands on the list payload — the server still
 * refuses delete when anything references the file.
 */
export function MediaLibraryPage() {
  const { accessToken } = useAdminAuth();
  const operator = useOperator();

  const [media, setMedia] = useState<MediaAsset[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [kind, setKind] = useState<MediaKind | 'all'>('all');
  const [busy, setBusy] = useState(false);
  const [actionBusy, setActionBusy] = useState(false);
  const [rejected, setRejected] = useState<{ code: string; text: string; file: string } | null>(
    null,
  );
  const [pending, setPending] = useState<{ asset: MediaAsset; action: 'retire' | 'delete' } | null>(
    null,
  );
  const [previewUrls, setPreviewUrls] = useState<Record<string, string>>({});
  const previewUrlsRef = useRef<Record<string, string>>({});
  const input = useRef<HTMLInputElement>(null);
  const alive = useRef(true);
  const { flash, say } = useFlash();

  /** Reference inventory is not on the media list wire yet — keep the column. */
  const versions: ReferencingVersion[] = [];

  useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  useEffect(() => {
    previewUrlsRef.current = previewUrls;
  }, [previewUrls]);

  useEffect(() => {
    return () => {
      for (const url of Object.values(previewUrlsRef.current)) URL.revokeObjectURL(url);
    };
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const assets = await listMedia(accessToken);
      if (alive.current) {
        setMedia(assets);
        setFailed(false);
      }
    } catch {
      if (alive.current) setFailed(true);
    }
  }, [accessToken]);

  useEffect(() => {
    void load();
  }, [load]);

  async function take(file: File) {
    if (accessToken === null) return;

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

      // Early operator check only. The server sniffs magic bytes again and is
      // the real boundary. Do not trust the DOM filename or MIME type for
      // anything beyond display.
      const asset = await uploadMedia(accessToken, file);
      const url = URL.createObjectURL(new Blob([bytes], { type: verdict.contentType }));

      if (alive.current) {
        setPreviewUrls((prev) => ({ ...prev, [asset.mediaId]: url }));
        setMedia((prev) => [asset, ...(prev ?? []).filter((m) => m.mediaId !== asset.mediaId)]);
        say({ tone: 'ok', text: `Đã nhận ${file.name}.` });
      } else {
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      if (alive.current) say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      if (alive.current) setBusy(false);
      if (input.current !== null) input.current.value = '';
    }
  }

  async function confirmPending() {
    if (pending === null || accessToken === null) return;

    setActionBusy(true);
    try {
      if (pending.action === 'delete') {
        await deleteMediaApi(accessToken, pending.asset.mediaId);
        if (alive.current) {
          setMedia((prev) => (prev ?? []).filter((m) => m.mediaId !== pending.asset.mediaId));
          setPreviewUrls((prev) => {
            const next = { ...prev };
            const url = next[pending.asset.mediaId];
            if (url !== undefined) {
              URL.revokeObjectURL(url);
              delete next[pending.asset.mediaId];
            }
            return next;
          });
          say({ tone: 'ok', text: `Đã xoá ${pending.asset.fileName}.` });
        }
      } else {
        await retireMediaApi(accessToken, pending.asset.mediaId);
        if (alive.current) {
          setMedia((prev) =>
            (prev ?? []).map((m) =>
              m.mediaId === pending.asset.mediaId ? { ...m, retired: true } : m,
            ),
          );
          say({ tone: 'ok', text: `Đã gỡ ${pending.asset.fileName} khỏi bộ chọn.` });
        }
      }
      if (alive.current) setPending(null);
    } catch (error) {
      if (alive.current) {
        say({ tone: 'bad', text: reasonOf(error) });
        setPending(null);
      }
    } finally {
      if (alive.current) setActionBusy(false);
    }
  }

  const items = media ?? [];
  const shown = kind === 'all' ? items : items.filter((m) => m.kind === kind);
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

      {flash}

      {failed && (
        <p className="cms-alert is-bad" role="alert">
          Không tải được kho media.{' '}
          <button type="button" className="cms-secondary" onClick={() => void load()}>
            Thử lại
          </button>
        </p>
      )}

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
              disabled={busy || accessToken === null}
              onChange={(event) => {
                const file = event.target.files?.[0];
                if (file !== undefined) void take(file);
              }}
            />
            <span>{busy ? 'Đang tải lên…' : 'Chọn tệp'}</span>
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
            nhận tệp.
          </p>
        </section>
      )}

      <div className="cms-filters" role="group" aria-label="Lọc theo loại">
        <Chip active={kind === 'all'} onClick={() => setKind('all')} count={items.length}>
          Tất cả
        </Chip>
        {(['audio', 'image', 'file'] as MediaKind[]).map((k) => (
          <Chip
            key={k}
            active={kind === k}
            onClick={() => setKind(k)}
            count={items.filter((m) => m.kind === k).length}
          >
            {KIND_LABEL[k]}
          </Chip>
        ))}
      </div>

      {media === null && !failed && <p className="cms-muted">Đang tải…</p>}

      {media !== null && shown.length === 0 && (
        <div className="cms-empty">
          <h3>{items.length === 0 ? 'Kho đang trống' : 'Không có tệp nào thuộc loại này'}</h3>
          <p>
            {items.length === 0
              ? 'Tải một tệp âm thanh lên để bắt đầu.'
              : 'Đổi bộ lọc để xem các loại khác.'}
          </p>
        </div>
      )}

      {media !== null && shown.length > 0 && (
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
                const url = previewUrls[asset.mediaId] ?? null;

                return (
                  <tr key={asset.mediaId}>
                    <td>
                      {asset.fileName}
                      <span className="cms-sub">
                        {KIND_LABEL[asset.kind]} · {asset.contentType} ·{' '}
                        <span className="cms-code">{asset.checksum.slice(0, 12)}</span>
                      </span>
                      {asset.kind === 'audio' && url !== null && (
                        // Session blob from this upload only. codeql[js/xss-through-dom]
                        <audio controls src={url} preload="metadata" />
                      )}
                      {asset.kind === 'audio' && url === null && (
                        <span className="cms-sub">
                          Phát thử có sẵn trong phiên vừa tải lên — tải lại trang thì mất phần phát
                          thử cục bộ.
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
        busy={actionBusy}
        onCancel={() => {
          if (!actionBusy) setPending(null);
        }}
        onConfirm={() => void confirmPending()}
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
