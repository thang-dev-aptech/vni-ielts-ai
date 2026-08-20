import { Component, type ErrorInfo, type ReactNode } from 'react';
import { ErrorState } from '@vni/ui';

/**
 * Catches a render failure so one broken component does not blank the page.
 *
 * A class component because React offers no hook equivalent —
 * `componentDidCatch` has no functional counterpart.
 *
 * <b>Strings are hard-coded here, deliberately.</b> Everything else in the app
 * goes through i18n, but this boundary has to work when rendering is already
 * failing — and one plausible cause is the i18n provider itself throwing.
 * Calling `useI18n` here would turn a recoverable error into a blank screen,
 * which is exactly what this exists to prevent.
 */
interface Props {
  children: ReactNode;
}

interface State {
  error: Error | null;
}

export class ErrorBoundary extends Component<Props, State> {
  override state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    // eslint-disable-next-line no-console
    console.error('Unhandled render error', error, info.componentStack);
  }

  override render(): ReactNode {
    if (this.state.error === null) return this.props.children;

    return (
      <div className="container" style={{ paddingBlock: 'var(--s-7)', maxWidth: 560 }}>
        <ErrorState
          title="Trang gặp sự cố / This page hit a problem"
          description="Bạn có thể tải lại trang. Nếu lỗi lặp lại, vui lòng báo cho chúng tôi. / You can reload. If it keeps happening, please tell us."
          action={
            <button
              onClick={() => window.location.reload()}
              style={{
                padding: 'var(--s-3) var(--s-5)',
                fontSize: 'var(--t-16)',
                fontWeight: 600,
                color: '#fff',
                background: 'var(--acc)',
                border: '1px solid var(--acc)',
                borderRadius: 'var(--r-sm)',
                cursor: 'pointer',
              }}
            >
              Tải lại / Reload
            </button>
          }
        />
      </div>
    );
  }
}
