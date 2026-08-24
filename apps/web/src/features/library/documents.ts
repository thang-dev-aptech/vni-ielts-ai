/**
 * The document catalogue.
 *
 * <b>Placeholder content, and the shape is the deliverable.</b> There is no
 * documents endpoint — `M-23` describes the module in one sentence and no CMS
 * screen exists to publish anything — so this file stands in for the response
 * the API will eventually return. It is deliberately a plain array with the
 * field names a document record would have, so replacing it with a fetch is a
 * change to one import rather than a rewrite of the page.
 *
 * <b>`fileUrl` is optional on purpose.</b> Nothing has been uploaded, so no
 * entry has one, and the page renders those as *"sắp có"* rather than as a
 * download button that 404s. The moment the CMS publishes a file the entry
 * gains a URL and the button becomes live with no further change — which is
 * the whole reason the absent case is modelled rather than assumed away.
 *
 * <b>No counts, no ratings, no "tải nhiều nhất".</b> Those are numbers nobody
 * has measured, and the project has a standing rule against carrying invented
 * figures out of a design mock.
 */

export type DocumentCategory =
  | 'reading'
  | 'listening'
  | 'writing'
  | 'speaking'
  | 'vocabulary'
  | 'general';

export interface LibraryDocument {
  /** The address of the record. Stable, lowercase, no diacritics. */
  slug: string;
  title: string;
  summary: string;
  category: DocumentCategory;
  /** What the reader is about to open. Shown before they commit to it. */
  format: 'PDF' | 'DOCX' | 'MP3';
  /** Human-readable, because the exact byte count helps nobody choose. */
  size: string;
  pages?: number;
  /** ISO date. Rendered through `Intl`, never string-sliced. */
  updatedAt: string;
  /** Absent until the CMS publishes the file. */
  fileUrl?: string;

  /**
   * Free to anyone signed in, or part of VNI's paid material.
   *
   * <b>There is no price on this record, and that is not an omission.</b>
   * `B-4` (whether the product sells anything) and `B-5b` (what anything
   * costs) are both open. A number here would be an answer to a question the
   * owner has not answered, and it would reach a learner as a commitment.
   *
   * So a premium document is marked, described, and routed to a human — the
   * hotline — rather than to a checkout that does not exist. When the pricing
   * decision lands, this is the field it attaches to. → `G-11`
   */
  access: 'free' | 'premium';
}

export const DOCUMENT_CATEGORIES: { id: DocumentCategory | 'all'; label: string }[] = [
  { id: 'all', label: 'Tất cả' },
  { id: 'reading', label: 'Reading' },
  { id: 'listening', label: 'Listening' },
  { id: 'writing', label: 'Writing' },
  { id: 'speaking', label: 'Speaking' },
  { id: 'vocabulary', label: 'Từ vựng' },
  { id: 'general', label: 'Chung' },
];

export const DOCUMENTS: LibraryDocument[] = [
  {
    slug: 'bo-de-reading-theo-dang-cau-hoi',
    access: 'free',
    title: 'Bộ đề Reading theo dạng câu hỏi',
    summary: 'Phân loại theo từng dạng câu hỏi, kèm đáp án và giải thích vì sao đáp án đúng.',
    category: 'reading',
    format: 'PDF',
    size: '4,2 MB',
    pages: 68,
    updatedAt: '2026-08-12',
  },
  {
    slug: 'chien-thuat-doc-luot-va-doc-quet',
    access: 'free',
    title: 'Chiến thuật skimming và scanning',
    summary: 'Cách khoanh vùng thông tin trước khi đọc kỹ, áp dụng cho ba passage của một đề.',
    category: 'reading',
    format: 'PDF',
    size: '1,8 MB',
    pages: 24,
    updatedAt: '2026-07-30',
  },
  {
    slug: 'transcript-listening-section-1-4',
    access: 'free',
    title: 'Transcript Listening Section 1–4',
    summary: 'Bản ghi lời thoại kèm dấu vị trí đáp án, dùng để dò lại sau khi làm bài.',
    category: 'listening',
    format: 'PDF',
    size: '2,6 MB',
    pages: 41,
    updatedAt: '2026-08-05',
  },
  {
    slug: 'luyen-nghe-so-va-ten-rieng',
    access: 'free',
    title: 'Luyện nghe số, ngày tháng và tên riêng',
    summary: 'Phần hay mất điểm nhất ở Section 1: số điện thoại, địa chỉ và cách đánh vần tên.',
    category: 'listening',
    format: 'MP3',
    size: '38 MB',
    updatedAt: '2026-07-18',
  },
  {
    slug: 'cau-truc-writing-task-1-va-2',
    access: 'free',
    title: 'Cấu trúc bài Writing Task 1 & 2',
    summary: 'Dàn ý mẫu cho từng dạng đề, kèm cụm từ chuyển ý và lỗi bố cục thường gặp.',
    category: 'writing',
    format: 'DOCX',
    size: '820 KB',
    pages: 32,
    updatedAt: '2026-08-14',
  },
  {
    slug: 'ngan-hang-de-writing-task-2',
    access: 'premium',
    title: 'Ngân hàng đề Writing Task 2 theo chủ đề',
    summary: 'Đề chia theo chủ đề thường ra, mỗi đề có gợi ý ý tưởng cho cả hai phía lập luận.',
    category: 'writing',
    format: 'PDF',
    size: '3,1 MB',
    pages: 56,
    updatedAt: '2026-08-09',
  },
  {
    slug: 'bo-cau-hoi-speaking-part-1-2-3',
    access: 'premium',
    title: 'Bộ câu hỏi Speaking Part 1, 2, 3',
    summary: 'Câu hỏi theo chủ đề kèm cue card mẫu và hướng triển khai cho Part 3.',
    category: 'speaking',
    format: 'PDF',
    size: '1,4 MB',
    pages: 29,
    updatedAt: '2026-07-25',
  },
  {
    slug: 'tu-vung-hoc-thuat-theo-chu-de',
    access: 'premium',
    title: 'Từ vựng học thuật theo chủ đề',
    summary: 'Danh sách từ theo chủ đề thường gặp, có ví dụ đặt câu và cụm đi kèm.',
    category: 'vocabulary',
    format: 'PDF',
    size: '2,2 MB',
    pages: 47,
    updatedAt: '2026-08-16',
  },
  {
    slug: 'huong-dan-lam-quen-bai-thi-tren-may',
    access: 'premium',
    title: 'Hướng dẫn làm quen bài thi trên máy',
    summary: 'Thao tác trong phòng thi máy tính: đánh dấu câu, ghi chú và quản lý thời gian.',
    category: 'general',
    format: 'PDF',
    size: '960 KB',
    pages: 16,
    updatedAt: '2026-06-28',
  },
];
