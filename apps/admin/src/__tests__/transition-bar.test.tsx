import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { Transition } from '../lib/lifecycle.js';

/**
 * `TransitionBar`, generic over a bare `ExamState` — the shape it took on
 * this session's cutover from the retired `previewStore` simulation.
 *
 * <b>Replaces the old `workflow.test.tsx`.</b> That file tested `advance()`,
 * the preview store's local state machine, and a version of this component
 * that took a whole `PreviewVersion` and read ownership off it with
 * `ownedByMe`. Both are gone: `advance()` had no server counterpart to test
 * against (its transitions — `withdraw`, `unapprove` — had no endpoint
 * either), and this component now takes `state: ExamState` plus an async
 * `onApply`, so any screen holding a real `AdminExam` row can use it.
 *
 * `useOperator` is mocked rather than provided, same reasoning as before: what
 * is under test is "given a permission set and a state, does the bar offer
 * the right buttons, and does it wait for `onApply` before closing" — not the
 * session or the HTTP transport underneath.
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

describe('TransitionBar', () => {
  beforeEach(() => {
    permissions.clear();
  });

  it('offers nothing when the operator holds no relevant permission', () => {
    const { container } = render(<TransitionBar state="inreview" onApply={async () => {}} />);
    expect(container.querySelector('.cms-actions')).toBeNull();
  });

  it('offers approve and return to a reviewer on an inreview version', () => {
    permissions.add('exam.review');
    render(<TransitionBar state="inreview" onApply={async () => {}} />);
    expect(screen.getByRole('button', { name: 'Duyệt' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Trả lại' })).toBeInTheDocument();
  });

  it('refuses to return an exam until a reason is given, and awaits onApply before closing', async () => {
    permissions.add('exam.review');
    let resolveApply: (() => void) | null = null;
    const onApply = vi.fn(
      (_transition: Transition, _note: string) =>
        new Promise<void>((resolve) => {
          resolveApply = resolve;
        }),
    );

    render(<TransitionBar state="inreview" onApply={onApply} />);
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

    // Dialog stays open — "Đang thực hiện…" — until onApply resolves.
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('Đang thực hiện…')).toBeInTheDocument();

    resolveApply!();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('names the audit entry the action will write', () => {
    permissions.add('exam.review');
    render(<TransitionBar state="inreview" onApply={async () => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'Duyệt' }));
    expect(screen.getByText('ExamApproved')).toBeInTheDocument();
  });

  it('holds a blocked transition shut and says why, rather than hiding it', () => {
    permissions.add('exam.review');
    const onApply = vi.fn(async () => {});
    render(
      <TransitionBar
        state="inreview"
        onApply={onApply}
        blockedBy={(t) => (t.id === 'approve' ? 'Thiếu 1 tệp media.' : null)}
      />,
    );

    const approve = screen.getByRole('button', { name: 'Duyệt' });
    expect(approve).toBeDisabled();
    expect(screen.getByText('Thiếu 1 tệp media.')).toBeInTheDocument();

    // The other transition on the same bar is untouched.
    expect(screen.getByRole('button', { name: 'Trả lại' })).toBeEnabled();

    fireEvent.click(approve);
    expect(onApply).not.toHaveBeenCalled();
  });

  it('states the consequence of publishing in terms of learners', () => {
    permissions.add('exam.publish');
    render(<TransitionBar state="approved" onApply={async () => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'Xuất bản' }));
    expect(screen.getByText(/Học viên sẽ thấy và làm được đề này/)).toBeInTheDocument();
  });

  it('offers submit on a draft to a holder of exam.submit, and nothing on inreview', () => {
    permissions.add('exam.submit');
    render(<TransitionBar state="draft" onApply={async () => {}} />);
    expect(screen.getByRole('button', { name: 'Nộp duyệt' })).toBeInTheDocument();
  });

  it('offers "Xuất bản lại" on an unpublished exam, distinct from a first publish', () => {
    permissions.add('exam.publish');
    render(<TransitionBar state="unpublished" onApply={async () => {}} />);
    expect(screen.getByRole('button', { name: 'Xuất bản lại' })).toBeInTheDocument();
  });
});
