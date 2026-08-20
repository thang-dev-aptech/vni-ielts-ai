import type { ReactNode } from 'react';

/**
 * Loading, empty and error states.
 *
 * These are first-class components rather than inline `{loading && ...}`
 * because <b>this is where exam software actually breaks</b>. The screen
 * inventory's definition of done names them explicitly, and the previous
 * attempt at this product listed them as the one part worth keeping.
 */
export function Spinner({ label }: { label: string }) {
  return (
    <div
      role="status"
      aria-live="polite"
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--s-3)',
        padding: 'var(--s-5)',
        color: 'var(--muted)',
      }}
    >
      <span
        aria-hidden="true"
        style={{
          width: 16,
          height: 16,
          border: '2px solid var(--line)',
          borderTopColor: 'var(--acc)',
          borderRadius: '50%',
          // prefers-reduced-motion zeroes --dur, which would freeze this. A
          // fixed duration keeps the indicator honest for everyone; the
          // reduced-motion concern is large moving surfaces, not a 16px dot.
          animation: 'vni-spin 700ms linear infinite',
        }}
      />
      <span>{label}</span>
      <style>{`@keyframes vni-spin { to { transform: rotate(360deg) } }`}</style>
    </div>
  );
}

/**
 * Nothing here yet — and it says why, rather than showing a blank panel.
 *
 * An empty state that offers a dead button is worse than one that admits the
 * feature is not built.
 */
export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div
      style={{
        padding: 'var(--s-7) var(--s-5)',
        textAlign: 'center',
        background: 'var(--sunk)',
        border: '1px dashed var(--line)',
        borderRadius: 'var(--r-md)',
      }}
    >
      <p style={{ color: 'var(--ink)', fontWeight: 600 }}>{title}</p>
      <p style={{ marginTop: 'var(--s-2)', color: 'var(--muted)', fontSize: 'var(--t-14)' }}>
        {description}
      </p>
      {action !== undefined && <div style={{ marginTop: 'var(--s-4)' }}>{action}</div>}
    </div>
  );
}

export function ErrorState({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div
      role="alert"
      style={{
        padding: 'var(--s-6) var(--s-5)',
        textAlign: 'center',
        background: 'var(--bad-soft)',
        border: '1px solid var(--bad)',
        borderRadius: 'var(--r-md)',
      }}
    >
      <p style={{ color: 'var(--bad)', fontWeight: 600 }}>{title}</p>
      <p style={{ marginTop: 'var(--s-2)', color: 'var(--ink-2)', fontSize: 'var(--t-14)' }}>
        {description}
      </p>
      {action !== undefined && <div style={{ marginTop: 'var(--s-4)' }}>{action}</div>}
    </div>
  );
}
