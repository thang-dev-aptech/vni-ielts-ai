import { PageHeader } from '@vni/ui';

/** Title plus a muted subtitle — the standard page opening. */
export function Default() {
  return <PageHeader title="Luyện thi IELTS" subtitle="Chọn một kỹ năng để bắt đầu, hoặc làm cả bài thi đầy đủ." />;
}

/** Subtitle is optional. */
export function TitleOnly() {
  return <PageHeader title="Hồ sơ" />;
}

/**
 * Vietnamese with full diacritics at display size — the case DESIGN.md calls a
 * hard constraint. Uppercase is never applied, because it strips the marks.
 */
export function VietnameseDisplay() {
  return (
    <PageHeader
      title="Kết quả phiên thi gần nhất"
      subtitle="Điểm AI mang nhãn tham khảo và không thay thế điểm thi chính thức. Reading và Listening được chấm theo đáp án."
    />
  );
}
