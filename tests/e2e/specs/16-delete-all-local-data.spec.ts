import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { en } from '../src/strings.ts';

/**
 * "Delete all local data": the one button in Settings that erases the database, every stored API
 * key and the log files, rather than merely reading one of them back.
 *
 * Three things are worth proving against the real, compiled application rather than a unit test.
 * First, that the host really does close the window on its own afterward — nothing here forces
 * it, unlike every other restart this suite drives — because a run that kept going against a
 * database it had just deleted is not a state DiffHacker supports. Second, that a fresh launch
 * onto the same data directory comes up clean rather than crashing on a database with no schema
 * in it: the migration path from schema version zero is otherwise only ever exercised on a
 * genuinely new install. Third, that the pending-log-wipe marker — left behind because Serilog
 * was still holding `log.txt` open at the moment the wipe ran — is actually consulted at the next
 * real start-up, before logging opens a new file, and not only by the unit test that calls the
 * same code directly.
 */
const apiKey = 'sk-e2e-delete-all-data-3f9a1c7b';

test('deleting all local data erases the database and the key, closes the window, and starts clean', async ({
  diffhacker,
}) => {
  const first = await diffhacker.launch();
  const { settings } = screens(first.page);

  await settings.openButton.click();
  await expect(settings.heading).toBeVisible();
  await expect(settings.emptyState).toBeVisible();

  await settings.addProvider({ name: 'To be erased', model: 'gpt-4.1-mini', apiKey });

  const profile = settings.profile('To be erased');
  await expect(profile).toBeVisible();
  await expect(profile.getByText(en.providers.keyStored)).toBeVisible();

  // Something worth erasing exists on disk before the button is ever pressed.
  expect(first.dataFileBytes('diffhacker.db'), 'the settings database was never created').not.toBeNull();
  expect(first.dataFileBytes('secrets.dat'), 'no encrypted secret store was written').not.toBeNull();

  await first.recordBridgeTraffic();

  // ------------------------------------------------------------------- confirm, then delete

  await settings.deleteAllDataButton.click();
  await expect(first.page.getByText(en.dataManagement.confirmTitle)).toBeVisible();
  await first.shot('confirming the deletion');

  await settings.deleteAllDataConfirmButton.click();
  await expect(settings.deleteAllDataDone).toBeVisible();
  await first.shot('all local data deleted');

  const frames = await first.bridgeFrames();
  expect(frames.some((frame) => frame.includes('data.deleteAll')), 'the request was never sent').toBe(true);

  // ---------------------------------------------------------- the host closes itself, unforced

  const root = await first.waitUntilClosedByTheHost();

  expect(first.dataFileBytes('diffhacker.db'), 'the database survived the wipe').toBeNull();
  expect(first.dataFileBytes('secrets.dat'), 'the secret store survived the wipe').toBeNull();

  // The active log file could not be removed while this same process was still writing to it —
  // that is what the marker is for, and finishing the job is the next launch's.
  expect(
    first.hasPendingLogWipeMarker(),
    'no marker was left for the next launch to finish clearing the log directory',
  ).toBe(true);

  // ------------------------------------------------------------- a fresh launch starts clean

  const second = await diffhacker.launch({ root });
  const restarted = screens(second.page);

  await restarted.settings.openButton.click();
  await expect(restarted.settings.heading).toBeVisible();
  await expect(restarted.settings.emptyState).toBeVisible();
  await expect(restarted.settings.profile('To be erased')).toHaveCount(0);
  await second.shot('a fresh launch after the wipe has nothing configured');

  // The previous run's log file is what the marker was for, and it is gone before this run's
  // own logger ever opened one to replace it.
  expect(
    second.hasPendingLogWipeMarker(),
    'the marker was still there after the launch that was supposed to act on it',
  ).toBe(false);
});
