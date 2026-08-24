/**
 * One date format for the public pages.
 *
 * <b>Through `Intl`, never by slicing the string.</b> An ISO date sliced by
 * hand renders in UTC, which is seven hours behind Vietnam — enough that
 * anything published in the evening shows yesterday's date. It also silently
 * ignores the interface language, and `M-4` may add another.
 */
export function formatDate(iso: string, locale = 'vi-VN'): string {
  const at = new Date(iso);
  if (Number.isNaN(at.getTime())) return '';

  return new Intl.DateTimeFormat(locale, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(at);
}
