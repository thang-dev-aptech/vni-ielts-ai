/**
 * Strips Vietnamese diacritics for search matching.
 *
 * <b>Half of Vietnamese search input arrives unmarked.</b> Nobody types "từ
 * vựng" with the marks when they are hunting for something, so a plain
 * substring match finds nothing and the reader concludes the library is empty.
 *
 * NFD splits a letter from its combining marks, which the range then removes.
 * `đ` is not a composed character — it is its own letter — so it needs saying
 * separately or "dong ho" never matches "đồng hồ".
 */
export function fold(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'D')
    .toLowerCase()
    .trim();
}
