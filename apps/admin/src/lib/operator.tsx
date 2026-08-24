import { createContext, useContext, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useAdminAuth } from './AdminAuth.js';
import { ROLE_PRESETS, type RolePreset } from './permissions.js';

/**
 * Who is operating the CMS — and, in development, who we are pretending to be.
 *
 * <b>Why a preview mode exists.</b> Phase 1 proposes two roles the server does
 * not seed yet (`exam-author`, `academic-lead`) and eleven permission keys it
 * does not know. Without a way to stand in one of those roles, the screens
 * built for them are unreachable and the proposal can only be reviewed by
 * reading a table. Picking a role here swaps the permission set the client
 * reasons with, so the whole CMS can be walked from each role's point of view.
 *
 * <b>Three properties keep this honest.</b>
 *
 * It is gated on `import.meta.env.DEV` at the point it renders, which Vite
 * folds at build time — so it is not in a production bundle, not merely hidden
 * there. Verified by grepping the built asset. It changes nothing on the
 * server: every request
 * still carries the real token and the server still answers on the real
 * permissions, which is why the screens behind it read from the preview store
 * rather than from the API. And it is announced on screen rather than
 * inferred, because a permission model you cannot see through is worse than no
 * preview at all.
 */

interface ViewAsState {
  /** Null means "use the real permissions from the server". */
  preset: RolePreset | null;
  setPreset: (preset: RolePreset | null) => void;
  available: boolean;
}

const ViewAsContext = createContext<ViewAsState | null>(null);

export function ViewAsProvider({ children }: { children: ReactNode }) {
  const [preset, setPreset] = useState<RolePreset | null>(null);
  const available = import.meta.env.DEV;

  const value = useMemo<ViewAsState>(
    () => ({ preset: available ? preset : null, setPreset, available }),
    [available, preset],
  );

  return <ViewAsContext.Provider value={value}>{children}</ViewAsContext.Provider>;
}

export function useViewAs(): ViewAsState {
  const value = useContext(ViewAsContext);
  if (value === null) throw new Error('useViewAs must be used inside ViewAsProvider');
  return value;
}

export interface Operator {
  can: (permission: string) => boolean;
  isOperator: boolean;
  name: string;
  email: string;
  /** True while a preview role is standing in for the real permission set. */
  previewing: boolean;
  previewLabel: string | null;
}

/**
 * The single answer to "what may this person do", for every screen.
 *
 * Screens ask this rather than `useAdminAuth().can` so that preview mode is
 * one substitution in one place instead of a condition in each of them.
 */
export function useOperator(): Operator {
  const { user, can, isOperator } = useAdminAuth();
  const { preset } = useViewAs();

  return useMemo<Operator>(() => {
    if (preset === null) {
      return {
        can,
        isOperator,
        name: user?.displayName ?? '',
        email: user?.email ?? '',
        previewing: false,
        previewLabel: null,
      };
    }

    const granted = new Set(preset.permissions);

    return {
      can: (permission) => granted.has(permission),
      isOperator: true,
      name: user?.displayName ?? '',
      email: user?.email ?? '',
      previewing: true,
      previewLabel: preset.label,
    };
  }, [can, isOperator, preset, user]);
}

export { ROLE_PRESETS };
