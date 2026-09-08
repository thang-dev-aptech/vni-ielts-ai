import { Fragment, useCallback, useEffect, useRef, useState } from 'react';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { listRoles, type AdminRole } from '../lib/adminApi.js';
import { PERMISSION } from '../lib/permissions.js';

/**
 * Screen 7.1 — roles, as a permission matrix.
 *
 * <b>The columns come from the server, not from `permissions.ts`.</b>
 * `PermissionKeys.All` is the one list of what actually exists; restating it
 * in TypeScript would mean a key added to the domain simply does not get a
 * column, and nobody notices until someone tries to grant it. `permissions.ts`
 * still has a role in this screen — as a *display* lookup, keyed by the
 * server's own strings, for a Vietnamese label and a group. A key with no
 * entry there renders under its raw string rather than being dropped: an
 * unlabelled-but-real permission is visible and auditable, which is strictly
 * better than an invisible one. Conversely, a label in `permissions.ts` for a
 * key the server does not currently grant (several — `.own`/`.any` scope
 * variants, `media.*`, `exam.preview` — are still proposals) simply never
 * gets a column, because the loop below iterates the server's list, not
 * `permissions.ts`'s.
 *
 * <b>The matrix is read-only, and one row of it explains why that matters.</b>
 * A role deliberately holds `exam.submit` and not `exam.review`: composing
 * content and reviewing it are different acts by different people. A grid of
 * live checkboxes is the easiest possible way to erase that distinction with
 * one stray click — so granting waits for the audit log that would record who
 * did it.
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

  /**
   * Grouped by `permissions.ts`'s own group label when the key has one, and
   * by its dotted prefix otherwise — so a permission the domain grew without
   * a matching label entry still lands in a sensible section instead of one
   * "khác" bucket nobody expects to check.
   */
  const groups = permissions.reduce<Record<string, string[]>>((acc, key) => {
    const group = PERMISSION[key]?.group ?? key.split('.')[0] ?? 'khác';
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
                      {/*
                        The label is a display fallback of a fallback — most
                        permissions have one, some genuinely do not, and both
                        are correct states for this cell to be in.
                      */}
                      {PERMISSION[key] !== undefined && (
                        <span className="cms-sub">{PERMISSION[key].label}</span>
                      )}
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
