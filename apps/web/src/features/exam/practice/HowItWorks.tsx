/**
 * Five steps, on a rail.
 *
 * <b>The rail is what makes five cards into a sequence.</b> Five boxes in a row
 * with a number on each is a feature grid; a line running through the markers
 * is an order. Same technique as the brand timeline on the landing page, and
 * the same rule applies below its breakpoint — a horizontal rail through a
 * vertical stack points nowhere, so it becomes a vertical one.
 *
 * <b>Step 4 is where this page has to be careful.</b> "Xem kết quả" is true for
 * Reading and Listening today and is the part of the product `F-1` has scoped
 * but not built for Writing and Speaking, so the copy says where each kind of
 * score comes from rather than promising a number.
 */
const STEPS = [
  {
    n: '01',
    title: 'Chọn kỹ năng',
    body: 'Reading, Listening, Writing hay Speaking. Hoặc chọn thi thử full để đi hết cả bốn trong một phiên.',
  },
  {
    n: '02',
    title: 'Chọn đề',
    body: 'Mỗi đề ghi rõ số câu và thời lượng. Lọc theo loại đề hoặc thời gian bạn đang có.',
  },
  {
    n: '03',
    title: 'Làm bài',
    body: 'Đồng hồ do máy chủ giữ nên nó không dừng khi bạn mất mạng — nhưng bài thì được lưu liên tục.',
  },
  {
    n: '04',
    title: 'Nhận kết quả',
    body: 'Reading và Listening chấm theo đáp án, có điểm ngay khi hết phần. Writing và Speaking do AI chấm và luôn mang nhãn tham khảo.',
  },
  {
    n: '05',
    title: 'Làm lại',
    body: 'Không giới hạn số lần. Mỗi lần là một buổi riêng và cả hai buổi đều nằm lại trong lịch sử.',
  },
];

export function HowItWorks() {
  return (
    <section className="section how-section" id="how" tabIndex={-1}>
      <div className="container">
        <div className="section-heading centered" data-reveal>
          <div className="eyebrow green-eyebrow">Cách luyện</div>
          <h2>Từ lúc mở trang đến lúc có điểm.</h2>
        </div>

        <ol className="steps" data-reveal data-reveal-stagger>
          {STEPS.map((step) => (
            <li className="step" key={step.n}>
              <span className="step-n num" aria-hidden="true">
                {step.n}
              </span>
              <h3>{step.title}</h3>
              <p>{step.body}</p>
            </li>
          ))}
        </ol>
      </div>
    </section>
  );
}
