import type { ReactNode } from 'react';

/**
 * Depth comes from three background layers plus a hairline border.
 * There is no shadow token in this system and none should be added —
 * `DESIGN.md` direction C dropped them deliberately.
 */
export function Card({
  children,
  sunk = false,
  style,
}: {
  children: ReactNode;
  sunk?: boolean;
  style?: React.CSSProperties;
}) {
  return (
    <div
      style={{
        background: sunk ? 'var(--sunk)' : 'var(--card)',
        border: `1px solid ${sunk ? 'var(--line-2)' : 'var(--line)'}`,
        borderRadius: sunk ? 'var(--r-md)' : 'var(--r-lg)',
        padding: 'var(--pad-card)',
        ...style,
      }}
    >
      {children}
    </div>
  );
}

export function PageHeader({ title, subtitle }: { title: string; subtitle?: string }) {
  return (
    <header style={{ marginBottom: 'var(--s-6)' }}>
      <h1>{title}</h1>
      {subtitle !== undefined && (
        <p style={{ marginTop: 'var(--s-2)', color: 'var(--muted)' }}>{subtitle}</p>
      )}
    </header>
  );
}
