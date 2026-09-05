import { STATE, type ExamState } from '../lib/lifecycle.js';

/**
 * A version's state, said in a word.
 *
 * <b>Shape and word, not colour alone.</b> Six states is past the point where
 * hue can carry the meaning on its own — and a status column read in
 * greyscale, printed, or by an operator who cannot separate green from red
 * still has to say which rows are live. The label is the signal; the tint is
 * reinforcement.
 *
 * Unknown strings render verbatim rather than falling back to a friendly
 * label. A state the client does not recognise is a real event — the server
 * grew one — and dressing it as "Bản nháp" would hide that.
 */
export function StatusBadge({ status }: { status: ExamState | string }) {
  const face = STATE[status as ExamState];

  if (face === undefined) {
    return <span className="cms-badge is-unknown">{status}</span>;
  }

  return (
    <span className={`cms-badge is-${face.tone}`} title={face.hint}>
      {face.label}
    </span>
  );
}
