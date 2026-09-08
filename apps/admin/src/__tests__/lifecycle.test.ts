import { describe, expect, it } from 'vitest';
import { EXAM_STATES, STATE, TRANSITIONS, allows, transitionsFor, type ExamState } from '../lib/lifecycle.js';
import { PERMISSION, ROLE_PRESETS } from '../lib/permissions.js';

/**
 * The lifecycle, pinned down against the real server model.
 *
 * <b>Rewritten for the five-state collapse.</b> This suite used to pin a
 * six-state table with `withdraw`, `unapprove` and `resume` transitions and a
 * client-side ownership dimension (`own` vs `any`). None of that exists on the
 * server: `ExamVersionStatus` has five values, `ReturnToDraft` goes straight
 * to `Draft` with no fifth "returned" state, and only five HTTP endpoints
 * exist — submit-for-review, approve, return-to-draft, publish, unpublish.
 * `withdraw`/`unapprove`/`resume` had no endpoint behind them and are gone;
 * the reviewer ≠ author rule that used to live in an `ownership` field is
 * enforced entirely server-side (`ExamVersion.Approve`) and surfaces as a 403
 * the client could not have pre-empted, so `allows()` checks permission alone.
 *
 * The red-when-removed target for the state collapse: every one of the five
 * status strings the server can actually send (`"draft"`, `"inreview"`,
 * `"approved"`, `"published"`, `"unpublished"`) resolves to a face, and no
 * code path here assumes a `"returned"` status the server never sends — see
 * "the table itself" below, which iterates `EXAM_STATES` rather than a
 * hand-typed list, so a stray reintroduction of `returned` would need to be
 * added to `EXAM_STATES` to type-check, and this test would then have to be
 * told about it explicitly rather than picking it up for free.
 */

function actor(permissions: string[]) {
  const held = new Set(permissions);
  return { can: (p: string) => held.has(p) };
}

const idsFor = (state: ExamState, permissions: string[]) =>
  transitionsFor(state, actor(permissions))
    .map((t) => t.id)
    .sort();

describe('the five real states', () => {
  it('is exactly draft, inreview, approved, published, unpublished — no more, no fewer', () => {
    expect(EXAM_STATES).toEqual(['draft', 'inreview', 'approved', 'published', 'unpublished']);
  });

  it('never carries a client-side "returned" state', () => {
    expect(EXAM_STATES).not.toContain('returned');
    expect(EXAM_STATES).not.toContain('in-review'); // the old, hyphenated spelling
  });

  it('gives every one of the five a face', () => {
    for (const state of EXAM_STATES) expect(STATE[state].label.length).toBeGreaterThan(0);
  });
});

describe('an operator holding exam.submit', () => {
  it('may submit a draft, and nothing else', () => {
    expect(idsFor('draft', ['exam.submit'])).toEqual(['submit']);
  });

  it('gets nothing on a version already in review', () => {
    expect(idsFor('inreview', ['exam.submit'])).toEqual([]);
  });
});

describe('an operator holding exam.review', () => {
  it('may approve or return a submission', () => {
    expect(idsFor('inreview', ['exam.review'])).toEqual(['approve', 'return']);
  });

  it('cannot publish an approved exam — that authority is separate', () => {
    expect(idsFor('approved', ['exam.review'])).toEqual([]);
  });
});

describe('an operator holding exam.publish and exam.unpublish', () => {
  it('publishes an approved exam', () => {
    expect(idsFor('approved', ['exam.publish'])).toEqual(['publish']);
  });

  it('unpublishes a live one', () => {
    expect(idsFor('published', ['exam.unpublish'])).toEqual(['unpublish']);
  });

  it('republishes one that was taken down', () => {
    expect(idsFor('unpublished', ['exam.publish'])).toEqual(['publish']);
  });

  it('cannot review — publishing does not imply reviewing', () => {
    expect(idsFor('inreview', ['exam.publish'])).toEqual([]);
  });
});

describe('an operator holding nothing', () => {
  it('gets no transition from any state', () => {
    for (const state of EXAM_STATES) expect(idsFor(state, [])).toEqual([]);
  });
});

describe('the table itself', () => {
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

  it('requires a note only on return — the one transition the server refuses without a reason', () => {
    for (const transition of TRANSITIONS) {
      expect(transition.requiresNote === true, transition.id).toBe(transition.id === 'return');
    }
  });

  it("names the server's own AuditAction enum value, not an invented dotted string", () => {
    const known = new Set([
      'ExamSubmittedForReview',
      'ExamApproved',
      'ExamReturnedToDraft',
      'ExamPublished',
      'ExamUnpublished',
    ]);
    for (const transition of TRANSITIONS) expect(known.has(transition.audit)).toBe(true);
  });
});

describe('ownership is no longer a client-side gate', () => {
  it('allows() takes no isOwner argument — the reviewer ≠ author rule is enforced server-side', () => {
    const submit = TRANSITIONS.find((t) => t.id === 'submit');
    expect(submit).toBeDefined();
    // A caller holding the permission is let through regardless of who they
    // are — the server has no ownership data to check against on this route
    // either (SubmitForReviewEndpoint checks only PermissionKeys.ExamSubmit).
    expect(allows(submit!, actor(['exam.submit']))).toBe(true);
  });
});

/* ── Confirmed decisions, as executable statements ────────────────────────
 *
 * Unrelated to the five-state collapse above — these pin ROLE_PRESETS, the
 * dev-only "Xem như" preview role bundles in `permissions.ts`, which this
 * task did not change. Carried over unmodified from the six-state suite.
 */

describe('the decisions taken on 2026-08-24', () => {
  const preset = (id: string) => {
    const found = ROLE_PRESETS.find((r) => r.id === id);
    if (found === undefined) throw new Error(`no preset ${id}`);
    return found.permissions;
  };

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
