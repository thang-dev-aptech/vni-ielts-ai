import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  // 5174, not a random port: the API's development CORS allowlist names 5173
  // and 5174 explicitly, and a random port means every reload is a CORS
  // failure that looks like an auth bug.
  server: { port: 5174, strictPort: true },
});
