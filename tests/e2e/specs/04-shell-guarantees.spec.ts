import { expect, test } from '../src/fixtures.ts';
import { en } from '../src/strings.ts';

/**
 * Properties of the shell itself, rather than behaviour a reviewer would notice.
 *
 * These used to live in the renderer as a `--self-test` mode that shipped inside the production
 * bundle. They belong here instead: the same checks, driven from outside, with no test code left
 * in the product.
 */

/**
 * §0.2.13 makes the WebView a pure renderer with no network access, and the
 * Content-Security-Policy is what enforces that. A policy present in the markup but not applied
 * by the engine looks identical to one that works, so this asks the engine directly.
 */
test('the Content-Security-Policy is enforced, not merely declared', async ({ diffhacker }) => {
  const app = await diffhacker.launch();

  const outcome = await app.page.evaluate(async () => {
    const violations: string[] = [];
    const onViolation = (event: SecurityPolicyViolationEvent) => {
      violations.push(event.violatedDirective);
    };

    document.addEventListener('securitypolicyviolation', onViolation);

    try {
      await new Promise<void>((resolve) => {
        const script = document.createElement('script');

        // A reserved TLD that can never resolve. Success is defined as the engine reporting a
        // violation, not as the load failing — otherwise a merely-unreachable host would pass
        // this check by accident.
        script.src = 'https://blocked.invalid/csp-probe.js';
        script.addEventListener('load', () => resolve());
        script.addEventListener('error', () => resolve());
        document.head.appendChild(script);

        // The violation fires synchronously on append in every engine we target; the timer only
        // stops a silent engine from hanging the test.
        setTimeout(resolve, 2_000);
      });
    } finally {
      document.removeEventListener('securitypolicyviolation', onViolation);
    }

    return violations;
  });

  expect(
    outcome.some((directive) => directive.startsWith('script-src')),
    `no script-src violation was reported, so the policy may not be enforced. Saw: ${outcome.join(', ') || 'nothing'}`,
  ).toBe(true);
});

/**
 * The renderer is served in-process through the `diffhacker://` scheme handler. Serving it over
 * HTTP is permanently out of scope, and it is what makes the policy above meaningful — so the
 * page's own origin is worth asserting rather than assuming.
 */
test('the renderer is served in-process, not over HTTP', async ({ diffhacker }) => {
  const app = await diffhacker.launch();

  expect(app.page.url()).toBe('diffhacker://app/index.html');

  const origin = await app.page.evaluate(() => globalThis.location.protocol);
  expect(origin).toBe('diffhacker:');
});

/**
 * The handshake. A host and a renderer built from different contract generations would produce
 * a plausible-looking window that fails in confusing ways later, so the renderer checks on
 * start-up and says so. This asserts the happy half — that a single build agrees with itself.
 */
test('the host and the renderer agree on the contract version', async ({ diffhacker }) => {
  const app = await diffhacker.launch();

  // The host panel renders only while the connection is unestablished or mismatched. Its
  // absence after launch is the handshake having succeeded.
  await expect(app.page.getByText(en.host.heading)).toHaveCount(0);
  await expect(app.page.getByText('contract', { exact: false })).toHaveCount(0);
});

/**
 * The colour scheme is a property of the shell, so it is checked here.
 *
 * Tailwind's dark variant is wired to a `dark` class on `<html>` and the document's own
 * `color-scheme` decides what the WebView paints behind the page — scrollbars, form controls, the
 * ground under a slow first paint. Both have to move together, and only a real engine can say
 * whether they did.
 *
 * The choice is remembered in `localStorage`, which lives in the WebView's own profile rather than
 * in the `--data-dir` this suite throws away. So this test puts it back to following the system
 * before it finishes: nothing of ours is left in the developer's browser profile.
 */
test('the colour scheme can be chosen, and reaches the document', async ({ diffhacker }) => {
  const app = await diffhacker.launch();

  const scheme = () =>
    app.page.evaluate(() => ({
      dark: document.documentElement.classList.contains('dark'),
      colorScheme: document.documentElement.style.colorScheme,
    }));

  try {
    await app.page.getByRole('button', { name: en.theme.dark }).click();
    expect(await scheme()).toEqual({ dark: true, colorScheme: 'dark' });

    await app.shot('the dark colour scheme');

    await app.page.getByRole('button', { name: en.theme.light }).click();
    expect(await scheme()).toEqual({ dark: false, colorScheme: 'light' });

    await app.shot('the light colour scheme');
  } finally {
    await app.page.getByRole('button', { name: en.theme.system }).click();
  }
});
