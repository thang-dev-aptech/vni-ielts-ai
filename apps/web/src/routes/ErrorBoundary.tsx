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
      /*
       * <b>`data-error-boundary` is a test hook, and it is not decoration.</b>
       *
       * The suite's crash gate reads `console.error` for React's own report.
       * That is one detector, and it depends on a message React owns and on
       * `componentDidCatch` above still being called — delete that method and
       * the boundary keeps working while the gate goes quiet. This attribute
       * is a second, independent detector: it is in the DOM whenever this
       * apology is on screen, whatever React did or did not print.
       * → the crash gate in `src/test-setup.ts`
       */
      <div
        className="container"
        data-error-boundary=""
        style={{ paddingBlock: 'var(--s-7)', maxWidth: 560 }}
      >
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
