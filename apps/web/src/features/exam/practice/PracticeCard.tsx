import type { CSSProperties } from 'react';
import { useI18n } from '../../../i18n/index.js';
import { formatDuration, SKILLS } from '../skills.js';
import { variantLabel, type PracticeItem } from './practiceCatalogue.js';

/**
 * One thing a learner can start, as a card.
 *
 * <b>Four questions, in the order a reader asks them.</b> What is this · what
 * is in it · how long will it take · can I start now. The skill badge answers
 * the first before the title is read; the metadata row answers the middle two;
 * the button answers the last and is the only saturated thing on the card.
 *
 * <b>The metadata is small and grey on purpose.</b> On the reference layout
 * this row competes with the title — same weight, same size, a badge each.
 * Everything here except the title and the button is one step quieter, because
 * a grid of these is scanned by title.
 *
 * <b>Nothing on the card is invented.</b> Title, variant, question count and
 * duration are the four fields `ExamCatalogueItem` carries. There is no band
 * level, no difficulty and no topic because the catalogue has none of them —
 * a "Band 6.5 · Medium" line would have to come from somewhere, and the only
 * available somewhere is us. → `practiceCatalogue.ts` § FACET_SEAM
 *
 * <b>The whole card is not a link.</b> Starting a sitting is a POST that opens
 * a server-side session with a deadline on it; that is a button, and it must
 * not be something a stray click on a card body can fire.
 */
export function PracticeCard({
  item,
  busy,
  onStart,
}: {
  item: PracticeItem;
  busy: boolean;
  /**
   * `timing` is the learner's own choice of clock, and it is asked here rather
   * than inferred from the mode bar above the grid.
   *
   * <b>Luyện đề / Thi thử is not the same pair as Full Test / Single Skill.</b>
   * `E-11` confirms the first two, `E-20` confirms the second two, and nothing
   * says how they compose — `B-13`. Reading "Luyện từng kỹ năng" as "luyện đề"
   * would answer that question by fiat, from a control that was built to mean
   * something else, and would leave no way to sit a timed single-skill paper.
   * Two buttons state the choice instead of guessing it. → `X-4`
   */
  onStart: (item: PracticeItem, timing: 'deadline' | 'open') => void;
}) {
  const { t } = useI18n();
  const skill = item.module === null ? null : SKILLS[item.module];
  const full = skill === null;

  return (
    <li
      className={`prac-card${full ? ' is-full' : ''}`}
      // A single-skill card carries its skill's own ink and tint as CSS
      // variables, so the top accent bar and hover glow can be pure CSS. A
      // full-test card gets neither — `.is-full` paints a fixed four-colour
      // bar instead, since no single skill applies.
      style={
        full ? undefined : ({ '--sk-ink': skill.ink, '--sk-tint': skill.tint } as CSSProperties)
      }
    >
      <h3 className="prac-title">{item.title}</h3>

      <div className="prac-card-tags">
        {full && (
          <span className="prac-chip is-full-badge">
            <span aria-hidden="true">◈</span>
            Full Test
          </span>
        )}

        <span className="prac-variant">{variantLabel(item.variant)}</span>
      </div>

      {item.description !== null && <p className="prac-desc">{item.description}</p>}

      {full && item.parts.length > 0 && (
        <ul className="prac-parts">
          {item.parts.map((part) => (
            <li key={part.module}>
              <span
                className="prac-part-key"
                style={{ background: SKILLS[part.module].tint, color: SKILLS[part.module].ink }}
                aria-hidden="true"
              >
                {SKILLS[part.module].name.slice(0, 1)}
              </span>
              <span className="prac-part-min">
                <span className="num">{part.minutes}</span> phút
              </span>
              <span className="sr-only">
                {SKILLS[part.module].name} {part.minutes} phút
              </span>
            </li>
          ))}
        </ul>
      )}

      {/* Metadata in D-5 order: parts/questions · duration · scoring source · resume state */}
      <ul className="prac-meta">
        <li className="prac-meta-item">
          {item.parts.length > 0 ? (
            <>
              <span className="prac-meta-value num">{item.parts.length}</span>&nbsp;phần ·{' '}
              <span className="prac-meta-value num">{item.questionCount}</span>&nbsp;câu
            </>
          ) : (
            <>
              <span className="prac-meta-value num">{item.questionCount}</span>&nbsp;câu
            </>
          )}
        </li>
        <li className="prac-meta-item">
          {durationParts(item.durationSeconds).map((part, at) =>
            part.digits ? (
              <span className="prac-meta-value num" key={at}>
                {part.text}
              </span>
            ) : (
              <span key={at}>{part.text}</span>
            ),
          )}
        </li>
        <li className="prac-meta-item prac-meta-marking">
          {full
            ? 'Theo đáp án / AI · tham khảo'
            : item.module === 'writing' || item.module === 'speaking'
              ? 'AI · tham khảo'
              : 'Theo đáp án'}
        </li>
      </ul>

      <div className="prac-starts">
        {/* Luyện đề is the primary button on single-skill cards */}
        {!full && (
          <button
            type="button"
            className="prac-start is-practice"
            disabled={busy}
            onClick={() => onStart(item, 'open')}
            aria-label={`${t('practice.startPractice')} ${skill.name} — ${item.title} · ${t('practice.startPracticeHint')}`}
          >
            {busy ? t('exam.starting') : t('practice.startPractice')}
            <span aria-hidden="true">→</span>
          </button>
        )}

        {/* Full Test cards have only Thi thử */}
        <button
          type="button"
          className={`prac-start is-mock${full ? ' is-full-primary' : ''}`}
          disabled={busy}
          onClick={() => onStart(item, 'deadline')}
          aria-label={
            full
              ? `${t('exam.startFull')} — ${item.title}`
              : `${t('exam.start')} ${skill.name} — ${item.title}`
          }
        >
          {busy ? t('exam.starting') : full ? 'Bắt đầu Thi thử' : t('exam.start')}
          <span aria-hidden="true">→</span>
        </button>
      </div>
    </li>
  );
}

/**
 * `"2 giờ 45 phút"` split into numerals and words.
 *
 * The tabular face is for digits: it is what keeps a column of durations
 * aligned. Applied to the whole string it sets "giờ" and "phút" in a monospace
 * face too, which is what shipped.
 */
function durationParts(seconds: number): { text: string; digits: boolean }[] {
  return formatDuration(seconds)
    .split(/(\d+)/)
    .filter((piece) => piece !== '')
    .map((piece) => ({ text: piece, digits: /^\d+$/.test(piece) }));
}
