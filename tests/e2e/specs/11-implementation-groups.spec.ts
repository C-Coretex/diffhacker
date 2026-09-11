import type { Page } from '@playwright/test';
import type { AppFactory } from '../src/appHarness.ts';
import type { RepoSet } from '../src/gitFixture.ts';
import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { StubProvider, stubImplementationResult } from '../src/stubProvider.ts';
import { en } from '../src/strings.ts';

/**
 * An interface and its implementations, drawn as one box.
 *
 * Two claims only a real window can make. First, that every row of a merged box is still, to the
 * reviewer, the file it stands for: clicking it explains that file, double-clicking it opens that
 * file's diff, and marking it reviewed marks that file alone — through a real React Flow node whose
 * click the surface has to resolve to a row. Second, that the toggle is a way of *looking*: taking
 * the box apart and putting it back asks the provider for nothing.
 *
 * And one claim about the request, asserted on the wire: a run told not to look for implementation
 * groups never mentions them, and the toggle then says why there is nothing to merge.
 *
 * The toggle is remembered in `localStorage`. What is asserted here is that the real engine was
 * handed the choice, not that it is read back after a restart: the harness stops the host with a
 * forced kill, and Chromium writes `localStorage` to disk lazily, so a restart here would test the
 * kill rather than the application. Reading it back on start is covered by the unit tests, against
 * the same key.
 */

const apiKey = 'sk-e2e-implementations-5b2e81c09d47';

/** The abstraction, its caller, then the two implementations — the order the stub reads them in. */
const changed = ['src/IStore.ts', 'src/Caller.ts', 'src/MemoryStore.ts', 'src/DiskStore.ts'];

const abstraction = 'src/IStore.ts';

async function ready(diffhacker: AppFactory, repos: RepoSet, provider: StubProvider) {
  const repo = repos.implementations();
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

/** What the page has asked the engine to remember about merging, if anything. */
function storedMergePreference(page: Page): Promise<string | null> {
  return page.evaluate(() => window.localStorage.getItem('diffhacker.graph.mergeImplementations'));
}

test('an interface and its implementations are one box whose rows are still their own files', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const { app, analysis } = await ready(diffhacker, repos, provider);

    provider.answers(stubImplementationResult(changed));

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });

    // ---- one box, three rows ---------------------------------------------------------------
    const box = analysis.mergedBox(abstraction);
    await expect(box).toBeVisible({ timeout: 30_000 });

    for (const path of [abstraction, 'src/MemoryStore.ts', 'src/DiskStore.ts']) {
      await expect(box.getByTestId(`graph-node-${path}`)).toBeVisible();
    }

    await expect(box.getByTestId(`graph-node-${abstraction}`)).toHaveAttribute('data-role', 'abstraction');
    await expect(box.getByTestId('graph-node-src/Caller.ts')).toHaveCount(0);
    await expect(analysis.graphNode('src/Caller.ts')).toBeVisible();
    await expect(analysis.mergeImplementationsToggle).toHaveAttribute('aria-pressed', 'true');

    await app.shot('an interface merged with its two implementations');

    const spentOnAnalysing = provider.requests.length;

    // ---- a row explains its own file -------------------------------------------------------
    await analysis.fitViewButton.click();
    const card = await analysis.clickForCard(box.getByText('MemoryStore.ts', { exact: true }));

    await expect(card).toContainText('What src/MemoryStore.ts does in this change');
    await expect(card).not.toContainText('What src/IStore.ts does in this change');
    await app.shot('the card of one row of a merged box');
    await analysis.closeCardButton.click();

    // ---- a row is marked reviewed on its own -----------------------------------------------
    await analysis.pressOnBox('src/MemoryStore.ts', analysis.nodeReviewedButton('src/MemoryStore.ts'));

    await expect(analysis.graphNode('src/MemoryStore.ts')).toHaveAttribute('data-reviewed', 'true');
    await expect(analysis.graphNode(abstraction)).not.toHaveAttribute('data-reviewed', 'true');
    await expect(analysis.graphNode('src/DiskStore.ts')).not.toHaveAttribute('data-reviewed', 'true');

    // ---- a row opens its own diff ----------------------------------------------------------
    await analysis.openDiff('src/DiskStore.ts');
    await expect(analysis.graphNode('src/DiskStore.ts')).toHaveAttribute('data-current', 'true');
    await app.shot('the diff of one implementation, its row marked as the current file');
    await analysis.closeDiffButton.click();

    // ---- taking it apart spends nothing, and loses nothing ---------------------------------
    await analysis.mergeImplementationsToggle.click();

    await expect(analysis.mergedBox(abstraction)).toHaveCount(0);
    await expect(analysis.mergeImplementationsToggle).toHaveAttribute('aria-pressed', 'false');

    for (const path of changed) {
      await expect(analysis.graphNode(path)).toBeVisible();
    }

    // The mark made on a row is the node's, so it is on the node's own box now.
    await expect(analysis.graphNode('src/MemoryStore.ts')).toHaveAttribute('data-reviewed', 'true');
    await app.shot('the same change with implementations drawn apart');

    // Remembered by the engine, not by the host: presentation, like the colour scheme.
    expect(await storedMergePreference(app.page)).toBe('false');

    await analysis.mergeImplementationsToggle.click();
    await expect(analysis.mergedBox(abstraction)).toBeVisible();
    expect(await storedMergePreference(app.page)).toBe('true');

    // Two relayouts, and not one request to the provider for either.
    expect(provider.requests.length).toBe(spentOnAnalysing);
  } finally {
    await provider.stop();
  }
});

test('a run told not to look for implementation groups never mentions them, and says so', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const { app, analysis } = await ready(diffhacker, repos, provider);

    await analysis.setRunOption('implementation-groups', false);

    provider.answers({ ...stubImplementationResult(changed), implementationGroups: undefined });

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });
    await expect(analysis.graphNode(abstraction)).toBeVisible({ timeout: 30_000 });

    // Taken out of the request — the prompt section and the schema property both — rather than
    // asked for and thrown away.
    const sent = JSON.stringify(provider.requests[0]);

    expect(sent).not.toContain('implementationGroups');
    expect(sent).not.toContain('Implementation groups');
    expect(sent).toContain('dependencyContainers');

    // Nothing to merge, and the control says why rather than disappearing.
    await expect(analysis.mergedBox(abstraction)).toHaveCount(0);
    await expect(analysis.mergeImplementationsToggle).toBeDisabled();
    await expect(analysis.mergeImplementationsToggle).toHaveAttribute('title', en.analysis.graph.merged.notAsked);

    await app.shot('an analysis with no implementation groups asked for');

    // And the choice was for that run only: the next one starts from the defaults in Settings.
    await analysis.openRunOptions();
    await expect(analysis.runOption('implementation-groups')).toBeChecked();
  } finally {
    await provider.stop();
  }
});
