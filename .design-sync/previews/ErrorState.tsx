import { Button, ErrorState } from '@vni/ui';

/** Announced as an alert, so a screen reader hears it rather than only seeing red. */
export function Default() {
  return (
    <div style={{ maxWidth: 460 }}>
      <ErrorState title="Không tải được đề thi" description="Kiểm tra kết nối rồi thử lại." />
    </div>
  );
}

/** With a recovery action — the shape the app's error boundary ships. */
export function WithAction() {
  return (
    <div style={{ maxWidth: 460 }}>
      <ErrorState
        title="Trang gặp sự cố"
        description="Bạn có thể tải lại trang. Nếu lỗi lặp lại, vui lòng báo cho chúng tôi."
        action={<Button variant="primary">Tải lại trang</Button>}
      />
    </div>
  );
}

/**
 * Mất kết nối giữa bài. Vietnamese only — the interface language is Vietnamese
 * with full diacritics, and the only English allowed is skill names and standard
 * IELTS terms (Task 1, Part 2, cue card, band).
 */
export function NetworkLost() {
  return (
    <div style={{ maxWidth: 460 }}>
      <ErrorState
        title="Mất kết nối"
        description="Bài làm của bạn được lưu trên máy này và sẽ gửi khi có mạng trở lại."
        action={<Button variant="secondary">Thử lại</Button>}
      />
    </div>
  );
}
