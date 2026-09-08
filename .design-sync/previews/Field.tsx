import { Button, Field } from '@vni/ui';

/** A real `<label>` bound by id — never a placeholder standing in for one. */
export function Default() {
  return (
    <div style={{ maxWidth: 360 }}>
      <Field label="Email" type="email" defaultValue="mai.nguyen@example.com" />
    </div>
  );
}

/** `hint` is announced through `aria-describedby` without marking the field invalid. */
export function WithHint() {
  return (
    <div style={{ maxWidth: 360 }}>
      <Field label="Mật khẩu" type="password" hint="Ít nhất 12 ký tự" />
    </div>
  );
}

/**
 * `error` sets `aria-invalid` and links the message, so it is announced rather
 * than only shown in red — state has to survive the greyscale test.
 */
export function WithError() {
  return (
    <div style={{ maxWidth: 360 }}>
      <Field label="Email" type="email" defaultValue="mai.nguyen@" error="Địa chỉ không hợp lệ" />
    </div>
  );
}

/** The shape it actually ships in: a sign-in form. */
export function SignInForm() {
  return (
    <div style={{ maxWidth: 360 }}>
      <Field label="Email" type="email" placeholder="ban@example.com" />
      <Field label="Mật khẩu" type="password" hint="Ít nhất 12 ký tự" />
      <Button variant="primary" fullWidth>
        Đăng nhập
      </Button>
    </div>
  );
}

/** Unavailable — for example while a verification round-trip is in flight. */
export function Disabled() {
  return (
    <div style={{ maxWidth: 360 }}>
      <Field label="Mã xác minh 6 số" defaultValue="482913" disabled />
    </div>
  );
}
