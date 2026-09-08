import { Card, Spinner } from '@vni/ui';

/** `label` is required and announced politely — never a bare dot with no text. */
export function Default() {
  return <Spinner label="Đang tải…" />;
}

/** The labels it actually ships with, all saying what is loading. */
export function RealLabels() {
  return (
    <div style={{ display: 'grid', gap: 'var(--s-2)' }}>
      <Spinner label="Đang hoàn tất đăng nhập…" />
      <Spinner label="Đang chấm bài viết của bạn…" />
      <Spinner label="Đang tải đề thi…" />
    </div>
  );
}

/** Inside a card, which is where a section-level load actually appears. */
export function InCard() {
  return (
    <div style={{ maxWidth: 420 }}>
      <Card>
        <p style={{ margin: '0 0 var(--s-3)', fontWeight: 600 }}>Writing Task 2</p>
        <Spinner label="Đang chấm bài viết của bạn…" />
      </Card>
    </div>
  );
}
