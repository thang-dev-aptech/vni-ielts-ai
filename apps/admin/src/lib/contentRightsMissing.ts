import { createElement, Fragment, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { AdminPaths } from '../routes/paths.js';

/** Vietnamese publish refusal when the registry has no grant for the paper. */
export function contentRightsMissingMessage(): ReactNode {
  return createElement(
    Fragment,
    null,
    'Đề này chưa có quyền xuất bản tới học viên. ',
    createElement(Link, { to: AdminPaths.contentRights }, 'Đăng ký quyền nội dung'),
    '.',
  );
}
