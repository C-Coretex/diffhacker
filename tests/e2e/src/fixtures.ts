import { test as base } from '@playwright/test';
import { AppFactory, canDriveTheWindow } from './appHarness.ts';
import { RepoSet } from './gitFixture.ts';

interface Fixtures {
  /** Launches the real application. Every instance is stopped and cleaned up after the test. */
  diffhacker: AppFactory;

  /** Builds real git repositories. Every one is removed after the test. */
  repos: RepoSet;
}

export const test = base.extend<Fixtures>({
  diffhacker: async ({}, use, testInfo) => {
    const factory = new AppFactory(testInfo);
    await use(factory);
    await factory.disposeAll();
  },

  repos: async ({}, use) => {
    const set = new RepoSet();
    await use(set);
    set.disposeAll();
  },
});

/**
 * Driving the window needs the Chrome DevTools Protocol, which means WebView2, which means
 * Windows. On macOS and Linux the shell is WKWebView and WebKitGTK and there is no equivalent,
 * so the suite skips loudly rather than reporting a pass it did not earn.
 */
test.beforeEach(() => {
  test.skip(
    !canDriveTheWindow(),
    'End-to-end tests drive the WebView2 window over CDP, which only exists on Windows.',
  );
});

export { expect } from '@playwright/test';
