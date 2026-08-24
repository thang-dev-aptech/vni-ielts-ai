/**
 * The article index.
 *
 * <b>Placeholder editorial copy, standing in for a CMS collection.</b> Same
 * arrangement as the document catalogue: the field names are the ones an
 * article record will carry, so swapping this array for a fetch touches one
 * import. Three of these came from the confirmed design mock and the rest were
 * written to fill the page out; none of it is a product claim, and every one
 * of them will be replaced by something the academic team actually publishes.
 *
 * <b>No photographs of strangers.</b> The design mock loaded article
 * thumbnails from a third-party image CDN, which put a request to another
 * company's server on every card, made the page depend on a network the
 * product does not control, and illustrated Vietnamese IELTS material with
 * stock pictures of people who have nothing to do with it. Covers are drawn
 * from the article's own category instead — see `module-pages.css`.
 */

/**
 * What kind of post this is — not which skill it is about.
 *
 * <b>Changed 22/08 at the owner's direction.</b> The set was the four skills
 * plus vocabulary, which described the guides and had nowhere to put anything
 * else: a recruitment notice is not a Reading article. Skill is still on the
 * card, drawn from the tag inside the post; the filter is now about what the
 * reader came for.
 */
export type ArticleCategory = 'huong-dan' | 'bai-viet' | 'tuyen-dung';

export interface Article {
  /** The URL. `/articles/<slug>` — ids never appear in an address. */
  slug: string;
  title: string;
  excerpt: string;
  category: ArticleCategory;
  /** Rounded minutes. An estimate, and labelled as one. */
  readMinutes: number;
  author: string;
  /** ISO date, rendered through `Intl`. */
  publishedAt: string;
  /** Paragraphs. Plain strings until the CMS decides what a body is made of. */
  body: string[];
}

export const ARTICLE_CATEGORIES: { id: ArticleCategory | 'all'; label: string }[] = [
  { id: 'all', label: 'Tất cả' },
  { id: 'tuyen-dung', label: 'Tuyển dụng' },
  { id: 'huong-dan', label: 'Hướng dẫn' },
  { id: 'bai-viet', label: 'Bài viết' },
];

/**
 * The label a category wears on a card.
 *
 * <b>`tuyen-dung` has no posts, and that is deliberate.</b> The filter exists
 * because the owner asked for it; inventing a job advertisement to fill it
 * would be a different kind of placeholder from the rest of this file. Nobody
 * applies to a fake Writing tip. The chip shows an honest empty state until
 * VNI publishes a real opening.
 */
export const ARTICLE_CATEGORY_LABEL: Record<ArticleCategory, string> = {
  'huong-dan': 'Hướng dẫn',
  'bai-viet': 'Bài viết',
  'tuyen-dung': 'Tuyển dụng',
};

export const ARTICLES: Article[] = [
  {
    slug: 'mo-bai-writing-task-2',
    title: '5 cách mở bài Writing Task 2 tự nhiên và ghi trọn điểm Cohesion',
    excerpt:
      'Công thức paraphrase mở bài ngắn gọn, đúng trọng tâm đề thi mà không sợ lặp từ của đề.',
    category: 'huong-dan',
    readMinutes: 5,
    author: 'Ban chuyên môn VNI',
    publishedAt: '2026-08-15',
    body: [
      'Mở bài của Writing Task 2 chỉ có hai việc phải làm: nói lại đề bằng chữ của mình, và cho người chấm biết bài viết sẽ đi theo hướng nào. Mọi thứ dài hơn thế đều đang lấy chỗ của phần thân bài.',
      'Cách paraphrase an toàn nhất là đổi cấu trúc câu trước, đổi từ sau. Nếu đổi từ trước, bạn dễ rơi vào việc thay một từ quen bằng một từ hiếm mà mình chưa chắc dùng đúng ngữ cảnh — đó là lỗi làm mất điểm Lexical Resource chứ không phải cách kiếm thêm.',
      'Câu thứ hai của mở bài nên trả lời thẳng câu hỏi của đề. Với dạng opinion, đó là quan điểm của bạn. Với dạng discussion, đó là việc bạn sẽ bàn cả hai phía rồi mới kết luận. Người chấm cần biết điều đó ngay, không phải đợi đến kết bài.',
      'Cuối cùng, đừng viết mở bài trước khi có dàn ý thân bài. Mở bài là lời hứa, và một lời hứa viết trước khi biết mình sẽ nói gì thường là lời hứa bài viết không giữ được.',
    ],
  },
  {
    slug: 'luyen-speaking-theo-mo-hinh-paf',
    title: 'Luyện Speaking theo vòng lặp Prompt – Answer – Feedback',
    excerpt: 'Ba bước lặp lại mỗi ngày để kéo dài câu trả lời mà vẫn giữ được mạch nói tự nhiên.',
    category: 'huong-dan',
    readMinutes: 7,
    author: 'Ban chuyên môn VNI',
    publishedAt: '2026-08-11',
    body: [
      'Điểm yếu phổ biến nhất ở Speaking không phải là từ vựng, mà là câu trả lời dừng quá sớm. Người nói trả lời đúng câu hỏi trong một câu rồi im lặng, và phần còn lại của thời gian trôi qua.',
      'Vòng lặp Prompt – Answer – Feedback đặt ba việc cạnh nhau: lấy một câu hỏi, trả lời thành tiếng và ghi âm, rồi nghe lại chính mình trước khi xem bất kỳ nhận xét nào. Bước nghe lại là bước hay bị bỏ, và cũng là bước dạy được nhiều nhất — bạn nghe ra chỗ mình ngập ngừng rõ hơn bất kỳ ai nói cho bạn.',
      'Khi mở rộng câu trả lời, hãy thêm lý do rồi thêm ví dụ, theo đúng thứ tự đó. Thêm ví dụ trước khi có lý do làm câu trả lời nghe như một câu chuyện lạc đề.',
      'Điểm do AI chấm ở phần Speaking luôn mang nhãn tham khảo. Nó dùng để so bài hôm nay với bài hôm qua của chính bạn, không phải để dự đoán band trong phòng thi thật.',
    ],
  },
  {
    slug: 'collocations-theo-chu-de',
    title: 'Collocations theo chủ đề: học cụm, đừng học từ lẻ',
    excerpt: 'Vì sao ghi nhớ theo cụm giúp câu nói tự nhiên hơn hẳn so với học từng từ rời rạc.',
    category: 'huong-dan',
    readMinutes: 6,
    author: 'Ban chuyên môn VNI',
    publishedAt: '2026-08-04',
    body: [
      'Một từ đứng một mình gần như không có giá trị trong bài thi. Cái người chấm nghe thấy là cả cụm — và cụm sai thì từ có hiếm đến đâu cũng vẫn sai.',
      'Cách học hiệu quả là ghi lại nguyên cụm mỗi lần gặp: động từ đi với danh từ nào, tính từ đi với giới từ nào. Một cuốn sổ ghi cụm luôn hữu ích hơn một danh sách từ đơn dài gấp ba.',
      'Chọn cụm theo chủ đề hay ra đề — giáo dục, môi trường, công nghệ, sức khỏe — rồi tự đặt hai câu cho mỗi cụm. Câu tự đặt là thứ giữ được, danh sách đọc lướt thì không.',
    ],
  },
  {
    slug: 'doc-luot-ba-passage-trong-60-phut',
    title: 'Chia 60 phút Reading cho ba passage mà không hụt hơi ở bài cuối',
    excerpt:
      'Cách phân bổ thời gian và thứ tự làm để passage khó nhất không rơi vào lúc còn 5 phút.',
    category: 'huong-dan',
    readMinutes: 6,
    author: 'Ban chuyên môn VNI',
    publishedAt: '2026-07-28',
    body: [
      'Ba passage, sáu mươi phút, bốn mươi câu. Chia đều là hai mươi phút một bài, nhưng passage 3 gần như luôn khó hơn passage 1 — nên chia đều là cách chắc chắn nhất để hết giờ giữa chừng bài cuối.',
      'Phân bổ hợp lý hơn là 17 – 20 – 23. Passage 1 làm nhanh và dứt khoát, để dành phần thời gian dôi ra cho bài khó nhất.',
      'Đừng đọc hết passage rồi mới xem câu hỏi. Đọc câu hỏi trước cho biết mình đang đi tìm cái gì, và phần lớn dạng câu hỏi đều theo thứ tự thông tin trong bài.',
      'Trong bài thi trên máy, hãy đánh dấu câu chưa chắc thay vì ngồi lại với nó. Đồng hồ do máy chủ giữ, không phải trình duyệt của bạn — nên thời gian mất ở một câu là thời gian mất thật.',
    ],
  },
  {
    slug: 'nghe-so-va-ten-rieng-section-1',
    title: 'Section 1 mất điểm ở đâu: số, ngày tháng và tên riêng',
    excerpt:
      'Phần dễ nhất của Listening lại là phần nhiều người mất điểm nhất, và lý do khá cụ thể.',
    category: 'huong-dan',
    readMinutes: 4,
    author: 'Ban chuyên môn VNI',
    publishedAt: '2026-07-20',
    body: [
      'Section 1 được thiết kế để dễ, nên khi mất điểm ở đây thì gần như luôn vì cùng một nhóm lý do: nghe nhầm số, viết sai chính tả tên riêng, hoặc viết ngày tháng theo thói quen tiếng Việt.',
      'Số điện thoại thường được đọc theo nhóm và có "double" hoặc "triple" ở giữa. Tên riêng luôn được đánh vần, và phần đánh vần là phần bạn phải viết theo, không phải phần để đoán.',
      'Chính tả sai là câu sai, kể cả khi bạn nghe đúng. Đó là lý do phần này đáng luyện riêng thay vì luyện chung với ba section còn lại.',
    ],
  },
  {
    slug: 'diem-ai-cham-la-diem-tham-khao',
    title: 'Điểm AI chấm là điểm tham khảo — và vì sao điều đó tốt cho bạn',
    excerpt:
      'Reading và Listening chấm theo đáp án. Writing và Speaking do AI chấm, và luôn mang nhãn tham khảo.',
    category: 'bai-viet',
    readMinutes: 5,
    author: 'Ban chuyên môn VNI',
    publishedAt: '2026-07-12',
    body: [
      'Trong VNI IELTS AI, điểm Reading và Listening đến từ đáp án. Không mô hình nào can thiệp vào con số đó, và đó là lý do hai kỹ năng này chấm được ngay cả khi không có AI.',
      'Writing và Speaking thì khác. Điểm do mô hình đề xuất và luôn hiện kèm nhãn tham khảo, không đặt ngang hàng với điểm theo đáp án.',
      'Nhãn đó không phải là lời xin lỗi. Nó nói đúng thứ bạn nên dùng con số này để làm: so bài hôm nay với bài tuần trước, tìm ra tiêu chí nào đang kéo mình xuống, và luyện đúng chỗ đó. Một con số hứa hẹn band thi thật mà không ai kiểm chứng được thì hữu ích ít hơn nhiều.',
    ],
  },
  {
    slug: 'dong-ho-thi-do-may-chu-giu',
    title: 'Vì sao đồng hồ bài thi không nằm trong trình duyệt của bạn',
    excerpt:
      'Đóng tab, mất mạng hay đổi máy đều không làm đồng hồ dừng. Đây là lý do, và nó có lợi cho bạn.',
    category: 'bai-viet',
    readMinutes: 4,
    author: 'Ban chuyên môn VNI',
    publishedAt: '2026-07-05',
    body: [
      'Khi bạn bắt đầu một phần thi, máy chủ ghi lại thời điểm bắt đầu và tự tính ra hạn nộp. Trình duyệt chỉ hiển thị lại con số đó.',
      'Nghĩa là đóng tab giữa chừng, sập nguồn hay đổi sang điện thoại đều không làm đồng hồ dừng lại — nhưng cũng nghĩa là không ai kéo dài được thời gian bằng cách chỉnh máy mình.',
      'Bài làm thì ngược lại: nó được lưu liên tục lên máy chủ trong lúc bạn gõ. Mở lại là thấy đúng chỗ đang dở. Mất bài và mất giờ là hai chuyện khác nhau, và chỉ một trong hai là điều bạn phải lo.',
    ],
  },
  {
    slug: 'thi-thu-full-hay-luyen-tung-ky-nang',
    title: 'Thi thử full hay luyện từng kỹ năng: chọn theo thứ bạn đang thiếu',
    excerpt:
      'Hai chế độ giải quyết hai vấn đề khác nhau. Chọn nhầm thì buổi luyện không nói cho bạn điều bạn cần biết.',
    category: 'huong-dan',
    readMinutes: 5,
    author: 'Ban chuyên môn VNI',
    publishedAt: '2026-06-28',
    body: [
      'Luyện từng kỹ năng dừng lại ngay sau kỹ năng đó. Dùng nó khi bạn đã biết mình yếu chỗ nào và muốn lặp lại đúng chỗ đó nhiều lần.',
      'Thi thử full đi hết bốn kỹ năng trong một phiên và tự chuyển tiếp. Dùng nó khi câu hỏi của bạn là "tôi trụ được bao lâu" chứ không phải "tôi sai dạng nào".',
      'Sức bền là một kỹ năng riêng. Rất nhiều người làm tốt từng phần rời rạc nhưng tụt hẳn ở passage thứ ba, và chỉ một buổi full mới cho bạn thấy điều đó.',
    ],
  },
  {
    slug: 'bon-tieu-chi-writing-doc-the-nao',
    title: 'Đọc bảng điểm Writing: bốn tiêu chí nói gì về bài của bạn',
    excerpt:
      'Task Response, Coherence, Lexical Resource, Grammar — mỗi tiêu chí đo một thứ khác nhau và cần cách sửa khác nhau.',
    category: 'huong-dan',
    readMinutes: 7,
    author: 'Ban chuyên môn VNI',
    publishedAt: '2026-06-20',
    body: [
      'Một bài Writing không có một điểm, mà có bốn. Band bạn thấy là trung bình của chúng, nên hai người cùng band có thể yếu ở hai chỗ hoàn toàn khác nhau.',
      'Task Response hỏi bạn có trả lời đúng câu hỏi của đề không. Coherence hỏi người đọc có đi theo được mạch của bạn không. Lexical Resource và Grammar hỏi về vốn từ và cấu trúc.',
      'Sửa sai tiêu chí là cách phổ biến nhất để luyện mãi không lên. Học thêm từ vựng không cứu được một bài lạc đề, và bài đúng đề mà câu nào cũng giống câu nào thì thêm từ cũng không đủ.',
    ],
  },
  {
    slug: 'ghi-am-speaking-tai-nha',
    title: 'Ghi âm Speaking tại nhà sao cho bản ghi dùng được',
    excerpt:
      'Chất lượng bản ghi ảnh hưởng trực tiếp tới phần nhận xét. Ba thứ cần chỉnh trước khi bấm ghi.',
    category: 'huong-dan',
    readMinutes: 4,
    author: 'Ban chuyên môn VNI',
    publishedAt: '2026-06-12',
    body: [
      'Phòng vọng là kẻ thù lớn nhất. Một căn phòng có rèm, thảm hoặc giường sẽ cho bản ghi sạch hơn hẳn phòng trống tường gạch.',
      'Giữ khoảng cách ổn định với micro và đừng cầm điện thoại sát miệng — tiếng thở và tiếng chạm tay lấn át phụ âm cuối, mà phụ âm cuối lại là chỗ hay mất điểm phát âm.',
      'Nói hết câu rồi mới dừng ghi. Bản ghi bị cắt giữa chừng làm phần nhận xét về mạch nói trở nên vô nghĩa, vì nó không phân biệt được bạn ngập ngừng hay bạn bị cắt.',
    ],
  },
  {
    slug: 'vi-sao-khong-hien-diem-0',
    title: 'Vì sao chỗ chưa chấm hiện dấu gạch chứ không hiện 0',
    excerpt:
      'Band 0 là một điểm có thật trong IELTS. Đó chính là lý do chúng tôi không dùng nó để thay cho "chưa có".',
    category: 'bai-viet',
    readMinutes: 3,
    author: 'Ban chuyên môn VNI',
    publishedAt: '2026-06-05',
    body: [
      'Trong thang IELTS, band 0 nghĩa là không làm bài. Một người mở đề rồi không trả lời câu nào thật sự nhận band 0.',
      'Nếu chúng tôi cũng dùng số 0 cho những phần chưa chấm, hai tình huống hoàn toàn khác nhau sẽ trông giống hệt nhau — và bạn không có cách nào phân biệt.',
      'Nên chỗ chưa có điểm hiện dấu gạch. Nó nói "chưa đo", không nói "đo rồi và bằng không". Đó là điều kiện để bạn tin những con số còn lại trên màn hình.',
    ],
  },
  {
    slug: 'ba-loi-hay-gap-o-listening-map',
    title: 'Dạng map và plan trong Listening: ba lỗi lặp đi lặp lại',
    excerpt:
      'Mất phương hướng ở dạng bản đồ thường không phải vì nghe kém, mà vì chuẩn bị sai trước khi audio chạy.',
    category: 'huong-dan',
    readMinutes: 5,
    author: 'Ban chuyên môn VNI',
    publishedAt: '2026-05-29',
    body: [
      'Lỗi thứ nhất: không xác định hướng trước khi audio bắt đầu. Tìm điểm xuất phát và trục bắc–nam trong lúc còn thời gian đọc đề.',
      'Lỗi thứ hai: bám vào tên riêng thay vì bám vào từ chỉ vị trí. Người nói mô tả đường đi bằng "opposite", "at the end of", "just past" — đó mới là thứ dẫn bạn.',
      'Lỗi thứ ba: dừng lại để nghĩ. Audio không chờ, và một câu bỏ lỡ thường kéo theo hai câu sau. Đánh dấu rồi đi tiếp, quay lại lúc kiểm tra.',
    ],
  },
];

export function findArticle(slug: string): Article | undefined {
  return ARTICLES.find((article) => article.slug === slug);
}
