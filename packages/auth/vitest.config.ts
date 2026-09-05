import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // jsdom, not node: the coordinator's whole job is browser coordination —
    // `localStorage`, `BroadcastChannel`, `navigator.locks`. Testing it against
    // a Node global object would test a different module.
    environment: 'jsdom',
    globals: true,
  },
});
