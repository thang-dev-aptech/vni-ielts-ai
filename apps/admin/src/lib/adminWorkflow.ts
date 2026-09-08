/**
 * Preview-mode guard for CMS mutations.
 *
 * Full workflow wiring lives on the feature branch; INT only needs the error
 * type so package-candidate review can refuse writes while a role is previewed
 * (preview never changes the real access token — see `operator.tsx`).
 */
export class PreviewModeError extends Error {}
