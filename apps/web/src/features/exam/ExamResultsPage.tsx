import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { getResults, type SessionResultsView } from './examApi.js';
import { SKILLS, SKILL_ORDER } from './skills.js';
import '../../styles/dashboard.css';
import '../../styles/exam.css';

/**
 * What the sitting produced.
 *
 * <b>A section with no band is absent, not zero.</b> Reading and Listening are
 * marked the moment they are submitted; Writing and Speaking wait on an
 * evaluation that does not exist yet, so they appear as `—` with the reason
 * beside them. Product law L3: a band that was never awarded is never drawn as
 * a number, and never as a skeleton that reads like one arriving.
 *
 * <b>Overall needs all four.</b> The server returns null until then, and this
 * screen does not average what it has — a mean over two sections is not an
 * overall band, it is a made-up one.
 */
export function ExamResultsPage() {
  const { sessionId = '' } = useParams();
  const { accessToken } = useAuth();
  const { t } = useI18n();

  const [results, setResults] = useState<SessionResultsView | null>(null);
  const [failed, setFailed] = useState(false);
  const alive = useRef(true);

  /*
   * Set true on the way IN, not just false on the way out.
   *
   * StrictMode double-invokes a mount effect: run, clean up, run again. The
   * cleanup flips this to false and the second run never flipped it back, so
   * every later `setState` guarded by it was skipped and the screen sat on
   * "Đang tải…" forever — against an API that had already answered 200.
   */
  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  useEffect(() => {
    if (accessToken === null) return;

    void (async () => {
      try {
        const loaded = await getResults(accessToken, sessionId);
        if (alive.current) setResults(loaded);
      } catch {
        if (alive.current) setFailed(true);
      }
    })();
  }, [accessToken, sessionId]);

  if (failed) {
    return (
      <div className="dash">
        <main className="dash-main">
          <div className="dash-empty">
            <h3>{t('exam.gone')}</h3>
            <p>{t('exam.goneBody')}</p>
          </div>
        </main>
      </div>
    );
  }

  if (results === null) {
    return (
      <div className="dash">
        <main className="dash-main">
          <p className="dash-notice">{t('exam.loading')}</p>
        </main>
      </div>
    );
  }

  const marked = new Map(results.sections.map((s) => [s.module, s]));

  // Single-skill sittings only ever have one section; showing the other three
  // as "chưa chấm" would imply an exam the learner never sat.
  const shown = results.mode === 'full' ? SKILL_ORDER : SKILL_ORDER.filter((m) => marked.has(m));

  return (
    <div className="dash">
      <main className="dash-main" id="dash-top">
        <header className="dash-head">
          <p className="dash-eyebrow">{t('exam.resultsEyebrow')}</p>
          <h1 className="dash-greeting">{results.examTitle}</h1>
          <p className="dash-lead">
            {results.status === 'expired' ? t('exam.resultsExpired') : t('exam.resultsLead')}
          </p>
        </header>

        <section className="result-overall">
          <span className="result-overall-label">{t('exam.overall')}</span>
          <span className="result-overall-value num">
            {results.overallBand === null ? '—' : results.overallBand.toFixed(1)}
          </span>
          {results.overallBand === null && (
            <span className="result-overall-note">{t('exam.overallPending')}</span>
          )}
        </section>

        <ul className="result-list">
          {shown.map((moduleId) => {
            const skill = SKILLS[moduleId];
            const Icon = skill.icon;
            const section = marked.get(moduleId);

            return (
              <li className="result-row" key={moduleId}>
                <span
                  className="result-icon"
                  style={{ background: skill.tint, color: skill.ink }}
                  aria-hidden="true"
                >
                  <Icon size={20} />
                </span>

                <span className="result-text">
                  <strong>{skill.name}</strong>
                  <span>
                    {section
                      ? t('exam.rawOf', { raw: section.rawScore, max: section.maxScore })
                      : t('exam.notMarked')}
                  </span>
                </span>

                {/* The tag says where the band came from. Answer-key and AI
                    bands must never look interchangeable. → product law L4 */}
                <span
                  className={
                    moduleId === 'writing' || moduleId === 'speaking'
                      ? 'dash-tag dash-tag-ai'
                      : 'dash-tag'
                  }
                >
                  {moduleId === 'writing' || moduleId === 'speaking'
                    ? t('dash.scoring.ai')
                    : t('dash.scoring.key')}
                </span>

                <span className="result-band num">{section ? section.band.toFixed(1) : '—'}</span>
              </li>
            );
          })}
        </ul>

        <p className="dash-notice">{t('exam.aiPending')}</p>

        <p>
          <Link className="dash-link" to={Paths.practice}>
            {t('exam.backToPractice')}
          </Link>
        </p>
      </main>
    </div>
  );
}
