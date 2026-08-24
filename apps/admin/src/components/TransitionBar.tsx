import { useState } from 'react';
import { Confirm } from '../chrome/Confirm.js';
import { transitionsFor, type Transition } from '../lib/lifecycle.js';
import { useOperator } from '../lib/operator.js';
import { ownedByMe, type PreviewVersion } from '../lib/previewStore.js';

/**
 * Everything this operator may do to this version, and the step before doing it.
 *
 * <b>The buttons are derived, never listed.</b> Which ones appear comes from
 * the transition table filtered by state, permission and ownership — so a
 * screen cannot offer an action the model does not allow, and adding a
 * transition later puts a button on every screen that shows this bar without
 * anyone remembering to.
 *
 * <b>Returning an exam demands a reason.</b> `requiresNote` holds the confirm
 * button shut until there is one. An author who receives "trả lại" with no
 * note has to guess what to change, and guessing is how a second round of
 * review gets spent on the wrong thing.
 */
export function TransitionBar({
  version,
  onApply,
  blockedBy,
}: {
  version: PreviewVersion;
  onApply: (transition: Transition, note: string) => void;
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

  const open = transitionsFor(version.state, {
    can: operator.can,
    isOwner: ownedByMe(version),
  });

  if (open.length === 0) return null;

  const wantsNote = pending !== null && (pending.requiresNote === true || pending.id === 'approve');
  const blocked = pending?.requiresNote === true && note.trim() === '';

  function close() {
    setPending(null);
    setNote('');
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
                disabled={reason !== null}
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
        busy={false}
        disabled={blocked}
        onCancel={close}
        onConfirm={() => {
          if (pending === null) return;
          onApply(pending, note);
          close();
        }}
        body={
          <>
            <ul className="cms-consequences">
              {(pending?.consequences ?? []).map((line) => (
                <li key={line}>{line}</li>
              ))}
            </ul>

            {wantsNote && (
              <label className="cms-field">
                <span>
                  Ghi chú cho người soạn
                  {pending?.requiresNote === true ? '' : ' (tuỳ chọn)'}
                </span>
                <textarea
                  rows={3}
                  value={note}
                  onChange={(event) => setNote(event.target.value)}
                  placeholder={
                    pending?.requiresNote === true
                      ? 'Nêu rõ chỗ cần sửa — càng cụ thể càng ít vòng duyệt.'
                      : 'Ghi chú thêm, nếu có.'
                  }
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
