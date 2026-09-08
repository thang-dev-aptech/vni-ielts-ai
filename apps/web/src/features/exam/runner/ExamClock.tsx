import { useI18n } from '../../../i18n/index.js';
import { formatClock } from '../examApi.js';
import { ClockGlyph } from './ExamIcons.js';

/**
 * The countdown, as a pill.
 *
 * <b>Product law L1 outranks the reference here, and happens to agree with
 * it.</b> The clock never turns red, never blinks, never moves. Red belongs to
 * something that has broken; time passing has not broken anything. The three
 * escalation levels are told apart by <em>size, border weight and a written
 * note</em> — three channels, so the set survives the greyscale test — and the
 * hue only ever shifts from green to `--warn`, never to `--bad`.
 *
 * | level | when | drawn as |
 * |---|---|---|
 * | 1 | more than 5 minutes | 42px pill, hairline green border |
 * | 2 | under 5 minutes | `--warn` border, note "còn dưới 5 phút" |
 * | 3 | under 1 minute | 2px border, `--warn-soft` ground, larger figures |
 *
 * <b>`aria-live="off"` on the read-out, deliberately.</b> A per-second
 * announcement makes the page unusable with a screen reader; the two threshold
 * crossings are announced once each through the `sr-only` alert instead.
 *
 * The figures are `--mono` with `tabular-nums`: without it every digit change
 * shifts the pill's width by a pixel or two, and a clock that twitches once a
 * second is exactly the interface panicking that L1 forbids.
 */
export function ExamClock({ remaining }: { remaining: number | null }) {
  const { t } = useI18n();

  const left = remaining;
  const level = left === null ? 1 : left === 0 ? 4 : left < 60 ? 3 : left < 300 ? 2 : 1;

  const crossing =
    left === 0
      ? t('exam.expired')
      : left === 60
        ? t('exam.underOneMinute')
        : left === 300
          ? t('exam.underFiveMinutes')
          : null;

  return (
    <>
      <span className="sr-only" role="alert">
        {crossing}
      </span>
      <span className={`exr-clock level-${level}`} role="timer" aria-live="off">
        <ClockGlyph />
        <span className="num">{left === null ? '--:--' : formatClock(left)}</span>
        {level > 1 && level < 4 && (
          <span className="exr-clock-note">
            {level >= 3 ? t('exam.underOneMinute') : t('exam.underFiveMinutes')}
          </span>
        )}
      </span>
    </>
  );
}
