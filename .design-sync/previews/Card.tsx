import { Button, Card } from '@vni/ui';

/**
 * The raised layer. Depth comes from three background layers plus a hairline
 * border — there is no shadow token in this system and none should be added.
 */
export function Raised() {
  return (
    <div style={{ maxWidth: 420 }}>
      <Card>
        <h2 style={{ marginTop: 0 }}>Academic Reading — Test 4</h2>
        <p style={{ color: 'var(--muted)' }}>
          3 đoạn văn · 40 câu hỏi · 60 phút. Chấm theo đáp án, không qua AI.
        </p>
        <Button variant="primary">Bắt đầu</Button>
      </Card>
    </div>
  );
}

/** `sunk` is the recessed layer — for content nested inside another surface. */
export function Sunk() {
  return (
    <div style={{ maxWidth: 420 }}>
      <Card sunk>
        <p style={{ margin: 0, fontWeight: 600 }}>Band tổng: 6.5</p>
        <p style={{ margin: 'var(--s-2) 0 0', color: 'var(--muted)' }}>
          Reading 7.0 · Listening 6.5 · Writing 6.0 · Speaking —
        </p>
      </Card>
    </div>
  );
}

/** The two layers together: sunk nests inside raised. That is the whole depth system. */
export function Nested() {
  return (
    <div style={{ maxWidth: 420 }}>
      <Card>
        <h2 style={{ marginTop: 0 }}>Kết quả phiên thi</h2>
        <Card sunk>
          <p style={{ margin: 0, fontWeight: 600 }}>Writing Task 2</p>
          <p style={{ margin: 'var(--s-2) 0 0', color: 'var(--muted)' }}>
            Đang chờ chấm — điểm sẽ hiện khi có kết quả.
          </p>
        </Card>
      </Card>
    </div>
  );
}

/** A list of cards is the learner app's most common layout. */
export function CardList() {
  return (
    <div style={{ display: 'grid', gap: 'var(--s-3)', maxWidth: 420 }}>
      <Card>
        <p style={{ margin: 0, fontWeight: 600 }}>Listening — Test 2</p>
        <p style={{ margin: 'var(--s-2) 0 0', color: 'var(--muted)' }}>40 câu · 30 phút</p>
      </Card>
      <Card>
        <p style={{ margin: 0, fontWeight: 600 }}>Writing — Test 1</p>
        <p style={{ margin: 'var(--s-2) 0 0', color: 'var(--muted)' }}>2 bài viết · 60 phút</p>
      </Card>
    </div>
  );
}
