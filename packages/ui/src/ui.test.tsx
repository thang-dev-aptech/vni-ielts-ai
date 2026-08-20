import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Alert } from './Alert.js';
import { Button } from './Button.js';
import { Field } from './Field.js';
import { EmptyState, ErrorState, Spinner } from './States.js';

describe('Button', () => {
  it('cannot be pressed while busy', async () => {
    // A form that keeps accepting presses while a request is in flight is how
    // people end up submitting twice.
    const onClick = vi.fn();
    render(
      <Button busy busyLabel="Đang gửi…" onClick={onClick}>
        Gửi
      </Button>,
    );

    const button = screen.getByRole('button');
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute('aria-busy', 'true');
    expect(button).toHaveTextContent('Đang gửi…');

    await userEvent.click(button);
    expect(onClick).not.toHaveBeenCalled();
  });

  it('announces busy state to assistive technology, not just visually', () => {
    render(<Button busy>Gửi</Button>);
    expect(screen.getByRole('button')).toHaveAttribute('aria-busy', 'true');
  });
});

describe('Field', () => {
  it('binds a real label rather than relying on a placeholder', () => {
    // A placeholder vanishes on first keystroke, leaving anyone who paused
    // without a clue what the field was for.
    render(<Field label="Mật khẩu" type="password" />);
    expect(screen.getByLabelText('Mật khẩu')).toBeInTheDocument();
  });

  it('links its error message so a screen reader announces it', () => {
    render(<Field label="Email" error="Địa chỉ không hợp lệ" />);

    const input = screen.getByLabelText('Email');
    expect(input).toHaveAttribute('aria-invalid', 'true');

    const describedBy = input.getAttribute('aria-describedby');
    expect(describedBy).toBeTruthy();
    expect(document.getElementById(describedBy!)).toHaveTextContent('Địa chỉ không hợp lệ');
  });

  it('is not marked invalid when there is no error', () => {
    render(<Field label="Email" />);
    expect(screen.getByLabelText('Email')).toHaveAttribute('aria-invalid', 'false');
  });

  it('exposes a hint without marking the field invalid', () => {
    render(<Field label="Mật khẩu" hint="Ít nhất 12 ký tự" />);

    const input = screen.getByLabelText('Mật khẩu');
    expect(input).toHaveAttribute('aria-invalid', 'false');
    expect(document.getElementById(input.getAttribute('aria-describedby')!)).toHaveTextContent(
      'Ít nhất 12 ký tự',
    );
  });
});

describe('Alert', () => {
  it('interrupts for an error', () => {
    // Assertive is right for a failure and rude for a confirmation.
    render(<Alert tone="error">Hỏng rồi</Alert>);
    expect(screen.getByRole('alert')).toHaveTextContent('Hỏng rồi');
  });

  it('does not interrupt for a success', () => {
    render(<Alert tone="success">Xong</Alert>);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Xong');
  });
});

describe('state components', () => {
  it('Spinner announces politely with a real label', () => {
    // A spinner with no label tells a screen-reader user nothing at all.
    render(<Spinner label="Đang tải…" />);
    const status = screen.getByRole('status');
    expect(status).toHaveTextContent('Đang tải…');
    expect(status).toHaveAttribute('aria-live', 'polite');
  });

  it('EmptyState explains itself instead of showing a blank panel', () => {
    render(<EmptyState title="Chưa có đề" description="Phần thi chưa được xây dựng." />);
    expect(screen.getByText('Chưa có đề')).toBeInTheDocument();
    expect(screen.getByText('Phần thi chưa được xây dựng.')).toBeInTheDocument();
  });

  it('EmptyState offers no action when none is given', () => {
    // An empty state with a dead button is worse than one that admits nothing
    // is there.
    render(<EmptyState title="Trống" description="Chưa có gì." />);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('ErrorState is announced as an alert', () => {
    render(<ErrorState title="Lỗi" description="Không tải được." />);
    expect(screen.getByRole('alert')).toHaveTextContent('Lỗi');
  });
});
