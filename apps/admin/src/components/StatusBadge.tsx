import { STATE, cmsBadgeTone, type ExamState } from '../lib/lifecycle.js';

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
 * grew one — and dressing it as "Bản nháp" would hide that. The badge tone
 * for that case is `warning` via {@link cmsBadgeTone}, never a silent `ok`.
 */
export function StatusBadge({ status }: { status: ExamState | string }) {
  const face = STATE[status as ExamState];

  if (face === undefined) {
    return (
      <span className="cms-badge" data-tone={cmsBadgeTone('unknown')}>
        {status}
      </span>
    );
  }

  return (
    <span className="cms-badge" data-tone={cmsBadgeTone(face.tone)} title={face.hint}>
      {face.label}
    </span>
  );
}
