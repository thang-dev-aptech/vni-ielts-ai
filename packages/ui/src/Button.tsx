import type { ButtonHTMLAttributes, ReactNode } from 'react';

export type ButtonVariant = 'primary' | 'secondary' | 'quiet' | 'danger';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  /**
   * Renders a busy label and disables the button.
   *
   * Separate from `disabled` on purpose: a busy button and an unavailable
   * button mean different things to someone reading the screen, and a form
   * that silently stops responding while a request is in flight is how people
   * end up double-submitting.
   */
  busy?: boolean;
  busyLabel?: string;
  fullWidth?: boolean;
  children: ReactNode;
}

/**
 * One primary action per view.
 *
 * `DESIGN.md` states it as a rule rather than a preference: never two solid
 * accent buttons in the same viewport. Secondary actions are outlined or
 * quiet. Inside an exam the primary action is always submit — nothing may
 * compete with it.
 */
export function Button({
  variant = 'primary',
  busy = false,
  busyLabel,
  fullWidth = false,
  disabled,
  children,
  style,
  ...rest
}: ButtonProps) {
  const palette: Record<ButtonVariant, React.CSSProperties> = {
    primary: { background: 'var(--acc)', color: '#fff', borderColor: 'var(--acc)' },
    secondary: { background: 'var(--card)', color: 'var(--ink)', borderColor: 'var(--line)' },
    quiet: { background: 'transparent', color: 'var(--acc)', borderColor: 'transparent' },
    // --bad is reserved for something that has actually broken or is about to
    // be destroyed. It is never used for ordinary emphasis.
    danger: { background: 'var(--bad)', color: '#fff', borderColor: 'var(--bad)' },
  };

  const isDisabled = disabled === true || busy;

  return (
    <button
      {...rest}
      disabled={isDisabled}
      aria-busy={busy}
      style={{
        ...palette[variant],
        width: fullWidth ? '100%' : undefined,
        padding: 'var(--s-3) var(--s-5)',
        fontSize: 'var(--t-16)',
        fontWeight: 600,
        lineHeight: 'var(--lh-body)',
        border: '1px solid',
        borderRadius: 'var(--r-sm)',
        cursor: isDisabled ? 'not-allowed' : 'pointer',
        opacity: isDisabled ? 0.6 : 1,
        transition: `background var(--dur) var(--ease), opacity var(--dur) var(--ease)`,
        ...style,
      }}
    >
      {busy && busyLabel ? busyLabel : children}
    </button>
  );
}
