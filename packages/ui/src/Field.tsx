import { useId, type InputHTMLAttributes } from 'react';

interface FieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string;
  /** Shown under the field and announced to assistive technology. */
  error?: string | undefined;
  hint?: string | undefined;
}

/**
 * A labelled input.
 *
 * The label is a real `<label>` bound by id, not a placeholder. A placeholder
 * disappears the moment someone types, which leaves anyone who paused — or who
 * is using a screen reader — with no idea what the field was for.
 *
 * `aria-invalid` and `aria-describedby` are wired so an error is announced
 * rather than only shown in red. `DESIGN.md` requires state to survive the
 * greyscale test; colour alone is never the signal.
 */
export function Field({ label, error, hint, style, ...rest }: FieldProps) {
  const id = useId();
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;

  const describedBy = [error ? errorId : null, hint ? hintId : null].filter(Boolean).join(' ');

  return (
    <div style={{ marginBottom: 'var(--s-4)' }}>
      <label htmlFor={id} className="label" style={{ display: 'block' }}>
        {label}
      </label>

      <input
        {...rest}
        id={id}
        aria-invalid={error !== undefined}
        aria-describedby={describedBy === '' ? undefined : describedBy}
        style={{
          display: 'block',
          width: '100%',
          marginTop: 'var(--s-2)',
          padding: 'var(--s-3)',
          fontSize: 'var(--t-16)',
          lineHeight: 'var(--lh-body)',
          color: 'var(--ink)',
          background: 'var(--card)',
          border: `1px solid ${error !== undefined ? 'var(--bad)' : 'var(--line)'}`,
          borderRadius: 'var(--r-sm)',
          ...style,
        }}
      />

      {hint !== undefined && (
        <p
          id={hintId}
          style={{ marginTop: 'var(--s-2)', fontSize: 'var(--t-14)', color: 'var(--muted)' }}
        >
          {hint}
        </p>
      )}

      {error !== undefined && (
        <p
          id={errorId}
          style={{ marginTop: 'var(--s-2)', fontSize: 'var(--t-14)', color: 'var(--bad)' }}
        >
          {error}
        </p>
      )}
    </div>
  );
}
