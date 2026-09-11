import type { AppFactory } from '../src/appHarness.ts';
import type { GitRepo, RepoSet } from '../src/gitFixture.ts';
import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { StubProvider, stubAnalysisResult } from '../src/stubProvider.ts';
import { en, fill } from '../src/strings.ts';

/** The History button's accessible name for a library of `count` runs. */
const history = (count: number) => fill(en.analysis.library.button, { count });

/**
 * Watching a run, inspecting it afterwards, returning to any earlier one, and being told when the
 * working tree has moved on from the one on screen.
 *
 * Each of these is a claim about the whole application rather than one layer. The live figures come
 * from the session through the notifier into the window; the trace comes back out of SQLite; reopening
 * is only "instant without re-running" if the provider is not asked for anything, which only counting
 * the provider's requests can show; and staleness is a comparison between a hash taken when the run
 * started and a real file edited on disk afterwards.
 *
 * As everywhere else here, the provider is a scripted endpoint on localhost. Nothing reaches a real
 * one.
 */

const apiKey = 'sk-e2e-library-5c2e90a4b7d1';

/** Every file the fixture's change touches, in the order git reports them. */
const changed = ['readme.md', 'src/main.ts', 'src/tenant.ts'];

/** The fixture: a baseline, then an uncommitted edit, a new file and a deletion. */
function changedRepository(repos: RepoSet): GitRepo {
  const repo = repos.clean();
  repo.write('readme.md', '# Fixture\n\nA repository for the library suite.\n');
  repo.write('src/main.ts', 'export const main = () => 0;\n');
  repo.commitAll('a baseline to change');

  repo.write('src/main.ts', 'export const main = (tenant: string) => tenant.length;\n');
  repo.write('src/tenant.ts', 'export type Tenant = string;\n');
  repo.remove('readme.md');

  return repo;
}

async function ready(diffhacker: AppFactory, repo: GitRepo, provider: StubProvider) {
  const app = await diffhacker.launch();
  const { welcome, settings, analysis } = screens(app.page);

  await settings.openButton.click();
  await settings.addProvider({
    name: 'Stub provider',
    model: 'stub-model',
    apiKey,
    baseUrl: provider.baseUrl,
  });
  await settings.backButton.click();

  await welcome.open(repo.root);
  await analysis.openButton.click();
  await expect(analysis.emptyNotice).toBeVisible();

  return { app, analysis };
}

test('a run is watched live, inspected afterwards, and every run stays reopenable for free', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const repo = changedRepository(repos);
    const { app, analysis } = await ready(diffhacker, repo, provider);

    // ---------------------------------------------------------- requirement 6: the live view

    // Three tool calls and then a turn that never answers, so the run is genuinely in flight while
    // the strip is read — and a clock that moves during a silence is the thing being checked.
    provider
      .callsTools(
        { name: 'get_project_profile' },
        { name: 'report_progress', arguments: { message: 'Reading the diff', phase: 'exploring' } },
      )
      .callsTools({ name: 'get_file_diff', arguments: { path: 'src/main.ts' } })
      .hangs();

    await analysis.runButton.click();

    // The phase is the model's own, from its report_progress call; the count is the host's running
    // total, carried on every event; the clock is the renderer's, and moves with nothing arriving.
    await expect(app.page.getByText('Reading the diff', { exact: true })).toBeVisible();
    await expect(analysis.runStat('phase')).toHaveText(en.progress.phase.exploring);
    await expect(analysis.runStat('tool-calls')).toHaveText('3');
    await expect(analysis.runStat('turn')).toHaveText('3');
    await expect(analysis.runStat('elapsed')).not.toHaveText('0:00', { timeout: 10_000 });
    await expect(analysis.runStat('cost')).toHaveText(en.toolLog.costUnknown);

    await app.shot('the live strip during a run');

    await analysis.stopButton.click();
    await expect(app.page.getByText(en.analysis.cancelled)).toBeVisible({ timeout: 30_000 });

    // ---------------------------------------------------------- requirement 7: the inspector

    const calls = ['get_project_profile', 'report_progress', 'get_file_diff', 'read_file'];

    provider
      .reset()
      .callsTools(
        { name: 'get_project_profile' },
        { name: 'report_progress', arguments: { message: 'Reading the diff', phase: 'exploring' } },
      )
      .callsTools({ name: 'get_file_diff', arguments: { path: 'src/main.ts' } })
      .callsTools({ name: 'read_file', arguments: { path: 'src/tenant.ts' } })
      .answers(stubAnalysisResult(changed));

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });
    await expect(analysis.historyButton).toHaveAccessibleName(history(1));

    await analysis.overviewToggle.click();
    await analysis.inspector.getByRole('button', { name: /^Tool calls \(4\)/ }).click();

    // Every call, in the order the model asked for them, each with the size of what came back.
    await expect(analysis.inspectorRows).toHaveCount(calls.length);
    await expect(analysis.inspectorRows.getByTestId('tool-call-name')).toHaveText(calls);

    for (const size of await analysis.inspectorRows.getByTestId('tool-call-size').allTextContents()) {
      expect(size).toMatch(/^\d[\d,.]* (B|KB|MB)$/);
    }

    await expect(analysis.inspector.getByTestId('trace-progress')).toContainText('Reading the diff');

    await app.shot('the tool-call inspector after the run');

    // ---------------------------------------------------------- requirement 8: the library

    // Two more runs of the same change, so there are three to choose between.
    for (const expected of [2, 3]) {
      provider.reset().answers(stubAnalysisResult(changed));
      await analysis.rerunButton.click();
      await expect(analysis.historyButton).toHaveAccessibleName(history(expected), { timeout: 30_000 });
    }

    // Across a restart, so the list is what was stored rather than what this session remembers.
    const root = await app.stop();
    const restarted = await diffhacker.launch({ root });
    const reopened = screens(restarted.page);

    await reopened.welcome.open(repo.root);
    await reopened.analysis.openButton.click();
    await expect(reopened.analysis.historyButton).toHaveAccessibleName(history(3), { timeout: 30_000 });

    const requestsBefore = provider.requests.length;

    await reopened.analysis.historyButton.click();
    await expect(reopened.analysis.historyEntries).toHaveCount(3);

    for (const entry of await reopened.analysis.historyEntries.all()) {
      await expect(entry.getByTestId('analysis-history-model')).toHaveText('Stub provider · stub-model');
      await expect(entry.getByTestId('analysis-history-size')).toHaveText('3 file(s) +2 −4');
      await expect(entry.getByTestId('analysis-history-cost')).toHaveText(en.analysis.costUnknown);
    }

    await restarted.shot('three runs in the library after a restart');

    // Reopen the oldest: it is on screen at once, says it is not the latest, and nothing was asked of
    // the provider to get it there.
    const oldest = reopened.analysis.historyEntries.last();
    const oldestId = await oldest.getAttribute('data-analysis-id');

    await oldest.getByTestId('analysis-history-open').click();

    await expect(reopened.analysis.earlierRunNotice).toBeVisible();
    await expect(reopened.analysis.graphNode('src/main.ts')).toBeVisible({ timeout: 30_000 });
    expect(provider.requests.length).toBe(requestsBefore);

    await reopened.analysis.historyButton.click();
    await expect(
      reopened.analysis.historyEntries.and(restarted.page.locator('[data-current="true"]')),
    ).toHaveAttribute('data-analysis-id', oldestId!);

    await restarted.shot('an earlier run reopened');

    // Delete the middle one, behind its confirmation; the one on screen stays where it is.
    await reopened.analysis.historyEntries.nth(1).getByTestId('analysis-history-delete').click();
    await reopened.analysis.confirmDeleteButton.click();

    await expect(reopened.analysis.historyButton).toHaveAccessibleName(history(2));
    await expect(reopened.analysis.earlierRunNotice).toBeVisible();

    // And back to the latest.
    await reopened.analysis.openLatestButton.click();
    await expect(reopened.analysis.earlierRunNotice).toHaveCount(0);
    expect(provider.requests.length).toBe(requestsBefore);
  } finally {
    await provider.stop();
  }
});

test('an analysis says when the working tree has moved on, and stops once it is put back', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const repo = changedRepository(repos);
    const { app, analysis } = await ready(diffhacker, repo, provider);

    provider.answers(stubAnalysisResult(changed));
    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });

    // Fresh, and said to be — rather than inferred from a banner that has not appeared yet.
    await expect(analysis.freshness).toHaveAttribute('data-freshness', 'fresh', { timeout: 30_000 });
    await expect(analysis.staleBanner).toHaveCount(0);

    // Same line counts, different content: only a content fingerprint can see this edit.
    const original = 'export const main = (tenant: string) => tenant.length;\n';
    repo.write('src/main.ts', 'export const main = (tenant: string) => tenant.size;\n');

    await expect
      .poll(
        async () => {
          await analysis.refocus();
          return analysis.freshness.getAttribute('data-freshness');
        },
        { timeout: 30_000 },
      )
      .toBe('stale');

    await expect(analysis.staleBanner).toBeVisible();
    await expect(analysis.staleBanner).toContainText('1 file(s) edited since');

    await app.shot('the stale prompt after an edit');

    // Put back byte for byte, and the prompt goes: a modification time could not have said this.
    repo.write('src/main.ts', original);

    await expect
      .poll(
        async () => {
          await analysis.refocus();
          return analysis.freshness.getAttribute('data-freshness');
        },
        { timeout: 30_000 },
      )
      .toBe('fresh');

    await expect(analysis.staleBanner).toHaveCount(0);

    // Reopening checks too, with no focus event at all: edit, restart, open.
    repo.write('src/extra.ts', 'export const extra = 1;\n');

    const root = await app.stop();
    const restarted = await diffhacker.launch({ root });
    const reopened = screens(restarted.page);

    await reopened.welcome.open(repo.root);
    await reopened.analysis.openButton.click();

    await expect(reopened.analysis.staleBanner).toBeVisible({ timeout: 30_000 });
    await expect(reopened.analysis.staleBanner).toContainText('1 file(s) now changed that the analysis does not cover');

    await restarted.shot('the stale prompt on reopening');

    // And the prompt's own button brings it up to date.
    provider.reset().answers(stubAnalysisResult([...changed, 'src/extra.ts'].sort()));
    await reopened.analysis.staleBanner.getByRole('button', { name: en.analysis.stale.reanalyse }).click();

    await expect(reopened.analysis.historyButton).toHaveAccessibleName(history(2), { timeout: 30_000 });
    await expect(reopened.analysis.freshness).toHaveAttribute('data-freshness', 'fresh', { timeout: 30_000 });
    await expect(reopened.analysis.staleBanner).toHaveCount(0);

    await restarted.shot('analysed again and fresh');
  } finally {
    await provider.stop();
  }
});
