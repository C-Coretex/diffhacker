import { defineConfig } from '@playwright/test';

/**
 * End-to-end configuration.
 *
 * These tests drive the real application: the real Photino window, the real .NET host, the real
 * git command line, real repositories on disk. Nothing is stubbed, which is the point — the unit
 * suites already prove each layer in isolation and cannot prove they are wired together.
 *
 * `workers: 1` is not a performance compromise. Each test launches a desktop window that takes
 * the foreground; two at once would fight over focus and over the debugging port.
 */
export default defineConfig({
  testDir: './specs',
  outputDir: './artifacts/test-results',
  globalSetup: './src/globalSetup.ts',

  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env.CI,

  /**
   * Generous per-test, tight per-assertion. A spec is a whole journey through several screens
   * and includes an app launch; a single assertion waiting more than a few seconds means
   * something is actually wrong, and failing fast keeps the feedback useful.
   */
  timeout: 120_000,
  expect: { timeout: 8_000 },

  reporter: [
    ['list'],
    ['html', { outputFolder: './artifacts/playwright-report', open: 'never' }],
  ],

  use: {
    actionTimeout: 8_000,
    navigationTimeout: 15_000,
    trace: 'retain-on-failure',
  },
});
