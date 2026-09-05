/**
 * One date format for the public pages.
 *
 * <b>Through `Intl`, never by slicing the string.</b> An ISO date sliced by
 * hand renders in UTC, which is seven hours behind Vietnam — enough that
 * anything published in the evening shows yesterday's date. It also silently
 * ignores the interface language, and `M-4` may add another.
 */
/*
 * The default is Vietnamese because that is the interface language today, not
 * because dates are Vietnamese. Callers inside a component should pass
 * `useI18n().locale` — the parameter existed from the start and nobody was
 * passing it, so `M-4` would have shipped English copy with Vietnamese dates.
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
