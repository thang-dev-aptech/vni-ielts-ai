import { useCallback, useEffect, useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { useI18n } from '../../i18n/index.js';
import type { StringKey } from '../../i18n/strings.js';
import { useAlive } from '../../lib/useAlive.js';
import { Paths } from '../../routes/paths.js';
import { useAuth } from '../auth/AuthContext.js';
import type { ExamModule } from '../exam/examApi.js';
import { SKILLS, SKILL_ORDER } from '../exam/skills.js';
import {
  getCoaching,
  getCoachingAdvice,
  setGoal,
  TARGET_BANDS,
  type Coaching,
  type CoachingSkill,
  type SkillState,
} from './learningApi.js';
import '../../styles/learning.css';

/**
 * "Where am I against my goal, and what should I do next."
 *
 * <b>Two kinds of advice, drawn as two kinds.</b> The gap per skill and the
 * order to work on them are arithmetic the server did; they are shown as
 * facts. The paragraph under them is a model's phrasing of those same facts
 * and wears the dashed "AI · tham khảo" tag every other AI sentence in the
 * product wears — product law L4, the same rule as the results screen.
 *
 * <b>No goal is a state, not an error.</b> The panel opens on the goal picker
 * and shows the four skills with whatever bands exist; the advice arrives the
 * moment a target is saved.
 */
export function GoalCoachingPanel({ compact = false }: { compact?: boolean }) {
  const { accessToken } = useAuth();
  const { t } = useI18n();
  const alive = useAlive();
  const selectId = useId();

  const [coaching, setCoaching] = useState<Coaching | null>(null);
  const [failed, setFailed] = useState(false);
  const [pending, setPending] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    let facts: Coaching;
    try {
      facts = await getCoaching(accessToken);
      // A reply without the four skills is not coaching — a proxy page, an
      // old server. Treated as unreachable rather than rendered.
      if (!Array.isArray(facts?.skills) || !facts.ai) throw new Error('malformed coaching');
      if (alive.current) {
        setCoaching(facts);
        setFailed(false);
      }
    } catch {
      if (alive.current) setFailed(true);
      return;
    }

    // The facts are on screen. The model's phrasing arrives when it arrives;
    // a failure here leaves the numbers exactly as they were.
    if (facts.ai.status !== 'pending') return;
    try {
      const advised = await getCoachingAdvice(accessToken);
      if (alive.current && Array.isArray(advised?.skills) && advised.ai) setCoaching(advised);
    } catch {
      if (alive.current)
        setCoaching((c) => (c ? { ...c, ai: { ...c.ai, status: 'unavailable' } } : c));
    }
  }, [accessToken]);

  useEffect(() => void load(), [load]);

  const save = async (target: number) => {
    if (accessToken === null) return;
    setSaving(true);
    try {
      await setGoal(accessToken, target, coaching?.goal?.examDate ?? null, crypto.randomUUID());
      await load();
      if (alive.current) setPending(null);
    } catch {
      if (alive.current) setFailed(true);
    } finally {
      if (alive.current) setSaving(false);
    }
  };

  if (failed && coaching === null) {
    return (
      <section className="coach" aria-labelledby="coach-title">
        <h2 id="coach-title" className="coach-title">
          {t('coach.title')}
        </h2>
        <p className="coach-note">{t('common.notConnected')}</p>
        <button type="button" className="dash-go" onClick={() => void load()}>
          {t('common.retry')}
        </button>
      </section>
    );
  }

  if (coaching === null) {
    return (
      <section className="coach" aria-labelledby="coach-title">
        <h2 id="coach-title" className="coach-title">
          {t('coach.title')}
        </h2>
        <p className="coach-note">{t('exam.loading')}</p>
      </section>
    );
  }

  const target = coaching.goal?.targetBand ?? null;
  const shownTarget = pending ?? target;
  const focus = coaching.focus[0] ?? null;
  const focusSkill = focus ? (coaching.skills.find((s) => s.module === focus) ?? null) : null;

  return (
    <section className={`coach${compact ? ' is-compact' : ''}`} aria-labelledby="coach-title">
      <header className="coach-head">
        <div>
          <p className="coach-eyebrow">{t('coach.eyebrow')}</p>
          <h2 id="coach-title" className="coach-title">
            {t('coach.title')}
          </h2>
        </div>
        <form
          className="coach-goal"
          onSubmit={(e) => {
            e.preventDefault();
            if (shownTarget !== null) void save(shownTarget);
          }}
        >
          <label className="coach-goal-label" htmlFor={selectId}>
            {t('coach.targetLabel')}
          </label>
          <select
            id={selectId}
            className="coach-goal-select num"
            value={shownTarget ?? ''}
            onChange={(e) => setPending(Number(e.target.value))}
            disabled={saving}
          >
            <option value="" disabled>
              {t('coach.targetPick')}
            </option>
            {TARGET_BANDS.map((band) => (
              <option key={band} value={band}>
                {band.toFixed(1)}
              </option>
            ))}
          </select>
          {pending !== null && pending !== target && (
            <button type="submit" className="dash-go" disabled={saving}>
              {saving ? t('coach.saving') : t('coach.save')}
            </button>
          )}
        </form>
      </header>

      {target === null ? (
        <p className="coach-note">{t('coach.noGoal')}</p>
      ) : focusSkill && focusSkill.currentBand !== null ? (
        <p className="coach-focus" role="status">
          {t('coach.focus', {
            skill: SKILLS[focusSkill.module].name,
            band: focusSkill.currentBand.toFixed(1),
            target: target.toFixed(1),
          })}
        </p>
      ) : coaching.skills.every((s) => s.currentBand === null) ? (
        <p className="coach-note">{t('coach.noData')}</p>
      ) : (
        <p className="coach-focus is-met" role="status">
          {t('coach.allMet', { target: target.toFixed(1) })}
        </p>
      )}

      <ul className="coach-skills">
        {SKILL_ORDER.map((moduleId) => {
          const skill = coaching.skills.find((s) => s.module === moduleId);
          if (!skill) return null;
          return <SkillRow key={moduleId} skill={skill} target={target} />;
        })}
      </ul>

      {target !== null && focusSkill && (
        <div className="coach-tips">
          <h3 className="coach-subhead">{t('coach.nextSteps')}</h3>
          <ul className="coach-tip-list">
            {coaching.focus.slice(0, 3).map((moduleId) => {
              const skill = coaching.skills.find((s) => s.module === moduleId);
              if (!skill) return null;
              return (
                <li key={moduleId} className="coach-tip">
                  <strong>{SKILLS[moduleId].name}</strong> {t(tipKey(moduleId, skill.state))}
                </li>
              );
            })}
          </ul>
        </div>
      )}

      {coaching.ai.status === 'ready' && coaching.ai.summary && (
        <div className="coach-ai">
          <div className="coach-ai-head">
            <span className="dash-tag dash-tag-ai">{t('dash.scoring.ai')}</span>
            {coaching.ai.model && <span className="coach-ai-model">{coaching.ai.model}</span>}
          </div>
          <p className="coach-ai-summary">{coaching.ai.summary}</p>
          {coaching.ai.tips.length > 0 && (
            <ul className="coach-tip-list">
              {coaching.ai.tips.map((tip, i) => (
                <li key={`${tip.module}-${i}`} className="coach-tip">
                  <strong>{SKILLS[tip.module]?.name ?? tip.module}</strong> {tip.text}
                </li>
              ))}
            </ul>
          )}
          <p className="coach-ai-note">{t('coach.aiNote')}</p>
        </div>
      )}

      {coaching.ai.status === 'pending' && (
        <p className="coach-note" role="status">
          {t('coach.aiPending')}
        </p>
      )}

      {coaching.ai.status === 'unavailable' && target !== null && (
        <p className="coach-note">{t('coach.aiUnavailable')}</p>
      )}
    </section>
  );
}

function SkillRow({ skill, target }: { skill: CoachingSkill; target: number | null }) {
  const { t } = useI18n();
  const meta = SKILLS[skill.module];
  const Icon = meta.icon;
  const band = skill.currentBand;
  const stateKey = STATE_KEY[skill.state];

  // The rail runs 4.0 → 9.0, the only range a target can be in. A band is
  // placed on it as a point; the target as a tick. Nothing is a percentage
  // of a goal — 4.0 against 6.5 is not "61%", it is two and a half bands.
  const pct = (value: number) => Math.max(0, Math.min(100, ((value - 4) / 5) * 100));

  return (
    <li className={`coach-skill is-${skill.state}`}>
      <span
        className="coach-skill-icon"
        style={{ background: meta.tint, color: meta.ink }}
        aria-hidden="true"
      >
        <Icon size={16} />
      </span>
      <span className="coach-skill-name">{meta.name}</span>
      <span className="coach-skill-rail" aria-hidden="true">
        {band !== null && <span className="coach-skill-dot" style={{ left: `${pct(band)}%` }} />}
        {target !== null && (
          <span className="coach-skill-tick" style={{ left: `${pct(target)}%` }} />
        )}
      </span>
      <span
        className="coach-skill-band num"
        aria-label={band === null ? t('goal.scoreNone') : undefined}
      >
        {band === null ? '—' : band.toFixed(1)}
      </span>
      <span className={`coach-skill-state is-${skill.state}`}>
        {t(stateKey)}
        {skill.gap !== null && skill.gap > 0 && (
          <span className="num"> −{skill.gap.toFixed(1)}</span>
        )}
      </span>
      {skill.sessionId && (
        <Link className="coach-skill-link" to={Paths.examResults(skill.sessionId)}>
          {t('coach.viewSitting')}
        </Link>
      )}
    </li>
  );
}

const STATE_KEY: Record<SkillState, StringKey> = {
  none: 'coach.state.none',
  met: 'coach.state.met',
  close: 'coach.state.close',
  behind: 'coach.state.behind',
};

function tipKey(module: ExamModule, state: SkillState): StringKey {
  const level = state === 'behind' ? 'behind' : 'close';
  return `coach.tip.${module}.${level}` as StringKey;
}
