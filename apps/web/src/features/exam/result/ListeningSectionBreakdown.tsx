import { useI18n } from '../../../i18n/index.js';
import type { ListeningSectionRow } from './resultModel.js';
import { TargetRingGlyph } from './ResultIcons.js';

/**
 * "Kết quả theo section" — one card per Listening part, Listening only.
 *
 * <b>Every figure on a card is the join `resultModel.listeningSectionsFrom`
 * already did: the part's own questions against the answer key.</b> Nothing
 * here assumes ten questions a part or splits the module's total evenly
 * across four cards — a part that came back with six questions draws "6"
 * questions, not a guess.
 *
 * <b>Empty when the join produced nothing, not four empty cards.</b> That
 * happens when this sitting's `content` has not arrived yet (still in
 * progress) or is genuinely absent — either way, four cards reading "0/0"
 * would look measured when nothing was.
 */
export function ListeningSectionBreakdown({ rows }: { rows: ListeningSectionRow[] }) {
  const { t } = useI18n();

  return (
    <section className="exs-panel">
      <div className="exs-panel-head">
        <span className="exs-panel-head-icon" aria-hidden="true">
          <TargetRingGlyph size={18} />
        </span>
        <div>
          <h2>{t('exam.sectionBreakdownTitle')}</h2>
          <p>{t('exam.sectionBreakdownLead')}</p>
        </div>
      </div>

      {rows.length === 0 ? (
        <p className="exs-empty">{t('exam.sectionBreakdownEmpty')}</p>
      ) : (
        <ul className="exs-section-cards">
          {rows.map((row) => {
            const percent = row.total === 0 ? 0 : (row.correct / row.total) * 100;

            return (
              <li className="exs-section-card" key={row.order}>
                <span className="exs-section-card-title">{t('exam.sectionN', { number: row.order })}</span>
                <span className="exs-section-card-range">
                  {row.firstQuestionNumber === null || row.lastQuestionNumber === null
                    ? '—'
                    : t('exam.sectionQuestionsRange', {
                        first: row.firstQuestionNumber,
                        last: row.lastQuestionNumber,
                      })}
                </span>
                <span className="exs-section-card-score">
                  {t('exam.sectionScoreOf', { correct: row.correct, total: row.total })}
                </span>
                <span className="exs-bar-track" aria-hidden="true">
                  <span className="exs-bar-fill" style={{ width: `${percent}%` }} />
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
