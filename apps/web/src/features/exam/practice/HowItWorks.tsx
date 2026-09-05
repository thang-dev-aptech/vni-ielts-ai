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
    /* "bài thì được lưu liên tục" overstated it: the runner has `queued` and
       `failed` save states precisely because a save can fail. Saying it saves
       *as you go* is true; saying it is continuous promises a guarantee the
       network does not give. */
    body: 'Đồng hồ do máy chủ giữ nên nó không dừng khi bạn mất mạng. Bài được gửi lên máy chủ trong lúc bạn làm, và thanh trạng thái trên đầu trang luôn nói phần cuối cùng đã lên tới nơi hay chưa.',
  },
  {
    n: '04',
    title: 'Nhận kết quả',
    body: 'Reading và Listening chấm theo đáp án, có điểm ngay khi hết phần. Writing và Speaking do AI chấm và luôn mang nhãn tham khảo.',
  },
  {
    n: '05',
    title: 'Làm lại',
    /* "cả hai buổi" was left over from a two-attempt example, in a sentence
       that had just said retakes were unlimited — and "không giới hạn" is
       itself a pricing promise `B-5a` has not made. */
    body: 'Mỗi lần làm là một buổi riêng, và mọi buổi đều nằm lại trong lịch sử để bạn so lại về sau.',
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
