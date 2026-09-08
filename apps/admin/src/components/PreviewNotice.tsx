/**
 * The line that stops a preview screen from being mistaken for a live one.
 *
 * <b>On every screen that runs on preview data, not once at sign-in.</b> An
 * operator arrives at a screen by link and by URL, not only by walking from
 * the front door, and a notice they did not pass is a notice that did not
 * happen. It is a banner rather than a footnote for the same reason: the cost
 * of someone believing these rows are real is a decision taken about content
 * that does not exist.
 *
 * <b>No longer names "vòng đời duyệt" specifically.</b> The exam review
 * lifecycle this notice used to explain now runs on real endpoints — only the
 * media library still has no server API to cut over to (no
 * `media.read`/`media.upload`/`media.retire` key exists yet in
 * `PermissionKeys.All`), so the copy stays generic to whatever screen is
 * still using this store.
 */
export function PreviewNotice({ what }: { what: string }) {
  return (
    <p className="cms-preview-notice" role="note">
      <strong>Dữ liệu xem trước.</strong> {what} Thao tác ở đây chỉ đổi trạng thái trong trình duyệt
      của bạn — không có gì được ghi lên hệ thống.
    </p>
  );
}
