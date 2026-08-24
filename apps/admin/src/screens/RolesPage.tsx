import { Fragment, useCallback, useEffect, useRef, useState } from 'react';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { listRoles, type AdminRole } from '../lib/adminApi.js';

/**
 * Screen 7.1 — roles, as a permission matrix.
 *
 * <b>The columns come from the server.</b> `PermissionKeys.All` is the one
 * list; restating it in TypeScript would mean a key added to the domain simply
 * does not get a column, and nobody notices until someone tries to grant it.
 *
 * <b>The matrix is read-only, and one row of it explains why that matters.</b>
 * `content-editor` deliberately holds `exam.create` and not `exam.publish`:
 * bringing content in and putting it in front of learners are different acts
 * by different people. A grid of live checkboxes is the easiest possible way
 * to erase that distinction with one stray click — so granting waits for the
 * audit log that would record who did it.
 */
export function RolesPage() {
  const { accessToken } = useAdminAuth();

  const [roles, setRoles] = useState<AdminRole[] | null>(null);
  const [permissions, setPermissions] = useState<string[]>([]);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const result = await listRoles(accessToken);
      if (!alive.current) return;
      setRoles(result.roles);
      setPermissions(result.permissions);
    } catch {
      if (alive.current) setRoles([]);
    }
  }, [accessToken]);

  useEffect(() => void load(), [load]);

  if (roles === null) return <p className="cms-muted">Đang tải…</p>;

  /** Grouped by the prefix, which is how the specification lists them. */
  const groups = permissions.reduce<Record<string, string[]>>((acc, key) => {
    const group = key.split('.')[0] ?? 'khác';
    (acc[group] ??= []).push(key);
    return acc;
  }, {});

  return (
    <>
      <header className="cms-head">
        <h1>Vai và quyền</h1>
        <p>
          {roles.length} vai, {permissions.length} quyền. Chỉ xem — cấp quyền chờ nhật ký audit.
        </p>
      </header>

      <div className="cms-table-wrap">
        <table className="cms-table cms-matrix">
          <thead>
            <tr>
              <th>Quyền</th>
              {roles.map((role) => (
                <th key={role.roleId} className="cms-matrix-role">
                  {role.name}
                  {role.isSystem && <span className="cms-sub">hệ thống</span>}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {Object.entries(groups).map(([group, keys]) => (
              // `Fragment` with a key, not `<>`. The shorthand takes no key,
              // so React saw one unkeyed child per group and said so.
              <Fragment key={group}>
                <tr className="cms-matrix-group">
                  <th colSpan={roles.length + 1}>{group}</th>
                </tr>
                {keys.map((key) => (
                  <tr key={key}>
                    <td>
                      <code>{key}</code>
                    </td>
                    {roles.map((role) => (
                      <td key={role.roleId} className="cms-matrix-cell">
                        {/*
                          A tick and a dash, not a coloured cell. The matrix has
                          to be readable in a screenshot pasted into a ticket,
                          and half of those are greyscale.
                        */}
                        {role.permissions.includes(key) ? (
                          <span className="cms-yes" title="Có">
                            ✓
                          </span>
                        ) : (
                          <span className="cms-no" title="Không">
                            —
                          </span>
                        )}
                      </td>
                    ))}
                  </tr>
                ))}
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
