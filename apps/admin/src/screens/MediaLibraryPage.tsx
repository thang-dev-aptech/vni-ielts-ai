import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, TRANSPORT_ERROR } from '@vni/auth';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import { useAdminAuth } from '../lib/AdminAuth.js';
import {
  deleteMedia,
  fetchMediaObjectUrl,
  listMedia,
  retireMedia,
  uploadMedia,
  type AdminMediaAsset,
  type AdminMediaVersionReference,
} from '../lib/adminApi.js';
import { objectUrlFor, rememberObjectUrl } from '../lib/mediaUrls.js';
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
  probeDuration,
  usedBy,
  type MediaAsset,
  type MediaKind,
} from '../lib/media.js';

/**
 * Màn D1 — the media library, on the real API.
 *
 * <b>Why the CMS needs this before it needs an editor.</b> A Listening section
 * is audio. Until there is somewhere to put an mp3, an author composing one has
 * written a question about a sound the system cannot play — and the exam looks
 * finished right up to the moment a candidate presses play.
 *
 * <b>The cut-over.</b> This screen ran on `previewStore` (localStorage
 * fixtures) until the server media API landed; now every row, upload, retire
 * and delete is a real request. The client-side sniff (`lib/media.ts`) stays —
 * as early advice to the operator only; the server re-derives the type from
 * the bytes and its answer is the record of truth.
 *
 * <b>The column that matters most is "đang dùng ở đâu" — and it is honestly
 * empty today.</b> Which exam versions reference which media is a mapping the
 * version-asset slice will build; until it exists there is no truthful
 * non-empty value, and an invented one would be worse than a dash. The
 * retire/delete rules still run through `media.ts` with that empty list, so
 * the day the mapping arrives, this screen only gains a data source.
 */
export function MediaLibraryPage() {
  const operator = useOperator();
  const { accessToken } = useAdminAuth();
  const { flash, say } = useFlash();

  const [adminMedia, setAdminMedia] = useState<AdminMediaAsset[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [kind, setKind] = useState<MediaKind | 'all'>('all');
  const [busy, setBusy] = useState(false);
  const [rejected, setRejected] = useState<{ code: string; text: string; file: string } | null>(
    null,
  );
  const [pending, setPending] = useState<{ asset: AdminMediaAsset; action: 'retire' | 'delete' } | null>(
    null,
  );
  const input = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const { media: rows } = await listMedia(accessToken);
      setAdminMedia(rows);
      setLoadError(null);
    } catch (error) {
      setLoadError(describe(error));
    }
  }, [accessToken]);

  useEffect(() => void load(), [load]);

  async function take(file: File) {
    if (accessToken === null) return;
    setBusy(true);
    setRejected(null);

    try {
      // Early advice, from the file's own bytes — the server checks again
      // from scratch, and its verdict is the one that counts.
      const bytes = await file.arrayBuffer();
      const head = new Uint8Array(bytes, 0, Math.min(16, bytes.byteLength));
      const verdict = inspect(head, file.size);

      if (typeof verdict === 'string') {
        setRejected({ code: verdict, text: REJECTION[verdict], file: file.name });
        return;
      }

      // A local blob URL gives instant playback of what was just uploaded;
      // after a reload the row falls back to fetching through the API.
      const url = URL.createObjectURL(new Blob([bytes], { type: verdict.contentType }));
      const durationMs = verdict.kind === 'audio' ? await probeDuration(url) : null;

      const stored = await uploadMedia(accessToken, file, durationMs);
      rememberObjectUrl(stored.mediaId, url);

      say({ tone: 'ok', text: `Đã lưu ${file.name} vào kho.` });
      await load();
    } catch (error) {
      say({ tone: 'bad', text: describe(error) });
      await load();
    } finally {
      setBusy(false);
      if (input.current !== null) input.current.value = '';
    }
  }

  async function apply(adminAsset: AdminMediaAsset, action: 'retire' | 'delete') {
    if (accessToken === null) return;
    try {
      if (action === 'delete') await deleteMedia(accessToken, adminAsset.mediaId);
      else await retireMedia(accessToken, adminAsset.mediaId);
      say({
        tone: 'ok',
        text:
          action === 'delete'
            ? `Đã xoá ${adminAsset.fileName}.`
            : `Đã gỡ ${adminAsset.fileName} khỏi bộ chọn.`,
      });
    } catch (error) {
      say({ tone: 'bad', text: describe(error) });
    } finally {
      await load();
    }
  }

  /** Play through the API on demand — an <audio> element cannot carry a token. */
  async function attachPlayback(adminAsset: AdminMediaAsset) {
    if (accessToken === null) return;
    try {
      rememberObjectUrl(adminAsset.mediaId, await fetchMediaObjectUrl(accessToken, adminAsset.mediaId));
      setAdminMedia((current) => current); // re-render with the URL now remembered
    } catch (error) {
      say({ tone: 'bad', text: describe(error) });
    }
  }

  if (adminMedia === null && loadError === null) {
    return (
      <>
        <Head />
        <p className="cms-muted">Đang mở kho…</p>
      </>
    );
  }

  if (loadError !== null) {
    return (
      <>
        <Head />
        {flash}
        <div className="cms-alert is-bad" role="alert">
          <strong className="cms-code">Không mở được kho</strong> {loadError}
        </div>
      </>
    );
  }

  const rows = adminMedia ?? [];
  const shown = kind === 'all' ? rows : rows.filter((m) => m.kind === kind);
  const mayUpload = operator.can('media.upload');

  return (
    <>
      <Head />
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
            <span>{busy ? 'Đang tải lên…' : 'Chọn tệp'}</span>
          </label>

          {rejected !== null && (
            <div className="cms-alert is-bad" role="alert">
              <strong className="cms-code">{rejected.code}</strong> {rejected.text}{' '}
              <span className="cms-muted">({rejected.file})</span>
            </div>
          )}

          <p className="cms-muted">
            Kiểm tra ở đây đọc magic bytes của tệp để báo sớm — còn máy chủ kiểm lại từ đầu với
            chính những byte đó, và lời của máy chủ mới là quyết định.
          </p>
        </section>
      )}

      <div className="cms-filters" role="group" aria-label="Lọc theo loại">
        <Chip active={kind === 'all'} onClick={() => setKind('all')} count={rows.length}>
          Tất cả
        </Chip>
        {(['audio', 'image', 'file'] as MediaKind[]).map((k) => (
          <Chip
            key={k}
            active={kind === k}
            onClick={() => setKind(k)}
            count={rows.filter((m) => m.kind === k).length}
          >
            {KIND_LABEL[k]}
          </Chip>
        ))}
      </div>

      {shown.length === 0 && (
        <div className="cms-empty">
          <h3>{rows.length === 0 ? 'Kho đang trống' : 'Không có tệp nào thuộc loại này'}</h3>
          <p>
            {rows.length === 0
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
              {shown.map((adminAsset) => {
                // Convert AdminMediaAsset to MediaAsset
                const asset: MediaAsset = {
                  mediaId: adminAsset.mediaId,
                  kind: adminAsset.kind as MediaKind,
                  fileName: adminAsset.fileName,
                  contentType: adminAsset.contentType,
                  bytes: adminAsset.bytes,
                  durationMs: adminAsset.durationMs,
                  checksum: adminAsset.checksum,
                  uploadedByName: adminAsset.uploadedByName,
                  uploadedAt: adminAsset.uploadedAt,
                  retired: adminAsset.retired,
                };

                // Convert server version references to the media rule format
                const versions: import('../lib/media.js').ReferencingVersion[] = adminAsset.referencedBy.map(
                  (v: AdminMediaVersionReference) => ({
                    versionId: v.versionId,
                    title: v.title,
                    state: v.state,
                    assets: [
                      {
                        ref: `media/${asset.mediaId}`,
                        mediaId: asset.mediaId,
                        usedAt: 'Exam version',
                        kind: asset.kind,
                      },
                    ],
                  }),
                );
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
                        <button
                          type="button"
                          className="cms-secondary"
                          onClick={() => void attachPlayback(adminAsset)}
                        >
                          Phát qua máy chủ
                        </button>
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
                        <span className="cms-muted">
                          — <span className="cms-sub">(chưa có ánh xạ đề ↔ media)</span>
                        </span>
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
                            onClick={() => setPending({ asset: adminAsset, action: 'retire' })}
                          >
                            Gỡ khỏi bộ chọn
                          </button>
                        )}
                        {mayDelete(asset, versions) && operator.can('media.retire') && (
                          <button
                            type="button"
                            className="cms-danger"
                            onClick={() => setPending({ asset: adminAsset, action: 'delete' })}
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
          void apply(pending.asset, pending.action);
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

/** "The API refused" and "the API was not reached" need opposite advice. */
function describe(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.problem.code === TRANSPORT_ERROR) {
      return 'Không kết nối được máy chủ. Kiểm tra mạng rồi thử lại.';
    }
    return error.problem.detail;
  }
  return 'Có lỗi không mong muốn. Thử lại.';
}

function Head() {
  return (
    <header className="cms-head">
      <h1>Kho media</h1>
      <p>
        Âm thanh, hình ảnh và tệp dùng trong đề. Một tệp dùng được cho nhiều đề — và một tệp đã ra
        tới học viên thì không thay được nữa.
      </p>
    </header>
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
