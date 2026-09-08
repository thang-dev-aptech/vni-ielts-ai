import { Button } from '@vni/ui';

const Row = ({ children }: { children: React.ReactNode }) => (
  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--s-3)', alignItems: 'center' }}>
    {children}
  </div>
);

/** The primary axis: the four variants side by side. */
export function Variants() {
  return (
    <Row>
      <Button variant="primary">Nộp bài</Button>
      <Button variant="secondary">Lưu nháp</Button>
      <Button variant="quiet">Quay lại</Button>
      <Button variant="danger">Xoá bài làm</Button>
    </Row>
  );
}

/**
 * One primary action per viewport — DESIGN.md states this as a rule, not a
 * preference. Inside an exam the primary action is always submit.
 */
export function OnePrimaryPerView() {
  return (
    <Row>
      <Button variant="quiet">Quay lại</Button>
      <Button variant="secondary">Lưu nháp</Button>
      <Button variant="primary">Nộp bài</Button>
    </Row>
  );
}

/** `busy` renders `busyLabel` and blocks the press — this is what stops double-submits. */
export function Busy() {
  return (
    <Row>
      <Button variant="primary" busy busyLabel="Đang gửi…">
        Gửi
      </Button>
      <Button variant="secondary" busy busyLabel="Đang kiểm tra…">
        Xác minh
      </Button>
    </Row>
  );
}

/** Unavailable, not in flight — a different meaning from `busy`. */
export function Disabled() {
  return (
    <Row>
      <Button variant="primary" disabled>
        Nộp bài
      </Button>
      <Button variant="secondary" disabled>
        Lưu nháp
      </Button>
    </Row>
  );
}

/** Full-width is the mobile form footer. */
export function FullWidth() {
  return (
    <div style={{ display: 'grid', gap: 'var(--s-3)', maxWidth: 320 }}>
      <Button variant="primary" fullWidth>
        Đăng nhập
      </Button>
      <Button variant="secondary" fullWidth>
        Tiếp tục với Google
      </Button>
    </div>
  );
}
