import { fireEvent, render, screen } from '@testing-library/react';
import type { ComponentProps } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { ImportReviewPanel, REVIEW_CHECKS } from '../components/ImportReviewPanel.js';

function renderPanel(overrides: Partial<ComponentProps<typeof ImportReviewPanel>> = {}) {
  const props: ComponentProps<typeof ImportReviewPanel> = {
    sourceText: 'raw question', packageJson: '{"title":"parsed"}',
    warnings: [], approved: false, canEdit: true, canReview: true, canPublish: false,
    onSave: vi.fn(), onResolve: vi.fn(), onApprove: vi.fn(), onPublish: vi.fn(),
    ...overrides,
  };
  render(<ImportReviewPanel {...props} />);
  return props;
}

describe('import review panel', () => {
  it('shows source and parsed content side by side and saves a manual edit', () => {
    const props = renderPanel();
    expect(screen.getByText('raw question')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Package JSON'), { target: { value: '{"title":"fixed"}' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lưu sửa đổi' }));
    expect(props.onSave).toHaveBeenCalledWith('{"title":"fixed"}');
  });

  it('blocks approval until every warning and checklist item is resolved', () => {
    const props = renderPanel({ warnings: [{ id: 'w1', message: 'Sai asset', resolved: false }] });
    expect(screen.getByRole('button', { name: 'Duyệt' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Đánh dấu đã xử lý' }));
    expect(props.onResolve).toHaveBeenCalledWith('w1');
  });

  it('requires all six evidence checks and keeps publish as a separate permission', () => {
    const props = renderPanel();
    for (const [, label] of REVIEW_CHECKS) fireEvent.click(screen.getByLabelText(label));
    fireEvent.click(screen.getByRole('button', { name: 'Duyệt' }));
    expect(props.onApprove).toHaveBeenCalledOnce();
    expect(screen.queryByRole('button', { name: 'Xuất bản' })).not.toBeInTheDocument();
  });

  it('publisher cannot publish an unapproved draft', () => {
    renderPanel({ canEdit: false, canReview: false, canPublish: true });
    expect(screen.getByRole('button', { name: 'Xuất bản' })).toBeDisabled();
  });
});
