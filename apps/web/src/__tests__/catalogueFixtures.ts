import type { Article } from '../features/articles/articles.js';
import type { LibraryDocument } from '../features/library/documents.js';

/**
 * Catalogue content that exists only for the tests.
 *
 * <b>The shipped catalogues are empty on purpose</b> — the product carries only
 * content the owner supplies, and none has been supplied for articles or
 * documents yet. But the *behaviour* around them is real and worth keeping
 * under test: filtering by skill, searching without diacritics, refusing a
 * download for a file that was never published, routing a slug to its own page.
 *
 * <b>No entry carries a `fileUrl`, and that is not an oversight.</b> Nothing has
 * ever been uploaded behind a document record, and one of the tests asserts
 * that no download link is offered. A fixture handing out working links would
 * make the fixture, rather than the product, the thing under test.
 *
 * So the tests bring their own content rather than borrowing whatever happens
 * to ship. That is the arrangement they should always have had: a test that
 * reads `ARTICLES[0]` is a test that breaks when an editor reorders the index,
 * which is a false alarm about a real edit.
 */

export const TEST_ARTICLES: Article[] = [
  {
    slug: 'cach-lam-dang-bai-matching-headings',
    title: 'Cách làm dạng bài Matching Headings',
    excerpt: 'Đọc câu chủ đề trước, đối chiếu sau.',
    category: 'huong-dan',
    readMinutes: 6,
    author: 'Tổ chuyên môn VNI',
    publishedAt: '2026-08-01',
    body: ['Đoạn thứ nhất của bài hướng dẫn.', 'Đoạn thứ hai của bài hướng dẫn.'],
  },
  {
    slug: 'vi-sao-diem-ai-chi-mang-tinh-tham-khao',
    title: 'Vì sao điểm AI chỉ mang tính tham khảo',
    excerpt: 'Điểm do mô hình đưa ra được kiểm lại phía máy chủ trước khi hiển thị.',
    category: 'bai-viet',
    readMinutes: 4,
    author: 'Tổ kỹ thuật VNI',
    publishedAt: '2026-08-05',
    body: ['Một mô hình có thể trả về một con số hợp lệ mà sai.', 'Nên con số đó được tính lại.'],
  },
  {
    slug: 'lo-trinh-tu-5-5-len-6-5',
    title: 'Lộ trình từ 5.5 lên 6.5',
    excerpt: 'Ba tháng, và phần lớn thời gian nằm ở Writing.',
    category: 'huong-dan',
    readMinutes: 8,
    author: 'Tổ chuyên môn VNI',
    publishedAt: '2026-08-10',
    body: ['Bắt đầu bằng việc đo lại điểm hiện tại.'],
  },
  {
    slug: 'ky-thuat-ghi-chu-khi-nghe-part-4',
    title: 'Kỹ thuật ghi chú khi nghe Part 4',
    excerpt: 'Viết tắt có hệ thống, không viết đủ chữ.',
    category: 'bai-viet',
    readMinutes: 5,
    author: 'Tổ chuyên môn VNI',
    publishedAt: '2026-08-12',
    body: ['Part 4 không có khoảng nghỉ giữa chừng.'],
  },
];

export const TEST_DOCUMENTS: LibraryDocument[] = [
  {
    id: 'writing-task-1-mo-ta-bieu-do',
    slug: 'writing-task-1-mo-ta-bieu-do',
    title: 'Writing Task 1 — Mô tả biểu đồ',
    description: 'Cấu trúc bốn đoạn và bộ từ vựng xu hướng.',
    skill: 'writing',
    category: 'Writing',
    type: 'guide',
    format: 'PDF',
    targetBand: '6.0',
    size: '1,2 MB',
    updatedAt: '2026-08-02',
    access: 'free',
  },
  {
    id: 'writing-task-2-lap-dan-y',
    slug: 'writing-task-2-lap-dan-y',
    title: 'Writing Task 2 — Lập dàn ý',
    description: 'Dàn ý cho bốn kiểu đề thường gặp.',
    skill: 'writing',
    category: 'Writing',
    type: 'worksheet',
    format: 'PDF',
    size: '800 KB',
    updatedAt: '2026-08-06',
    access: 'free',
  },
  {
    id: 'reading-matching-headings-luyen-tap',
    slug: 'reading-matching-headings-luyen-tap',
    title: 'Reading — Luyện tập Matching Headings',
    description: 'Mười đoạn văn kèm đáp án.',
    skill: 'reading',
    category: 'Reading',
    type: 'practice',
    format: 'PDF',
    size: '2,4 MB',
    updatedAt: '2026-08-08',
    access: 'free',
  },
  {
    // Diacritics on purpose: half of Vietnamese search input arrives unmarked,
    // and `fold` is what makes "tu vung" find this row.
    id: 'tu-vung-hoc-thuat-theo-chu-de',
    slug: 'tu-vung-hoc-thuat-theo-chu-de',
    title: 'Từ vựng học thuật theo chủ đề',
    description: 'Sáu trăm từ, nhóm theo mười hai chủ đề thường gặp.',
    skill: 'vocabulary',
    category: 'Từ vựng',
    type: 'guide',
    format: 'PDF',
    size: '1,8 MB',
    updatedAt: '2026-08-09',
    access: 'free',
  },
  {
    id: 'bo-de-speaking-part-2',
    slug: 'bo-de-speaking-part-2',
    title: 'Bộ đề Speaking Part 2',
    description: 'Năm mươi cue card kèm gợi ý triển khai.',
    skill: 'speaking',
    category: 'Speaking',
    type: 'pdf',
    format: 'PDF',
    size: '3,1 MB',
    updatedAt: '2026-08-11',
    access: 'premium',
  },
];
