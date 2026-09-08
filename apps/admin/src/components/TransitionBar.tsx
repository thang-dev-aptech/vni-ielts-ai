import { useState } from 'react';
import { Confirm } from '../chrome/Confirm.js';
import { transitionsFor, type ExamState, type Transition } from '../lib/lifecycle.js';
import { useOperator } from '../lib/operator.js';

/**
 * Everything this operator may do to a version in this state, and the step
 * before doing it.
 *
 * <b>Takes a bare `state`, not a version object.</b> It used to take a whole
 * `PreviewVersion` from the now-retired preview lifecycle store and read
 * ownership off it (`ownedByMe`). The real server exposes no author on
 * `GET /api/v1/admin/exams`, so there is nothing to check client-side —
 * `transitionsFor` is permission-only now, and this component only needs to
 * know which state the version is in. That also makes it reusable from any
 * screen that renders a real `AdminExam` row, not only the retired preview
 * detail screen it was written for.
 *
 * <b>The buttons are derived, never listed.</b> Which ones appear comes from
 * the transition table filtered by state and permission — so a screen cannot
 * offer an action the model does not allow, and adding a transition later
 * puts a button on every screen that shows this bar without anyone
 * remembering to.
 *
 * <b>Returning an exam demands a reason.</b> `requiresNote` holds the confirm
 * button shut until there is one — the same rule the server enforces in
 * `ReturnToDraftEndpoint` ("Trả về đề cần kèm lý do"). An author who receives
 * "trả lại" with no note has to guess what to change, and guessing is how a
 * second round of review gets spent on the wrong thing.
 */
export function TransitionBar({
  state,
  onApply,
  blockedBy,
}: {
  state: ExamState;
  /**
   * Runs the real request. Awaited internally — the bar holds the confirm
   * button in its "Đang thực hiện…" state until this resolves, and closes the
   * dialog only then. Never expected to throw: the caller reports its own
   * failure (a flash message, same as every other write in this CMS) rather
   * than relying on this component to render one.
   */
  onApply: (transition: Transition, note: string) => Promise<void>;
  /**
   * Why a transition cannot run right now, in a sentence — or null when it can.
   *
   * <b>Held shut and explained, never hidden.</b> A button that vanishes when
   * an exam is missing its audio teaches nothing; the operator concludes the
   * screen is broken, or that they lack a permission. The button stays,
   * disabled, with the reason beside it, because the reason is the work.
   */
  blockedBy?: (transition: Transition) => string | null;
}) {
  const operator = useOperator();
  const [pending, setPending] = useState<Transition | null>(null);
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const open = transitionsFor(state, { can: operator.can });

  if (open.length === 0) return null;

  const wantsNote = pending !== null && pending.requiresNote === true;
  const blocked = pending?.requiresNote === true && note.trim() === '';

  function close() {
    setPending(null);
    setNote('');
  }

  async function confirm() {
    if (pending === null) return;
    setSubmitting(true);
    await onApply(pending, note);
    setSubmitting(false);
    close();
  }

  return (
    <>
      <div className="cms-actions">
        {open.map((transition) => {
          const reason = blockedBy?.(transition) ?? null;

          return (
            <span className="cms-action" key={`${transition.id}-${transition.from}`}>
              <button
                type="button"
                className={
                  transition.tone === 'primary'
                    ? 'cms-primary'
                    : transition.tone === 'danger'
                      ? 'cms-danger'
                      : 'cms-secondary'
                }
                disabled={reason !== null || submitting}
                onClick={() => {
                  setNote('');
                  setPending(transition);
                }}
              >
                {transition.label}
              </button>
              {reason !== null && <span className="cms-blocked">{reason}</span>}
            </span>
          );
        })}
      </div>

      <Confirm
        open={pending !== null}
        title={pending?.title ?? ''}
        confirmLabel={pending?.label ?? ''}
        tone={pending?.tone === 'danger' ? 'danger' : 'normal'}
        busy={submitting}
        disabled={blocked}
        onCancel={close}
        onConfirm={() => void confirm()}
        body={
          <>
            <ul className="cms-consequences">
              {(pending?.consequences ?? []).map((line) => (
                <li key={line}>{line}</li>
              ))}
            </ul>

            {wantsNote && (
              <label className="cms-field">
                <span>Lý do trả lại</span>
                <textarea
                  rows={3}
                  value={note}
                  onChange={(event) => setNote(event.target.value)}
                  placeholder="Nêu rõ chỗ cần sửa — càng cụ thể càng ít vòng duyệt."
                />
              </label>
            )}

            <p className="cms-audit-line">
              Nhật ký sẽ ghi: <code>{pending?.audit}</code> · {operator.email}
            </p>
          </>
        }
      />
    </>
  );
}
