import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  // 5173, not a random port. Three things in the running system name this port
  // by hand and cannot discover it: the API's development CORS allowlist
  // (`Cors:Origins`), the SSO client base URL, and `Sso:ClientCallbackPath`'s
  // redirect target. With `port: 0` a fresh clone starts on whatever port is
  // free, every API call fails CORS, and it presents as a broken sign-in
  // rather than as a misconfigured port.
  //
  // `strictPort` so a collision is a loud failure instead of a silent shift to
  // 5174 — which is the admin app's port, and would put two apps behind one
  // allowlist entry. Same fix, same reasoning, as `apps/admin`.
  server: { port: 5173, strictPort: true },
});
