import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import path from 'node:path';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  // Assets are served from diffhacker://app/, so every URL the bundle emits must be relative
  // to the document rather than rooted at '/'.
  base: './',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    // The host serves these in-process; a sourcemap is only ever read by DevTools locally.
    sourcemap: true,
    // One window, one document: no need to split for network latency that does not exist.
    chunkSizeWarningLimit: 4096,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
