import type { AppFactory } from '../src/appHarness.ts';
import type { RepoSet } from '../src/gitFixture.ts';
import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { StubProvider, stubGroupedResult } from '../src/stubProvider.ts';
import { en } from '../src/strings.ts';

/**
 * Grouping modes — Iteration 11.
 *
 * The one claim in this iteration that no unit test can make is the one the whole design turns on:
 * **switching costs nothing.** Requirement 2 is that a mode change does not re-run the analysis, and
 * the only honest way to check it is to count what the provider was asked for — across a switch,
 * across a switch back, and across a restart of the application onto the same stored state.
 *
 * The rest follows from having a real window and a real diagram: two arrangements of one node set,
 * where dependency flow keeps a database-to-auth-to-API path in one cluster and change clusters
 * splits it into three. jsdom can be told what the containers are; only this can show that both
 * pictures were drawn from one stored answer.
 *
 * As everywhere else here, the provider is a scripted endpoint on localhost. Nothing reaches a real
 * one.
 */

const apiKey = 'sk-e2e-grouping-4f80a1c76d29';

/** Every file the layered fixture changes, in the order git reports them. */
const changed = [
  'api/routes.ts',
  'api/users.ts',
  'auth/middleware.ts',
  'auth/tokens.ts',
  'db/001_add_tenant.sql',
  'db/schema.sql',
  'docs/changelog.md',
];

/**
 * A configured application in front of the layered fixture, with nothing analysed yet.
 *
 * Split out because both tests need it and neither is about getting there — the same reason
 * `08-explanations.spec.ts` has an `analysed` helper.
 */
async function ready(diffhacker: AppFactory, repos: RepoSet, provider: StubProvider) {
  const repo = repos.layered();
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

  return { app, analysis, repo };
}

test('one analysis is read two ways, and switching between them spends nothing', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const { app, analysis, repo } = await ready(diffhacker, repos, provider);

    provider.answers(stubGroupedResult(changed));

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });
    await expect(analysis.graphNode(changed[0]!)).toBeVisible({ timeout: 30_000 });
    await app.shot('analysed-in-dependency-flow');

    const spentOnAnalysing = provider.requests.length;

    // Verification step 2: the cross-concern path is in ONE cluster. Database, auth and API are
    // three areas and this is one change, and dependency flow is the view that says so.
    await expect(analysis.graphSurface).toHaveAttribute('data-grouping-mode', 'dependency_flow');
    await expect(analysis.groupingOption('dependency_flow')).toHaveAttribute('aria-pressed', 'true');
    await expect(analysis.graphContainer('tenant-end-to-end')).toBeVisible();
    await expect(analysis.groupingExplanation).toHaveText(
      en.analysis.graph.grouping.dependencyFlowBody,
    );

    for (const path of changed) {
      await expect(analysis.graphNode(path)).toBeVisible();
    }

    // ---- switch ----------------------------------------------------------------------------
    await analysis.groupingOption('change_clusters').click();

    await expect(analysis.graphSurface).toHaveAttribute('data-grouping-mode', 'change_clusters');
    await expect(analysis.groupingOption('change_clusters')).toHaveAttribute('aria-pressed', 'true');

    // Verification step 7, read cold: the line under the toolbar changes to say what this picture
    // is organised by, so the reviewer is told why it looks different rather than left to guess.
    await expect(analysis.groupingExplanation).toHaveText(
      en.analysis.graph.grouping.changeClustersBody,
    );

    // Verification step 3: the same change, split by theme. One cluster per area.
    for (const theme of ['api', 'auth', 'db', 'docs']) {
      await expect(analysis.graphContainer(theme)).toBeVisible({ timeout: 30_000 });
    }

    await expect(analysis.graphContainer('tenant-end-to-end')).toHaveCount(0);
    await app.shot('analysed-in-change-clusters');

    // Verification step 1: the node set is identical, and equal to the changeset. Read off the
    // diagram, because that is where a missing file would actually hurt.
    for (const path of changed) {
      await expect(analysis.graphNode(path)).toBeVisible();
    }

    // ---- switch back, and again ------------------------------------------------------------
    await analysis.groupingOption('dependency_flow').click();
    await expect(analysis.graphSurface).toHaveAttribute('data-grouping-mode', 'dependency_flow');

    await analysis.groupingOption('change_clusters').click();
    await expect(analysis.graphSurface).toHaveAttribute('data-grouping-mode', 'change_clusters');

    // Verification step 5, and the whole reason one pass produces both groupings: not one further
    // request to the provider, on any of the three switches.
    expect(provider.requests.length).toBe(spentOnAnalysing);

    // ---- reviewed marks survive it ---------------------------------------------------------
    // Verification step 4. Marks are node ids on the analysis row, so a grouping changes underneath
    // them without touching them.
    await analysis.pressOnBox('db/schema.sql', analysis.nodeReviewedButton('db/schema.sql'));
    await analysis.pressOnBox('api/users.ts', analysis.nodeReviewedButton('api/users.ts'));

    await expect(analysis.graphNode('db/schema.sql')).toHaveAttribute('data-reviewed', 'true');
    await expect(analysis.graphNode('api/users.ts')).toHaveAttribute('data-reviewed', 'true');

    await analysis.groupingOption('dependency_flow').click();
    await expect(analysis.graphSurface).toHaveAttribute('data-grouping-mode', 'dependency_flow');

    await expect(analysis.graphNode('db/schema.sql')).toHaveAttribute('data-reviewed', 'true');
    await expect(analysis.graphNode('api/users.ts')).toHaveAttribute('data-reviewed', 'true');

    // And nothing else was marked on the way, which is the other half of "correctly attributed".
    await expect(analysis.graphNode('auth/tokens.ts')).not.toHaveAttribute('data-reviewed', 'true');
    await app.shot('marks-survived-the-switch');

    // ---- and it is remembered for this analysis --------------------------------------------
    await analysis.groupingOption('change_clusters').click();
    await expect(analysis.graphSurface).toHaveAttribute('data-grouping-mode', 'change_clusters');

    const spentBeforeRestart = provider.requests.length;
    const root = await app.stop();
    const restarted = await diffhacker.launch({ root });
    const screensAfter = screens(restarted.page);
    const reopened = screensAfter.analysis;

    // A restart lands on the welcome screen: the repository is a recent one, not a reopened one.
    await screensAfter.welcome.open(repo.root);
    await reopened.openButton.click();
    await expect(reopened.rerunButton).toBeVisible({ timeout: 30_000 });

    // The grouping the reviewer left it in, off disk, with no conversation to get it back.
    await expect(reopened.graphSurface).toHaveAttribute('data-grouping-mode', 'change_clusters');
    expect(provider.requests.length).toBe(spentBeforeRestart);
    await restarted.shot('reopened-in-the-grouping-it-was-left-in');
  } finally {
    await provider.stop();
  }
});

test('a run told not to produce change clusters never mentions them, and says the view is unbought', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const { app, analysis, repo } = await ready(diffhacker, repos, provider);

    // The opt-out is only worth having if it takes the work out of the request, so that is what is
    // asserted — at the wire, not by looking at the screen.
    await analysis.changeClustersToggle.uncheck();

    provider.answers({
      ...stubGroupedResult(changed),
      clusterContainers: undefined,
      clusterReadingOrder: undefined,
    });

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });
    await expect(analysis.graphNode(changed[0]!)).toBeVisible({ timeout: 30_000 });

    const sent = JSON.stringify(provider.requests[0]);

    expect(sent).not.toContain('clusterContainers');
    expect(sent).not.toContain('clusterReadingOrder');
    expect(sent).not.toContain('GROUP THE SAME NODES TWICE');

    // And it still asks for the grouping it does want, so the saving is the second grouping rather
    // than half the instructions.
    expect(sent).toContain('dependencyContainers');

    // The control is present and disabled with its reason, rather than absent — the feature is
    // unbought, not missing.
    await expect(analysis.graphSurface).toHaveAttribute('data-grouping-mode', 'dependency_flow');
    await expect(analysis.groupingOption('change_clusters')).toBeDisabled();
    await expect(analysis.groupingOption('change_clusters')).toHaveAttribute(
      'title',
      en.analysis.graph.grouping.unavailable,
    );

    await app.shot('one-grouping-only');

    // And the choice is remembered, so the next run does not quietly cost more again.
    const root = await app.stop();
    const restarted = await diffhacker.launch({ root });
    const screensAfter = screens(restarted.page);
    const reopened = screensAfter.analysis;

    // A restart lands on the welcome screen: the repository is a recent one, not a reopened one.
    await screensAfter.welcome.open(repo.root);
    await reopened.openButton.click();
    await expect(reopened.rerunButton).toBeVisible({ timeout: 30_000 });
    await expect(reopened.changeClustersToggle).not.toBeChecked();
  } finally {
    await provider.stop();
  }
});
