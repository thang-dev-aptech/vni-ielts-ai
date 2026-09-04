import { request } from '../../lib/api.js';
import type { ExamModule } from '../exam/examApi.js';

/**
 * `/api/v1/me/goal`, `/me/coaching`, `/me/activity` — the learner's goal, the
 * coaching computed against it, and the days they turned up.
 *
 * <b>Every number here was computed by the server.</b> The client draws gaps
 * and streaks; it never derives them, so the dashboard and the profile cannot
 * disagree about which skill is weakest or how long the streak is.
 */

export interface LearnerGoal {
  targetBand: number;
  examDate: string | null;
  updatedAt: string;
}

/** `none` · `met` · `close` · `behind` — see `GoalGap.StateOf` on the server. */
export type SkillState = 'none' | 'met' | 'close' | 'behind';

export interface CoachingSkill {
  module: ExamModule;
  currentBand: number | null;
  gap: number | null;
  state: SkillState;
  sessionId: string | null;
  measuredAt: string | null;
}

export interface CoachingTip {
  module: ExamModule;
  text: string;
}

/**
 * `ready` carries validated AI text; `unavailable` means the advisor refused
 * or failed and only the deterministic facts are shown; `no-goal` / `no-data`
 * mean there was nothing to advise on yet.
 */
export type CoachingAiStatus = 'ready' | 'unavailable' | 'no-goal' | 'no-data' | 'pending';

export interface Coaching {
  goal: LearnerGoal | null;
  skills: CoachingSkill[];
  /** Weakest first. Empty when nothing is under target. */
  focus: ExamModule[];
  ai: {
    status: CoachingAiStatus;
    summary: string | null;
    tips: CoachingTip[];
    model: string | null;
  };
}

export interface ActivityDay {
  date: string;
  count: number;
  kinds: string[];
}

export interface Activity {
  timeZone: string;
  today: string;
  days: ActivityDay[];
  currentStreak: number;
  longestStreak: number;
  activeToday: boolean;
  flame: boolean;
  flameThreshold: number;
}

export const getGoal = async (accessToken: string): Promise<LearnerGoal | null> => {
  const goal = await request<LearnerGoal | null | ''>('/api/v1/me/goal', { accessToken });
  return goal === '' || goal === undefined ? null : goal;
};

export const setGoal = (
  accessToken: string,
  targetBand: number,
  examDate: string | null,
  idempotencyKey: string,
) =>
  request<LearnerGoal>('/api/v1/me/goal', {
    method: 'PUT',
    accessToken,
    body: { targetBand, examDate },
    idempotencyKey,
  });

export const getCoaching = (accessToken: string) =>
  request<Coaching>('/api/v1/me/coaching', { accessToken });

/** The slow half: same shape, `ai` resolved. Ask after the facts are on screen. */
export const getCoachingAdvice = (accessToken: string) =>
  request<Coaching>('/api/v1/me/coaching/advice', { accessToken });

export const getActivity = (accessToken: string, days = 371) =>
  request<Activity>(`/api/v1/me/activity?days=${days}`, { accessToken });

/** 4.0 … 9.0 in half bands — the only targets the server accepts. */
export const TARGET_BANDS: number[] = Array.from({ length: 11 }, (_, i) => 4 + i * 0.5);
