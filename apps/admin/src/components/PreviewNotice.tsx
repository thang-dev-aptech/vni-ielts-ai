/**
 * The line that stops a preview screen from being mistaken for a live one.
 *
 * <b>On every screen that runs on preview data, not once at sign-in.</b> An
 * operator arrives at a screen by link and by URL, not only by walking from
 * the front door, and a notice they did not pass is a notice that did not
 * happen. It is a banner rather than a footnote for the same reason: the cost
 * of someone believing these rows are real is a decision taken about content
 * that does not exist.
 */
export function PreviewNotice({ what }: { what: string }) {
  return (
    <p className="cms-preview-notice" role="note">
      <strong>Dữ liệu xem trước.</strong> {what} Máy chủ chưa có vòng đời duyệt, nên các thao tác ở
      đây chỉ đổi trạng thái trong trình duyệt của bạn — không có gì được ghi lên hệ thống.
    </p>
  );
}
