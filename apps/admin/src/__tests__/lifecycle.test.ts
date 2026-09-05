import { describe, expect, it } from 'vitest';
import {
  EXAM_STATES,
  STATE,
  TRANSITIONS,
  allows,
  transitionsFor,
  type ExamState,
} from '../lib/lifecycle.js';
import { PERMISSION, ROLE_PRESETS } from '../lib/permissions.js';

/**
 * The lifecycle is the part of Phase 1 that is finished, so it is the part
 * that gets pinned down.
 *
 * These are not tests of the screens. They are tests of the two rules the
 * screens read from — which transition is open to whom, and which role holds
 * which authority — written so that a change to either has to be deliberate.
 * The three at the bottom are confirmed product decisions expressed as code:
 * if someone widens a role preset until an author can publish, the suite says
 * so rather than the CMS quietly shipping it.
 */

function actor(permissions: string[], isOwner: boolean) {
  const held = new Set(permissions);
  return { can: (p: string) => held.has(p), isOwner };
}

const preset = (id: string) => {
  const found = ROLE_PRESETS.find((r) => r.id === id);
  if (found === undefined) throw new Error(`no preset ${id}`);
  return found.permissions;
};

const idsFor = (state: ExamState, permissions: string[], isOwner: boolean) =>
  transitionsFor(state, actor(permissions, isOwner))
    .map((t) => t.id)
    .sort();

describe('an author acting on their own work', () => {
  it('may submit a draft, and nothing else', () => {
    expect(idsFor('draft', preset('exam-author'), true)).toEqual(['submit']);
  });

  it('may withdraw a submission but cannot review it', () => {
    expect(idsFor('in-review', preset('exam-author'), true)).toEqual(['withdraw']);
  });

  it('may reopen a returned exam', () => {
    expect(idsFor('returned', preset('exam-author'), true)).toEqual(['resume']);
  });

  it('cannot publish an approved exam — that authority is not theirs', () => {
    expect(idsFor('approved', preset('exam-author'), true)).toEqual([]);
  });
});

describe("an author acting on someone else's work", () => {
  it('gets no transitions at all on a draft', () => {
    expect(idsFor('draft', preset('exam-author'), false)).toEqual([]);
  });

  it('cannot withdraw a submission they did not make', () => {
    expect(idsFor('in-review', preset('exam-author'), false)).toEqual([]);
  });
});

describe('the academic lead', () => {
  it('may approve or return a submission, and may unstick it', () => {
    expect(idsFor('in-review', preset('academic-lead'), false)).toEqual([
      'approve',
      'return',
      'withdraw',
    ]);
  });

  it('may take an approval back', () => {
    expect(idsFor('approved', preset('academic-lead'), false)).toEqual(['unapprove']);
  });

  it('still cannot publish', () => {
    expect(
      transitionsFor('approved', actor(preset('academic-lead'), false)).map((t) => t.to),
    ).not.toContain('published');
  });
});

describe('the administrator', () => {
  it('publishes an approved exam', () => {
    expect(idsFor('approved', preset('admin'), false)).toEqual(['publish', 'unapprove']);
  });

  it('unpublishes a live one', () => {
    expect(idsFor('published', preset('admin'), false)).toEqual(['unpublish']);
  });

  it('republishes one that was taken down', () => {
    expect(idsFor('unpublished', preset('admin'), false)).toEqual(['publish']);
  });
});

describe('ownership scope', () => {
  it('closes an own-scoped transition to a non-owner', () => {
    const submit = TRANSITIONS.find((t) => t.id === 'submit');
    expect(submit).toBeDefined();
    expect(allows(submit!, actor(['exam.submit'], false))).toBe(false);
  });

  it('opens it again for a holder of exam.update.any', () => {
    const submit = TRANSITIONS.find((t) => t.id === 'submit');
    expect(allows(submit!, actor(['exam.submit', 'exam.update.any'], false))).toBe(true);
  });

  it('never lets a permission the actor lacks through, owner or not', () => {
    for (const transition of TRANSITIONS) {
      expect(allows(transition, actor([], true))).toBe(false);
    }
  });
});

describe('the table itself', () => {
  it('gives every state a face', () => {
    for (const state of EXAM_STATES) expect(STATE[state].label.length).toBeGreaterThan(0);
  });

  it('only names permissions the CMS knows how to label', () => {
    for (const transition of TRANSITIONS) {
      expect(PERMISSION[transition.permission], transition.permission).toBeDefined();
    }
  });

  it('states a consequence for every transition, and never a mechanism', () => {
    for (const transition of TRANSITIONS) {
      expect(transition.consequences.length).toBeGreaterThan(0);
      for (const line of transition.consequences) {
        expect(line).not.toMatch(/status|Published|Draft|enum/);
      }
    }
  });

  it('leaves no transition pointing at a state that does not exist', () => {
    for (const transition of TRANSITIONS) {
      expect(EXAM_STATES).toContain(transition.from);
      expect(EXAM_STATES).toContain(transition.to);
    }
  });
});

/* ── Confirmed decisions, as executable statements ───────────────────────── */

describe('the decisions taken on 2026-08-24', () => {
  it('C-16 · leaves publishing to the administrator alone', () => {
    for (const role of ROLE_PRESETS) {
      const publishes = role.permissions.includes('exam.publish');
      expect(publishes, role.id).toBe(role.id === 'admin');
    }
  });

  it('keeps learner essays and recordings away from everyone but the admin', () => {
    for (const role of ROLE_PRESETS) {
      const reads = role.permissions.includes('learner-content.read');
      expect(reads, role.id).toBe(role.id === 'admin');
    }
  });

  it('C-25 · seeds three operator roles, no more', () => {
    expect(ROLE_PRESETS.map((r) => r.id)).toEqual(['exam-author', 'academic-lead', 'admin']);
  });

  it('keeps both separations the trimming was not allowed to lose', () => {
    // Composing is not reviewing.
    expect(preset('exam-author')).not.toContain('exam.review');
    // Reviewing is not publishing.
    expect(preset('academic-lead')).not.toContain('exam.publish');
  });

  it('leaves the folded-in keys in the model, so a role can come back as data', () => {
    for (const key of [
      'article.write',
      'document.write',
      'dictation.write',
      'analytics.content.read',
    ]) {
      expect(PERMISSION[key], key).toBeDefined();
    }
  });

  it('names only permissions the CMS can label', () => {
    for (const role of ROLE_PRESETS) {
      for (const key of role.permissions) {
        expect(PERMISSION[key], `${role.id} → ${key}`).toBeDefined();
      }
    }
  });
});
