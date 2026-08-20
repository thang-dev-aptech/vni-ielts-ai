import type { ReactNode } from 'react';

export type AlertTone = 'info' | 'success' | 'warning' | 'error';

/**
 * An inline message.
 *
 * <b>`error` means something has broken.</b> It is not for "time is running
 * out" or "you have not written enough yet" — `DESIGN.md` law L1 reserves red
 * for failure, and using it for ordinary progress trains people to ignore it.
 * Use `warning` for informational urgency.
 *
 * `role="alert"` only on error and warning: an assertive announcement
 * interrupts whatever a screen-reader user was reading, which is right for a
 * failure and rude for a confirmation.
 */
export function Alert({
  tone,
  title,
  children,
}: {
  tone: AlertTone;
  title?: string;
  children: ReactNode;
}) {
  const palette: Record<AlertTone, { fg: string; bg: string }> = {
    info: { fg: 'var(--acc-700)', bg: 'var(--acc-soft)' },
    success: { fg: 'var(--ok)', bg: 'var(--ok-soft)' },
    warning: { fg: 'var(--warn)', bg: 'var(--warn-soft)' },
    error: { fg: 'var(--bad)', bg: 'var(--bad-soft)' },
  };

  const { fg, bg } = palette[tone];
  const assertive = tone === 'error' || tone === 'warning';

  return (
    <div
      role={assertive ? 'alert' : 'status'}
      style={{
        padding: 'var(--s-3) var(--s-4)',
        marginBottom: 'var(--s-4)',
        background: bg,
        color: fg,
        border: `1px solid ${fg}`,
        borderRadius: 'var(--r-sm)',
        fontSize: 'var(--t-14)',
        lineHeight: 'var(--lh-body)',
      }}
    >
      {title !== undefined && (
        <strong style={{ display: 'block', marginBottom: 'var(--s-1)' }}>{title}</strong>
      )}
      {children}
    </div>
  );
}
