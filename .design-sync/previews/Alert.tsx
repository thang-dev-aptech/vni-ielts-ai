import { Alert } from '@vni/ui';

/**
 * The full tone axis. `error` means something has broken — DESIGN.md law L1
 * reserves red for failure, so "sắp hết giờ" is a `warning`, never an `error`.
 */
export function Tones() {
  return (
    <div style={{ display: 'grid', gap: 'var(--s-3)' }}>
      <Alert tone="info">Bài thi gồm 4 kỹ năng, làm lần lượt trong một phiên.</Alert>
      <Alert tone="success">Đã lưu bài làm của bạn.</Alert>
      <Alert tone="warning">Còn 5 phút cho phần thi này.</Alert>
      <Alert tone="error">Không gửi được bài làm. Vui lòng thử lại.</Alert>
    </div>
  );
}

/** `title` is a bold first line above the body. */
export function WithTitle() {
  return (
    <div style={{ display: 'grid', gap: 'var(--s-3)' }}>
      <Alert tone="error" title="Không kết nối được máy chủ">
        Bài làm của bạn vẫn được giữ trên máy. Kiểm tra kết nối rồi thử lại.
      </Alert>
      <Alert tone="success" title="Đã xác minh email">
        Bạn có thể bắt đầu luyện thi ngay bây giờ.
      </Alert>
    </div>
  );
}

/**
 * Urgency without failure. Red here would train people to ignore red, which is
 * the failure mode L1 exists to prevent.
 */
export function UrgencyIsWarningNotError() {
  return (
    <div style={{ display: 'grid', gap: 'var(--s-3)' }}>
      <Alert tone="warning" title="Sắp hết giờ">
        Còn 2 phút cho phần Writing Task 2.
      </Alert>
      <Alert tone="info">Điểm AI là điểm tham khảo, không phải điểm thi chính thức.</Alert>
    </div>
  );
}
