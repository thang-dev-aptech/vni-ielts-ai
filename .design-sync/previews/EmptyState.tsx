import { Button, EmptyState } from '@vni/ui';

/**
 * Nothing here yet — and it says why. An empty state that offers a dead button
 * is worse than one that admits the feature is not built.
 */
export function Default() {
  return (
    <div style={{ maxWidth: 460 }}>
      <EmptyState title="Chưa có đề" description="Phần thi này chưa được xây dựng." />
    </div>
  );
}

/** With a live action — offered only when it actually goes somewhere. */
export function WithAction() {
  return (
    <div style={{ maxWidth: 460 }}>
      <EmptyState
        title="Bạn chưa làm bài nào"
        description="Kết quả sẽ hiện ở đây sau phiên thi đầu tiên của bạn."
        action={<Button variant="primary">Bắt đầu luyện thi</Button>}
      />
    </div>
  );
}

/** The not-built case: no action, because there is nothing honest to offer. */
export function NotBuiltYet() {
  return (
    <div style={{ maxWidth: 460 }}>
      <EmptyState
        title="Tài liệu"
        description="Tính năng này chưa được xây dựng. Chúng tôi sẽ báo khi có."
      />
    </div>
  );
}
