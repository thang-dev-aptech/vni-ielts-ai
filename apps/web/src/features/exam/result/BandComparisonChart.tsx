import { useI18n } from '../../../i18n/index.js';
import { BarsGlyph } from './ResultIcons.js';

/** One column: a band and the share of learners who scored it. */
export interface BandShare {
  band: string;
  /** 0–100. */
  percent: number;
}

/**
 * "So sánh kết quả" — where this band sits against everyone else's.
 *
 * <b>The chart is real; the data source is not built yet, and it is not
 * invented here.</b> The reference draws six columns with exact percentages and
 * a "you are here" marker. Producing those needs a cohort distribution, and no
 * endpoint in this product returns one — `SessionResultsView` carries a band
 * and nothing about anybody else's. DESIGN.md anti-pattern #12 forbids putting
 * a fabricated percentage on screen, so the panel keeps its footprint and says
 * plainly that the comparison is not available.
 *
 * <b>Wired, not stubbed.</b> `distribution` is the seam: the day an endpoint
 * returns one, this renders it with no change here. That is the `G-11` shape —
 * a configured seam with a null implementation, never an invented default.
 */
export function BandComparisonChart({
  distribution,
  myBand,
  skillName,
}: {
  /** Null while no cohort data exists. Never a made-up shape. */
  distribution: BandShare[] | null;
  /** The learner's own band, as a string, so the marker can find its column. */
  myBand: string | null;
  skillName: string | null;
}) {
  const { t } = useI18n();

  return (
    <section className="exs-panel">
      <div className="exs-panel-head">
        <span className="exs-panel-head-icon" aria-hidden="true">
          <BarsGlyph size={18} />
        </span>
        <div>
          <h2>{t('exam.compareTitle')}</h2>
          <p>{t('exam.compareLead')}</p>
        </div>
      </div>

      {distribution === null || distribution.length === 0 ? (
        <p className="exs-empty">{t('exam.compareUnavailable')}</p>
      ) : (
        <>
          <div
            className="exs-chart"
            style={{ ['--exs-chart-cols' as string]: String(distribution.length) }}
          >
            {distribution.map((column) => {
              const here = myBand !== null && column.band === myBand;
              return (
                <div className="exs-chart-col" data-here={here} key={column.band}>
                  {here && <span className="exs-chart-here">{t('exam.compareYouAreHere')}</span>}
                  <span className="exs-chart-pct">{Math.round(column.percent)}%</span>
                  <span
                    className="exs-chart-bar"
                    style={{ height: `${Math.max(4, column.percent * 2.6)}px` }}
                  />
                </div>
              );
            })}
          </div>

          <div
            className="exs-chart-axis"
            style={{ ['--exs-chart-cols' as string]: String(distribution.length) }}
          >
            {distribution.map((column) => (
              <span key={column.band}>{column.band}</span>
            ))}
          </div>

          <p className="exs-chart-caption">
            {skillName === null
              ? t('exam.compareAxis')
              : t('exam.compareAxisSkill', { skill: skillName })}
          </p>
        </>
      )}
    </section>
  );
}
