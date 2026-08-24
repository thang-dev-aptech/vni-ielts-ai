import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { advance, type PreviewVersion } from '../lib/previewStore.js';
import { TRANSITIONS, type Transition } from '../lib/lifecycle.js';

/**
 * `useOperator` is mocked rather than provided.
 *
 * The real one reads a session and a preview role through two contexts, and
 * standing both up here would make this a test of the providers. What is under
 * test is narrower and more valuable: given a permission set, does the bar
 * offer the right buttons, and does returning an exam refuse to proceed
 * without a reason.
 */
const permissions = new Set<string>();

vi.mock('../lib/operator.js', () => ({
  useOperator: () => ({
    can: (p: string) => permissions.has(p),
    isOperator: true,
    name: 'Người duyệt',
    email: 'lead@vni.test',
    previewing: true,
    previewLabel: 'Trưởng chuyên môn',
  }),
}));

const { TransitionBar } = await import('../components/TransitionBar.js');

const find = (id: string, from: string): Transition => {
  const found = TRANSITIONS.find((t) => t.id === id && t.from === from);
  if (found === undefined) throw new Error(`no transition ${id} from ${from}`);
  return found;
};

function version(overrides: Partial<PreviewVersion> = {}): PreviewVersion {
  return {
    versionId: 'v1',
    definitionId: 'd1',
    title: 'Đề mẫu',
    variant: 'academic',
    versionNumber: 1,
    state: 'in-review',
    modules: [{ module: 'reading', questionCount: 40 }],
    author: { self: false, name: 'Trần B' },
    createdAt: '2026-08-01T00:00:00.000Z',
    submittedAt: '2026-08-10T00:00:00.000Z',
    reviewedAt: null,
    reviewedByName: null,
    publishedAt: null,
    notes: [],
    topic: 'Môi trường',
    difficultyAuthored: '6.5',
    assets: [],
    ...overrides,
  };
}

describe('advance', () => {
  it('stamps the submission time and moves the state', () => {
    const next = advance(
      version({ state: 'draft', submittedAt: null }),
      find('submit', 'draft'),
      'Nguyễn A',
      '',
    );
    expect(next.state).toBe('in-review');
    expect(next.submittedAt).not.toBeNull();
  });

  it('clears the submission time when the author withdraws', () => {
    const next = advance(version(), find('withdraw', 'in-review'), 'Nguyễn A', '');
    expect(next.state).toBe('draft');
    expect(next.submittedAt).toBeNull();
  });

  it('records who reviewed, and keeps the note with the version', () => {
    const next = advance(
      version(),
      find('return', 'in-review'),
      'Người duyệt',
      'Câu 12 sai đáp án.',
    );
    expect(next.state).toBe('returned');
    expect(next.reviewedByName).toBe('Người duyệt');
    expect(next.notes).toHaveLength(1);
    expect(next.notes[0]?.body).toBe('Câu 12 sai đáp án.');
  });

  it('drops the reviewer again when an approval is taken back', () => {
    const approved = advance(version(), find('approve', 'in-review'), 'Người duyệt', '');
    const back = advance(approved, find('unapprove', 'approved'), 'Người duyệt', '');
    expect(back.state).toBe('in-review');
    expect(back.reviewedByName).toBeNull();
  });

  it('stamps publication without touching the earlier signatures', () => {
    const approved = advance(version(), find('approve', 'in-review'), 'Người duyệt', '');
    const live = advance(approved, find('publish', 'approved'), 'Quản trị viên', '');
    expect(live.state).toBe('published');
    expect(live.publishedAt).not.toBeNull();
    expect(live.reviewedByName).toBe('Người duyệt');
    expect(live.submittedAt).toBe(approved.submittedAt);
  });

  it('does not invent a note out of whitespace', () => {
    const next = advance(version(), find('approve', 'in-review'), 'Người duyệt', '   ');
    expect(next.notes).toHaveLength(0);
  });
});

describe('TransitionBar', () => {
  beforeEach(() => {
    permissions.clear();
  });

  it('offers nothing when the operator holds no relevant permission', () => {
    const { container } = render(<TransitionBar version={version()} onApply={() => {}} />);
    expect(container.querySelector('.cms-actions')).toBeNull();
  });

  it('offers approve and return to a reviewer', () => {
    permissions.add('exam.review');
    render(<TransitionBar version={version()} onApply={() => {}} />);
    expect(screen.getByRole('button', { name: 'Duyệt' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Trả lại' })).toBeInTheDocument();
  });

  it('refuses to return an exam until a reason is given', () => {
    permissions.add('exam.review');
    const onApply = vi.fn();
    render(<TransitionBar version={version()} onApply={onApply} />);

    fireEvent.click(screen.getByRole('button', { name: 'Trả lại' }));

    const confirm = screen.getByRole('dialog').querySelector('.cms-danger, .cms-primary');
    expect(confirm).toBeDisabled();

    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: 'Passage 3 dài quá mức General Training.' },
    });

    expect(confirm).toBeEnabled();
    fireEvent.click(confirm!);

    expect(onApply).toHaveBeenCalledTimes(1);
    expect(onApply.mock.calls[0]?.[0].id).toBe('return');
    expect(onApply.mock.calls[0]?.[1]).toContain('Passage 3');
  });

  it('names the audit entry the action will write', () => {
    permissions.add('exam.review');
    render(<TransitionBar version={version()} onApply={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'Duyệt' }));
    expect(screen.getByText('exam.approved')).toBeInTheDocument();
  });

  it('holds a blocked transition shut and says why, rather than hiding it', () => {
    permissions.add('exam.review');
    const onApply = vi.fn();
    render(
      <TransitionBar
        version={version()}
        onApply={onApply}
        blockedBy={(t) => (t.id === 'approve' ? 'Thiếu 1 tệp media.' : null)}
      />,
    );

    const approve = screen.getByRole('button', { name: 'Duyệt' });
    expect(approve).toBeDisabled();
    expect(screen.getByText('Thiếu 1 tệp media.')).toBeInTheDocument();

    // The other transition on the same bar is untouched: a missing recording is
    // a reason not to approve, not a reason to trap the exam in the queue.
    expect(screen.getByRole('button', { name: 'Trả lại' })).toBeEnabled();

    fireEvent.click(approve);
    expect(onApply).not.toHaveBeenCalled();
  });

  it('states the consequence of publishing in terms of learners', () => {
    permissions.add('exam.publish');
    render(<TransitionBar version={version({ state: 'approved' })} onApply={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'Xuất bản' }));
    expect(screen.getByText(/Học viên sẽ thấy và làm được đề này/)).toBeInTheDocument();
  });
});
