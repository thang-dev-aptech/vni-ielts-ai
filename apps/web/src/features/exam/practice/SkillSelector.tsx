import { useEffect, useRef, type CSSProperties } from 'react';
import type { ExamModule } from '../examApi.js';
import { SKILLS, SKILL_ORDER } from '../skills.js';

/**
 * The four skills, as the page's primary control.
 *
 * <b>It used to be four labels that did nothing.</b> `pick-skills` rendered the
 * same four cards purely as decoration above the exam list — the most
 * prominent row on the page, and not one of them was pressable. Choosing a
 * skill is the first decision this page exists to support, so the row is now
 * the control that makes it.
 *
 * <b>Radio semantics, not four buttons.</b> The choice is exclusive and the
 * chosen one persists, which is what a radio group means; four buttons would
 * announce as four unrelated actions and leave a screen reader with no way to
 * hear which is current. Arrow keys move between them for free.
 *
 * <b>Colour is never alone.</b> The selected card gets its skill's tint, a
 * 2px border, and a visible check — so it survives greyscale and a reader who
 * cannot separate the hues. The four skill hues come from `skills.ts`, where
 * every ink-on-tint pair is measured.
 *
 * <b>The count is real or absent.</b> `count` is derived from the catalogue the
 * page actually loaded. A signed-out visitor has no catalogue — the API needs
 * a token — so they get no number rather than a plausible one.
 *
 * <b>`selected` can be null, and full-test mode is why.</b> The row stayed
 * tinted, ticked and `aria-checked` while the grid showed full tests — a
 * control announcing a current value that governed nothing on screen, and one
 * that auto-scrolled to prove it. With nothing selected the row is four equal
 * offers, and pressing one still means "luyện kỹ năng này", which is what
 * switches the mode back.
 *
 * <b>Below 640px the row scrolls sideways, and the chosen card is scrolled
 * into it.</b> Four cards squeezed into 350px gives each about 80px, which is
 * not enough for a label beside a 46px icon — the brief is explicit that they
 * must not shrink. But a row that scrolls has an off-screen half, and
 * `/practice?skill=speaking` landed with Speaking selected and Reading in
 * view: the control appeared to have ignored the address.
 */
export function SkillSelector({
  selected,
  counts,
  onSelect,
}: {
  /** Null in full-test mode, where no single skill is being chosen. */
  selected: ExamModule | null;
  /** Papers available per skill, or null when the catalogue is not loaded. */
  counts: Record<ExamModule, number> | null;
  onSelect: (skill: ExamModule) => void;
}) {
  const row = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (selected === null) return;

    const strip = row.current;
    const card = strip?.querySelector<HTMLElement>('.skill-pick.is-active');
    // Only when the row actually scrolls. At desktop widths it is a four-column
    // grid, and calling this there would scroll the *page* to the selector on
    // every skill change.
    if (!strip || !card || strip.scrollWidth <= strip.clientWidth) return;

    /*
     * An explicit `behavior: 'smooth'` in JavaScript beats the CSS override.
     *
     * `practice.css` sets `html { scroll-behavior: auto }` under
     * `prefers-reduced-motion`, and `useReveal` bails out of animating
     * entirely — but `scrollTo` with an explicit behaviour ignores both. This
     * was the one piece of motion on the page that did not honour the
     * setting, and it is a horizontal slide of the whole skill row.
     */
    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;

    strip.scrollTo({
      left: card.offsetLeft - strip.offsetLeft - 20,
      behavior: reduced ? 'auto' : 'smooth',
    });
  }, [selected]);

  return (
    <div
      ref={row}
      className="skill-picker"
      role="radiogroup"
      aria-label="Chọn kỹ năng muốn luyện"
      onKeyDown={(event) => {
        const step =
          event.key === 'ArrowRight' || event.key === 'ArrowDown'
            ? 1
            : event.key === 'ArrowLeft' || event.key === 'ArrowUp'
              ? -1
              : 0;
        const jump = event.key === 'Home' ? 'first' : event.key === 'End' ? 'last' : null;
        if (step === 0 && jump === null) return;
        event.preventDefault();

        /*
         * With nothing selected, either arrow lands on an end rather than
         * counting from a phantom position.
         *
         * `indexOf(null)` is -1, and -1 + 1 is 0, so ArrowRight already gave
         * the first card. ArrowLeft gave `(-1 - 1 + 4) % 4` = 2 — Writing,
         * the third card, for no reason a reader could infer. Full-test mode
         * is "nothing selected", so this is the state most people press an
         * arrow from.
         */
        const last = SKILL_ORDER.length - 1;
        const next =
          jump !== null
            ? SKILL_ORDER[jump === 'first' ? 0 : last]!
            : selected === null
              ? SKILL_ORDER[step === 1 ? 0 : last]!
              : SKILL_ORDER[
                  (SKILL_ORDER.indexOf(selected) + step + SKILL_ORDER.length) % SKILL_ORDER.length
                ]!;
        onSelect(next);
        // Focus follows selection in a radio group, which is what makes the
        // arrow keys usable rather than merely functional.
        document.getElementById(`skill-${next}`)?.focus();
      }}
    >
      {SKILL_ORDER.map((id) => {
        const skill = SKILLS[id];
        const Icon = skill.icon;
        const active = id === selected;
        const count = counts?.[id];
        // Exactly one card is reachable by Tab. With nothing selected that is
        // the first, so the group is never a four-stop detour.
        const stop = selected === null ? id === SKILL_ORDER[0] : active;

        return (
          <button
            key={id}
            id={`skill-${id}`}
            type="button"
            role="radio"
            aria-checked={active}
            tabIndex={stop ? 0 : -1}
            className={`skill-pick${active ? ' is-active' : ''}`}
            style={
              {
                '--sk-tint': skill.tint,
                '--sk-ink': skill.ink,
                ...(active ? { borderColor: skill.ink, background: skill.tint } : {}),
              } as CSSProperties
            }
            onClick={() => onSelect(id)}
          >
            <span
              className="skill-pick-icon"
              style={{ background: active ? '#fff' : skill.tint, color: skill.ink }}
              aria-hidden="true"
            >
              <Icon size={22} />
            </span>

            <span className="skill-pick-text">
              <strong>{skill.name}</strong>
              <span className="skill-pick-meta">
                {count === undefined
                  ? skill.marking
                  : count === 0
                    ? 'Chưa có đề'
                    : `${count} bài luyện`}
              </span>
            </span>

            {/* Painted by CSS now — a filled disc in `--sk-ink`, inherited from
                the button, with white text on it. An inline `color` here would
                beat the stylesheet regardless of specificity and silently
                undo the disc's white glyph. */}
            {active && (
              <span className="skill-pick-tick" aria-hidden="true">
                ✓
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
