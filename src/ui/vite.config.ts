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
  worker: {
    // A classic worker, not a module one. WebView2 refuses to start a module worker whose script
    // comes from the custom `diffhacker://` scheme — the constructor succeeds and the worker dies
    // immediately after, which reaches the screen as "the diagram could not be arranged" with
    // nothing in any log to say why. An IIFE bundle has no import statements to resolve and starts
    // everywhere; ELK is inlined into it, which is what a layout worker wants anyway.
    format: 'iife',
  },
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
    onUnhandledError: isJsdomMissingEventView,
  },
});

/**
 * The one unhandled error the graph tests provoke that nothing in this repository can prevent.
 *
 * A mouse event in a browser always carries a `view`. `@testing-library/user-event` builds its
 * events by defining `view` as a **non-configurable** own property from an init object that has
 * none, so it is fixed at `null` before the event is ever dispatched and no amount of patching in
 * `setup.ts` can put it back — the property cannot be redefined.
 *
 * React Flow pans the canvas with d3-zoom, whose mousedown handler calls `dragDisable(event.view)`
 * and immediately reads `view.document`. So every click that reaches the pane — collapsing a
 * cluster, expanding one, choosing a search hit — threw a TypeError inside a DOM listener,
 * asynchronously and outside any assertion. The tests passed and the run still reported errors
 * nobody could act on, attributed to whichever test happened to be running at the time.
 *
 * Matched exactly rather than by turning unhandled errors off: anything that is not this one still
 * fails the run.
 */
function isJsdomMissingEventView(error: unknown): boolean {
  const stack = error instanceof Error ? (error.stack ?? '') : '';

  return (
    error instanceof TypeError &&
    error.message.includes("reading 'document'") &&
    stack.includes('d3-drag')
  );
}
