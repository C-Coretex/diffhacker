import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { en } from '../src/strings.ts';

/**
 * Provider configuration, the promises made about API keys, and whether any of it survives a
 * restart.
 *
 * The key assertions here are the ones §0.2.13 and Iteration 2's verification list actually
 * name: no key in the SQLite file, no key in `log.txt`, no key across the JSON-RPC bridge, no
 * key echoed back into the interface. Those are claims the README makes to users, so they are
 * worth testing against the real thing rather than against a mock.
 */
const apiKey = 'sk-e2e-DO-NOT-LEAK-3f9a1c7b5e2d4806';

test('a provider is configured, its key never leaves the host, and everything survives a restart', async ({
  diffhacker,
  repos,
}) => {
  const repo = repos.awkward();
  const first = await diffhacker.launch();
  const { welcome, changeset, settings } = screens(first.page);

  // ---------------------------------------------------------------- open a repository first

  // So the restart has both kinds of state to restore: a provider and a recent repository.
  await welcome.open(repo.root);
  await changeset.waitForLoaded();
  await expect(changeset.row('src/Web/edited.ts')).toBeVisible();

  // ---------------------------------------------------------------- configure a provider

  await settings.openButton.click();
  await expect(settings.heading).toBeVisible();
  await expect(settings.emptyState).toBeVisible();

  // Recording starts before the key is ever typed, so every frame carrying it would be caught.
  await first.recordBridgeTraffic();

  await settings.addProvider({ name: 'E2E account', model: 'gpt-4.1-mini', apiKey });

  const profile = settings.profile('E2E account');
  await expect(profile).toBeVisible();
  await expect(profile.getByText(en.providers.keyStored)).toBeVisible();
  await expect(profile.getByText(en.providers.active)).toBeVisible();
  await expect(profile.getByText('gpt-4.1-mini', { exact: true })).toBeVisible();
  await first.shot('a configured provider');

  // ---------------------------------------------------------------- the key must not come back

  // Whatever the interface shows, it is not the key. The host has no reason to send it back and
  // a response that carried it would be a real leak.
  await expect(first.page.getByText(apiKey)).toHaveCount(0);
  expect(await first.page.content()).not.toContain(apiKey);

  // Outbound is a different question: saving a provider is the one call that carries a key, and
  // it must be the only one.
  const frames = await first.bridgeFrames();
  expect(frames.length, 'the recorder saw no traffic at all').toBeGreaterThan(0);

  const carrying = frames.filter((frame) => frame.includes(apiKey));
  expect(
    carrying.length,
    `exactly one frame may carry the key, and it must be providers.save; saw ${carrying.length}`,
  ).toBe(1);
  expect(carrying[0]).toContain('providers.save');

  // ---------------------------------------------------------------- and it must not be logged

  expect(first.logText()).not.toContain(apiKey);

  // ---------------------------------------------------------------- restart

  const root = await first.stop();

  // With the app stopped, its files are on disk and can be read as a user would inspect them.
  const database = first.dataFileBytes('diffhacker.db');
  expect(database, 'the SQLite database was never created').not.toBeNull();
  expect(
    database!.includes(Buffer.from(apiKey, 'utf8')),
    'the API key is sitting in the SQLite file in plaintext',
  ).toBe(false);

  const secrets = first.dataFileBytes('secrets.dat');
  expect(secrets, 'no encrypted secret store was written').not.toBeNull();
  expect(
    secrets!.includes(Buffer.from(apiKey, 'utf8')),
    'the API key is sitting in the secret store in plaintext',
  ).toBe(false);

  expect(first.logText()).not.toContain(apiKey);

  const second = await diffhacker.launch({ root });
  const restarted = screens(second.page);

  // ---------------------------------------------------------------- the provider persisted

  await restarted.settings.openButton.click();
  await expect(restarted.settings.heading).toBeVisible();

  const restoredProfile = restarted.settings.profile('E2E account');
  await expect(restoredProfile).toBeVisible();
  await expect(restoredProfile.getByText(en.providers.active)).toBeVisible();
  await expect(restoredProfile.getByText(en.providers.keyStored)).toBeVisible();
  await second.shot('the provider survived a restart');

  // Still not readable, even from the store that kept it.
  expect(await second.page.content()).not.toContain(apiKey);

  // ---------------------------------------------------------------- the repository persisted

  await restarted.settings.backButton.click();
  await expect(restarted.welcome.heading).toBeVisible();

  const entry = restarted.welcome.recentEntry(nameOf(repo.root));
  await expect(entry).toBeVisible();
  await second.shot('the recent repository survived a restart');

  await entry.getByRole('button', { name: en.welcome.open, exact: true }).click();
  await restarted.changeset.waitForLoaded();
  await expect(restarted.changeset.row('src/Web/edited.ts')).toBeVisible();
  await second.shot('reopened after a restart');
});

/**
 * A provider that cannot work never reaches the host.
 *
 * The form marks the fields a provider genuinely needs as required, so the browser refuses the
 * submission itself. The host's matching error codes are a backstop for anything that gets past
 * the interface, and the .NET suite covers those; what is worth proving here is that the
 * interface does not let a reviewer create a configuration that could not possibly connect.
 */
test('a provider that cannot work is refused before anything is saved', async ({ diffhacker }) => {
  const app = await diffhacker.launch();
  const { settings } = screens(app.page);

  await settings.openButton.click();
  await expect(settings.heading).toBeVisible();
  await expect(settings.emptyState).toBeVisible();

  await settings.addButton.click();

  // An OpenAI-compatible endpoint has no standard endpoint to fall back on, so it needs a URL.
  await settings.typeField.selectOption('openai_compatible');
  await settings.nameField.fill('Local runtime');
  await settings.modelField.fill('llama-3.1');
  await settings.apiKeyField.fill('not-a-real-key');
  await settings.saveButton.click();

  await expect(settings.baseUrlField).toHaveAttribute('required', '');
  expect(
    await settings.baseUrlField.evaluate((field: HTMLInputElement) => field.checkValidity()),
    'the base URL was accepted as valid while empty',
  ).toBe(false);

  // Nothing was created, and the form is still open on the reviewer's own input.
  await expect(settings.saveButton).toBeVisible();
  await expect(settings.profile('Local runtime')).toHaveCount(0);
  await app.shot('an endpoint with no URL is refused before it is saved');

  // The same for the model, which is free text but cannot be blank.
  await settings.modelField.fill('');
  expect(
    await settings.modelField.evaluate((field: HTMLInputElement) => field.checkValidity()),
  ).toBe(false);

  // Complete it and it saves, with the endpoint shown back.
  await settings.modelField.fill('llama-3.1');
  await settings.baseUrlField.fill('https://localhost:11434/v1');
  await settings.saveButton.click();

  const saved = settings.profile('Local runtime');
  await expect(saved).toBeVisible();
  await expect(saved.getByText('https://localhost:11434/v1', { exact: true })).toBeVisible();
  await expect(saved.getByText(en.providers.type.openai_compatible, { exact: true })).toBeVisible();
  await app.shot('a complete OpenAI-compatible provider');
});

/**
 * Iteration 2's fixed decision that the interface "states honestly which backend is active".
 * A claim about where someone's API key is kept has to be the truth about this machine, not a
 * hardcoded sentence, so it is worth reading off the real window.
 */
test('the interface names the secret backend actually protecting the keys', async ({ diffhacker }) => {
  const app = await diffhacker.launch();
  const { settings } = screens(app.page);

  await settings.openButton.click();
  await expect(settings.heading).toBeVisible();

  await expect(app.page.getByText(en.environment.secretBackend, { exact: false })).toBeVisible();

  // The harness only runs on Windows, where the backend is DPAPI and is not a fallback.
  await expect(app.page.getByText(en.environment.backend.windows_dpapi, { exact: false })).toBeVisible();
  await expect(app.page.getByText(en.environment.fallbackWarning)).toHaveCount(0);
  await app.shot('the secret backend, stated');
});

function nameOf(path: string): string {
  const parts = path.split(/[\\/]/).filter(Boolean);
  return parts[parts.length - 1]!;
}
