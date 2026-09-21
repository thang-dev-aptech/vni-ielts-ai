/**
 * Surfaces whose screens do not exist yet.
 *
 * <b>This file should keep shrinking.</b> Nhật ký left it the moment the audit
 * log existed; Đánh giá AI, lịch sử gói and cấu hình left the same way, as
 * real screens rather than as placeholders that still claimed they were
 * waiting. What remains is not a page: dictation authoring and the two
 * analytics entries are inert labels in the sidebar, because a Pending page
 * they cannot open would be a second map of the same gap.
 *
 * When the last inert sidebar entry gains a screen, delete this file.
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

export { Pending };
