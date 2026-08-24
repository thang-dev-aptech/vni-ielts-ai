/**
 * The three surfaces whose underlying capability does not exist yet.
 *
 * <b>Each names its dependency.</b> An operator who opens Đánh giá AI needs to
 * know whether to wait five minutes or raise it with someone — "sắp có" answers
 * neither. And none of them shows a zero, an empty table or a placeholder
 * chart: those are claims that we counted and found none.
 *
 * <b>This file should keep shrinking.</b> Nhật ký left it the moment the audit
 * log existed; each of these leaves the same way.
 */

function Pending({
  title,
  lead,
  what,
  waitingOn,
}: {
  title: string;
  lead: string;
  what: string[];
  waitingOn: string;
}) {
  return (
    <>
      <header className="cms-head">
        <h1>{title}</h1>
        <p>{lead}</p>
      </header>

      <section className="cms-panel">
        <h2>Màn này sẽ có</h2>
        <ul className="cms-notes">
          {what.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>

        <p className="cms-alert">
          <strong>Đang chờ:</strong> {waitingOn}
        </p>
      </section>
    </>
  );
}

export function EvaluationsPage() {
  return (
    <Pending
      title="Đánh giá AI"
      lead="Kết quả chấm Writing và Speaking, và hàng đợi khi chấm hỏng."
      what={[
        'Danh sách đánh giá, lọc theo kỹ năng và trạng thái',
        'Chi tiết một đánh giá: điểm từng tiêu chí và nhận xét',
        'Chạy lại một đánh giá đã hỏng',
        'Đây là màn duy nhất trong sản phẩm được phép hiện đầu ra thô sai của mô hình — band ngoài thang nửa bậc hiện là hỏng, không kẹp về giá trị gần nhất',
      ]}
      waitingOn="API AI. Chưa có nhà cung cấp nào được nối, nên chưa có đánh giá nào tồn tại."
    />
  );
}

export function PackagesPage() {
  return (
    <Pending
      title="Lịch sử gói"
      lead="Mọi lần tải gói lên, kể cả những lần bị từ chối."
      what={[
        'Thời điểm, người tải, tên tệp và kết quả',
        'Với gói bị từ chối: chặng nào từ chối và danh sách finding kèm vị trí trong tệp',
        'Thông điệp từ chối nêu hạng mục, không nêu con số ngưỡng',
      ]}
      waitingOn="Đường nhập ZIP. Hiện chưa có thực thể lưu lịch sử gói vì chưa có gói nào đi qua."
    />
  );
}

export function ConfigPage() {
  return (
    <Pending
      title="Cấu hình"
      lead="Các giá trị vận hành đổi được mà không cần triển khai lại."
      what={[
        'Nhà cung cấp AI đang dùng cho từng kỹ năng',
        'Số token mỗi thao tác tiêu tốn',
        'Hạn mức gói nhập',
      ]}
      waitingOn="Phần lớn nội dung màn này là các quyết định chưa chốt — số token mỗi giao dịch (B-5b) và operation nào bị trừ (B-5a). Dựng ô nhập cho một luật chưa tồn tại là mời người ta điền vào một con số bịa."
    />
  );
}
