/**
 * Format a server timestamp for CMS tables, or nothing.
 *
 * Empty strings and unparseable values used to render as "Invalid Date".
 * Missing is honest; a fabricated instant is not.
 */
export function formatAdminDate(
  value: string | null | undefined,
  style: 'date' | 'datetime' = 'date',
): string | null {
  if (value == null || value.trim() === '') return null;
  const ms = Date.parse(value);
  if (Number.isNaN(ms)) return null;
  const date = new Date(ms);
  return style === 'datetime'
    ? date.toLocaleString('vi-VN')
    : date.toLocaleDateString('vi-VN');
}
