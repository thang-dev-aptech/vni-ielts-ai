import { formatBand, formatScoreState, type ScoreState } from '@vni/types';

/**
 * Stage 0 shell. Not a screen — the learner screens are blocked on `B-8`
 * (22 UI/UX proposals, 8 of which change the structure of the Reading,
 * Listening, Writing, Speaking and Results screens) and on task T2, the
 * screen inventory, which does not exist yet.
 *
 * What this does instead is prove the design system and the shared types are
 * wired, and put the four product laws on screen where a regression is
 * visible rather than theoretical.
 */
export function App() {
  const sections: Array<[string, ScoreState]> = [
    ['Reading', { status: 'scored', band: 7, provenance: 'answer-key' }],
    ['Listening', { status: 'scored', band: 6.5, provenance: 'answer-key' }],
    ['Writing', { status: 'evaluating' }],
    ['Speaking', { status: 'failed' }],
  ];

  return (
    <div className="container" style={{ paddingBlock: 'var(--s-8)' }}>
      <h1>VNI IELTS AI — Quản trị</h1>
      <p className="label" lang="vi" style={{ marginTop: 'var(--s-2)' }}>
        Nền tảng CMS — mật độ compact
      </p>

      <div className="card" style={{ marginTop: 'var(--s-6)' }}>
        <h2 lang="vi">Bốn luật sản phẩm</h2>
        <table style={{ width: '100%', marginTop: 'var(--s-4)', borderCollapse: 'collapse' }}>
          <tbody>
            {sections.map(([name, state]) => (
              <tr key={name} style={{ borderTop: '1px solid var(--line-2)' }}>
                <td style={{ padding: 'var(--s-3) 0' }}>{name}</td>
                <td className="num" style={{ padding: 'var(--s-3) 0', textAlign: 'right' }}>
                  {formatScoreState(state)}
                </td>
                <td
                  style={{
                    padding: 'var(--s-3) 0 var(--s-3) var(--s-4)',
                    color: 'var(--muted)',
                    fontSize: 'var(--t-14)',
                  }}
                  lang="vi"
                >
                  {state.status === 'scored'
                    ? 'Chấm theo đáp án'
                    : state.status === 'failed'
                      ? 'Chấm thất bại'
                      : 'Đang chấm'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <p style={{ marginTop: 'var(--s-4)', color: 'var(--muted)', fontSize: 'var(--t-14)' }}>
          {/* L3 in one line: absence is an em dash, and 0.0 is still a real band. */}
          Chưa có điểm hiện <span className="num">{formatBand(null)}</span>, không bao giờ hiện{' '}
          <span className="num">{formatBand(0)}</span>.
        </p>
      </div>
    </div>
  );
}
