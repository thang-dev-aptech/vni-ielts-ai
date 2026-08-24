import { useState } from 'react';

/**
 * Screen 4.1 — bringing an exam package in.
 *
 * <b>The screen says where the package goes before it asks for one.</b> A
 * successful import produces a <i>draft</i>: nobody sits it until a separate
 * publish action, by someone with a separate permission. The specification
 * names this as the thing operators misread most often at this step, so it is
 * the first sentence rather than a footnote.
 *
 * <b>The size limit appears here and nowhere else.</b> Stating it before an
 * upload is a usability need; stating it in a <i>refusal</i> would tell an
 * attacker exactly which threshold to sit under. Rejections name the category
 * and never the number. → `cms-spec.md` ràng buộc 4, and its stated exception
 *
 * <b>Nothing is written until every stage passes.</b> The seven stages below
 * are the real pipeline from `zip-ingestion-security.md`, in order, and the
 * screen shows them because an operator whose 200-question package failed
 * needs to know which stage refused it.
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
  const [file, setFile] = useState<File | null>(null);

  return (
    <>
      <header className="cms-head">
        <h1>Nhập đề</h1>
        <p>
          Gói nhập thành công sẽ ra <strong>bản nháp</strong> — học viên chưa thấy được. Muốn đưa
          vào sử dụng thì cần một thao tác xuất bản riêng.
        </p>
      </header>

      <section className="cms-panel">
        <h2>Chọn gói</h2>

        <dl className="cms-facts">
          <div>
            <dt>Định dạng</dt>
            <dd>
              <code>.zip</code> chứa một hoặc nhiều đề, hoặc <code>.json</code> một đề
            </dd>
          </div>
          <div>
            <dt>Phiên bản định dạng</dt>
            <dd>
              <code>formatVersion 1.0</code>
            </dd>
          </div>
          <div>
            <dt>Dung lượng tối đa</dt>
            <dd>200 MB mỗi gói</dd>
          </div>
        </dl>

        <label className="cms-drop">
          <input
            type="file"
            accept=".zip,.json"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
          <span>{file === null ? 'Chọn tệp gói đề' : file.name}</span>
        </label>

        <div className="cms-version-actions">
          <button type="button" className="cms-primary" disabled>
            Tải lên và kiểm
          </button>
          <span className="cms-muted">
            Đường nhập ZIP chưa dựng — hiện chỉ nạp được gói JSON qua seeder lúc khởi động.
          </span>
        </div>
      </section>

      <section className="cms-panel">
        <h2>Gói sẽ đi qua bảy chặng</h2>
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
    </>
  );
}
