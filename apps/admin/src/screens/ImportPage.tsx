import { useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { useOperator } from '../lib/operator.js';
import { useFlash } from '../chrome/Confirm.js';
import {
  uploadPackage,
  ImportApiError,
} from '../lib/adminApi.js';
import { AdminPaths } from '../routes/paths.js';
import { reasonOf } from './UserDetailPage.js';

/**
 * Screen 4.1 — Bringing an exam package into the durable package pipeline.
 *
 * Every non-empty upload creates a durable package row immediately and redirects
 * to the package detail/history screen where its progress and linked review draft
 * can be inspected.
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

export function ImportPage() {
  const { accessToken } = useAdminAuth();
  const operator = useOperator();
  const navigate = useNavigate();
  const { flash, say } = useFlash();

  const [file, setFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [rejection, setRejection] = useState<ImportApiError | null>(null);

  const input = useRef<HTMLInputElement>(null);

  const canUpload = operator.can('package.upload') && operator.can('exam.create');

  async function upload() {
    if (accessToken === null || file === null) return;
    setUploading(true);
    setRejection(null);

    try {
      const result = await uploadPackage(accessToken, file);
      say({ tone: 'ok', text: `Đã nhận gói ${file.name}.` });
      navigate(AdminPaths.package(result.packageId));
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

  return (
    <>
      <header className="cms-head">
        <h1>Nhập đề</h1>
        <p>
          Gói nhập thành công sẽ tạo bản ghi trong <strong>Lịch sử gói</strong> và tạo{' '}
          <strong>bản nháp</strong> sau khi xử lý — học viên chưa thấy được. Muốn đưa vào sử dụng thì
          cần một thao tác xuất bản riêng. Nguồn đề chưa có quyền tới học viên thì đăng ký tại{' '}
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

        <label className="cms-drop">
          <input
            ref={input}
            type="file"
            accept=".zip,.json,.docx,.pdf"
            disabled={uploading || !canUpload}
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
          <span>{file === null ? 'Chọn tệp gói đề' : file.name}</span>
        </label>

        {!canUpload && (
          <p className="cms-alert" role="status">
            Bạn cần cả hai quyền <code>package.upload</code> và <code>exam.create</code> để tải gói lên.
          </p>
        )}

        <div className="cms-version-actions">
          <button
            type="button"
            className="cms-primary"
            disabled={file === null || uploading || !canUpload}
            onClick={() => void upload()}
          >
            {uploading ? 'Đang tải lên và xử lý…' : 'Tải lên và xử lý'}
          </button>
        </div>

        {rejection !== null && <RejectionPanel error={rejection} />}
      </section>

      <section className="cms-panel">
        <h2>Gói đi qua bảy chặng</h2>
        <p className="cms-muted">
          Mỗi gói tải lên đều được lưu vết trong Lịch sử gói. Trạng thái và tiến trình xử lý
          sẽ được cập nhật tự động.
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
    </>
  );
}

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
