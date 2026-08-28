// Runtime configuration, read before the app bundle loads. See
// packages/auth/src/runtimeConfig.ts for the full contract and rationale.
//
// This checked-in copy is what `vite dev` and a plain static preview serve —
// every field empty, so `getRuntimeConfig()` falls through to its own
// build-time/localhost defaults. A container built from this app's
// Dockerfile overwrites this exact file at startup with real values read
// from its own environment; the source file never carries one.
window.__VNI_RUNTIME_CONFIG__ = {
  apiBaseUrl: '',
  environment: '',
  telemetryEndpoint: '',
};
