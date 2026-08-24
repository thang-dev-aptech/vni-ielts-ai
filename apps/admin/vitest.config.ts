import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

/**
 * The CMS's tests.
 *
 * `jsdom` rather than `node` because the preview store reads `localStorage` on
 * first use — the store is browser state by design, and testing it against a
 * stub would test the stub.
 */
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test-setup.ts'],
  },
});
