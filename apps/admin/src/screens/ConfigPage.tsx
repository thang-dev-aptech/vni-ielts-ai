import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, TRANSPORT_ERROR } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';
import {
  getRuntimeConfiguration,
  type AdminRuntimeConfiguration,
} from '../lib/adminApi.js';
import { cmsBadgeTone } from '../lib/lifecycle.js';

/**
 * Live runtime configuration, as an operator is allowed to see it.
 *
 * <b>Read-only on purpose.</b> `config.read` opens this screen; `config.update`
 * is seeded and unused. The values here come from process configuration, not
 * from a form this CMS can write, and inventing an editor would imply a save
 * path that does not exist.
 *
 * <b>Token pricing stays Pending even when the rest of the page has loaded.</b>
 * `B-5a` and `B-5b` are unresolved (`Q-05` decided the currency, not the
 * amounts). A number in that panel would be a claim. The other panels still
 * render, because an unconfigured price is not a reason to hide the rubric
 * version or the ZIP caps that are already enforced.
 *
 * Route composition (`App.tsx` / `PendingPages.tsx`) is owned by
 * admin-route-composition; this file is the screen that lands there.
 */
export function ConfigPage() {
  const { accessToken } = useAdminAuth();
  const [config, setConfig] = useState<AdminRuntimeConfiguration | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    setLoadError(null);
    try {
      const next = await getRuntimeConfiguration(accessToken);
      if (!alive.current) return;
      setConfig(next);
    } catch (error) {
      if (!alive.current) return;
      // Leave `config` as it was — a failed refresh must not replace live
      // caps with zeroes, and a first failure must not invent a blank table
      // of zeros either.
      setConfig(null);
      setLoadError(describe(error));
    }
  }, [accessToken]);

  useEffect(() => void load(), [load]);

  const token = config?.tokenPricing ?? { status: 'pending', blockers: ['B-5a', 'B-5b'] };

  return (
    <>
      <header className="cms-head">
        <h1>Cấu hình</h1>
        <p>
          Giá trị đang hiệu lực trên máy chủ này. Chỉ xem — không sửa từ đây, vì chưa có đường
          ghi.
        </p>
      </header>

      {config === null && loadError === null && (
        <p className="cms-muted" aria-live="polite">
          Đang tải cấu hình…
        </p>
      )}

      {loadError !== null && (
        <div className="cms-alert is-bad" role="alert">
          <strong>Không đọc được cấu hình.</strong> {loadError} Các hạn mức nhập không được hiện
          bằng số 0.
          <button type="button" className="cms-button cms-button--secondary" onClick={() => void load()}>
            Thử lại
          </button>
        </div>
      )}

      {config !== null && (
        <>
          <ProvidersPanel skills={config.ai.skills} />
          <WritingPanel writing={config.writing} />
          <ImportPanel archive={config.importArchive} />
        </>
      )}

      <TokenPanel status={token.status} blockers={token.blockers} />
    </>
  );
}

function ProvidersPanel({
  skills,
}: {
  skills: AdminRuntimeConfiguration['ai']['skills'];
}) {
  return (
    <section className="cms-panel" aria-labelledby="config-providers">
      <div className="cms-panel-head">
        <h2 id="config-providers">Nhà cung cấp theo kỹ năng</h2>
      </div>
      <p className="cms-muted">
        Chỉ mô hình và phiên bản. Khoá, endpoint và chuỗi kết nối không đi qua API này.
      </p>
      <div className="cms-table-wrap">
        <table className="cms-table">
          <thead>
            <tr>
              <th>Kỹ năng</th>
              <th>Trạng thái</th>
              <th>Nhà cung cấp</th>
              <th>Mô hình</th>
              <th>Phiên bản</th>
            </tr>
          </thead>
          <tbody>
            {skills.map((row) => {
              const configured = row.status === 'available';
              return (
                <tr key={row.skill}>
                  <td>
                    {skillLabel(row.skill)}
                    <span className="cms-sub cms-code">{row.skill}</span>
                  </td>
                  <td>
                    <span className="cms-badge" data-tone={cmsBadgeTone(configured ? 'published' : 'draft')}>
                      {configured ? 'Đã cấu hình' : 'Chưa cấu hình'}
                    </span>
                  </td>
                  <td>{present(row.provider)}</td>
                  <td>
                    <span className="cms-code">{present(row.model)}</span>
                    {row.fallback !== null && (
                      <span className="cms-sub">
                        Dự phòng: {present(row.fallback.provider)} · {present(row.fallback.model)}
                      </span>
                    )}
                  </td>
                  <td className="cms-code">{present(row.version)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function WritingPanel({
  writing,
}: {
  writing: AdminRuntimeConfiguration['writing'];
}) {
  return (
    <section className="cms-panel" aria-labelledby="config-writing">
      <div className="cms-panel-head">
        <h2 id="config-writing">Chấm Writing</h2>
      </div>
      <dl className="cms-detail-list">
        <dt>Phiên bản rubric</dt>
        <dd className="cms-code">{present(writing.rubricVersion)}</dd>
        <dt>Trọng số Task 1 : Task 2</dt>
        <dd className="num">{weights(writing.task1Weight, writing.task2Weight)}</dd>
        <dt>Ngôn ngữ phản hồi</dt>
        <dd>{present(writing.feedbackLanguage)}</dd>
        <dt>Độ chi tiết tiêu chí</dt>
        <dd>{present(writing.criterionGranularity)}</dd>
      </dl>
    </section>
  );
}

function ImportPanel({
  archive,
}: {
  archive: AdminRuntimeConfiguration['importArchive'];
}) {
  return (
    <section className="cms-panel" aria-labelledby="config-import">
      <div className="cms-panel-head">
        <h2 id="config-import">Hạn mức gói nhập</h2>
      </div>
      <p className="cms-muted">
        Các ngưỡng đang được cửa ZIP thi hành. Thông báo từ chối trên đường nhập không nêu các con
        số này.
      </p>
      <dl className="cms-facts">
        <div>
          <dt>Số entry tối đa</dt>
          <dd className="num">{archive.maxEntries.toLocaleString('vi-VN')}</dd>
        </div>
        <div>
          <dt>Tổng giải nén</dt>
          <dd className="num">{formatBytes(archive.maxTotalUncompressedBytes)}</dd>
        </div>
        <div>
          <dt>Một entry giải nén</dt>
          <dd className="num">{formatBytes(archive.maxEntryUncompressedBytes)}</dd>
        </div>
        <div>
          <dt>Tỉ lệ nén tối đa</dt>
          <dd className="num">{archive.maxCompressionRatio}:1</dd>
        </div>
        <div>
          <dt>Kích thước archive</dt>
          <dd className="num">{formatBytes(archive.maxArchiveBytes)}</dd>
        </div>
        <div>
          <dt>Hết giờ giải nén</dt>
          <dd className="num">{archive.extractionTimeoutSeconds} giây</dd>
        </div>
      </dl>
    </section>
  );
}

function TokenPanel({ status, blockers }: { status: string; blockers: string[] }) {
  const named = blockers.length > 0 ? blockers : ['B-5a', 'B-5b'];
  return (
    <section className="cms-panel" aria-labelledby="config-tokens">
      <div className="cms-panel-head">
        <h2 id="config-tokens">Kinh tế token</h2>
      </div>
      <p className="cms-alert">
        <strong>Pending.</strong> Màn này không hiện số token mỗi thao tác. Việc đó chờ{' '}
        {named.join(' và ')}
        {status !== 'pending' ? ` (máy chủ báo «${status}»)` : ''}. Dựng một ô nhập ở đây là mời
        điền một con số bịa.
      </p>
    </section>
  );
}

function skillLabel(skill: string): string {
  switch (skill) {
    case 'writing-marking':
      return 'Chấm Writing';
    case 'import-parser':
      return 'Parse gói nhập';
    case 'import-transcription':
      return 'Chép audio Listening';
    default:
      return skill;
  }
}

function weights(task1: number | null, task2: number | null): string {
  if (task1 === null || task2 === null) return '—';
  return `${task1} : ${task2}`;
}

function present(value: string | null | undefined): string {
  return value !== null && value !== undefined && value.length > 0 ? value : '—';
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  return `${(bytes / 1024 / 1024 / 1024).toFixed(1)} GB`;
}

function describe(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.problem.code === TRANSPORT_ERROR) {
      return 'Không kết nối được máy chủ. Kiểm tra mạng rồi thử lại.';
    }
    return error.problem.detail;
  }
  return 'Không đọc được cấu hình.';
}
