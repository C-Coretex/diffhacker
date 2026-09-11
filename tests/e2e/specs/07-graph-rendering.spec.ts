import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { StubProvider, stubTwoClusterResult } from '../src/stubProvider.ts';
import { en, fill } from '../src/strings.ts';

/**
 * The diagram — Iteration 8, and what CLAUDE.md calls the product.
 *
 * The unit tests already check the ELK input, the layout snapshot and the components in jsdom.
 * What only this suite can prove is that the whole stack agrees: a real analysis stored by the
 * real host, read back over the real bridge, laid out by a real Web Worker in a real WebView, and
 * drawn. Every one of those is a place the graph could arrive empty with nothing failing.
 *
 * As everywhere else here, the provider is a scripted endpoint on localhost.
 */

const apiKey = 'sk-e2e-graph-4471aa02be9c';

test('an analysis renders as a diagram that can be searched, collapsed and expanded', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const repo = repos.clean();
    repo.write('src/cache.ts', 'export const cache = () => 0;\n');
    repo.write('src/tenant.ts', 'export type Tenant = string;\n');
    repo.write('docs/notes.md', '# Notes\n');
    repo.write('docs/changelog.md', '# Changelog\n');
    repo.commitAll('a baseline to change');

    repo.write('src/cache.ts', 'export const cache = (tenant: string) => tenant.length;\n');
    repo.write('src/tenant.ts', 'export type Tenant = { id: string };\n');
    repo.write('docs/notes.md', '# Notes\n\nThe cache key now includes the tenant.\n');
    repo.write('docs/changelog.md', '# Changelog\n\n- Tenant-aware cache.\n');

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

    const changed = ['docs/changelog.md', 'docs/notes.md', 'src/cache.ts', 'src/tenant.ts'];
    provider.answers(stubTwoClusterResult(changed));

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });

    // ------------------------------------------------------------ requirement 1: one diagram

    // Every file is a box, and every cluster is a region. §0.2.5 again, but drawn this time: a
    // graph that dropped a file would be as wrong as a result that did.
    for (const path of changed) {
      await expect(analysis.graphNode(path)).toBeVisible({ timeout: 30_000 });
    }

    await expect(analysis.graphContainer('first-half')).toBeVisible();
    await expect(analysis.graphContainer('second-half')).toBeVisible();

    await app.shot('the change as a diagram');

    // ------------------------------------------------------------ requirement 2: what a box says

    const cache = analysis.graphNode('src/cache.ts');

    await expect(cache).toContainText('cache.ts');
    await expect(cache).toContainText('src/');
    await expect(cache).toContainText('modified');

    // ------------------------------------------------------------ verification step 2

    // The entry node is at the top of its own container. Not "somewhere in it" — above every
    // other box in it, which is the whole reason ELK is given a layer constraint.
    const entry = await analysis.graphNode('docs/changelog.md').boundingBox();
    const follower = await analysis.graphNode('docs/notes.md').boundingBox();

    expect(entry).not.toBeNull();
    expect(follower).not.toBeNull();
    expect(entry!.y).toBeLessThan(follower!.y);

    // ------------------------------------------------------------ requirement 8: collapsing

    const collapse = app.page.getByRole('button', {
      name: fill(en.analysis.graph.collapseContainer, { title: 'The second half' }),
    });

    await collapse.click();

    await expect(analysis.graphNode('src/cache.ts')).toHaveCount(0);
    await expect(analysis.graphContainer('second-half')).toHaveAttribute('data-collapsed', 'true');

    // A folded cluster still says enough to decide whether to open it again.
    await expect(analysis.graphContainer('second-half')).toContainText(
      fill(en.analysis.graph.fileCount, { count: 2 }),
    );

    await app.shot('a collapsed cluster');

    // And the other cluster is untouched by its neighbour folding.
    await expect(analysis.graphNode('docs/changelog.md')).toBeVisible();

    await app.page
      .getByRole('button', {
        name: fill(en.analysis.graph.expandContainer, { title: 'The second half' }),
      })
      .click();

    await expect(analysis.graphNode('src/cache.ts')).toBeVisible();

    // Collapse all, then expand all — the same journey thirty clusters would need.
    await analysis.collapseAllButton.click();
    await expect(analysis.graphContainer('first-half')).toHaveAttribute('data-collapsed', 'true');

    await analysis.expandAllButton.click();
    await expect(analysis.graphNode('docs/notes.md')).toBeVisible();

    // ------------------------------------------------------------ requirements 10 and 13: search

    await analysis.graphSearch.fill('tenant');

    // The file, listed under the heading that says it matched a path rather than a title.
    await app.page
      .getByRole('button')
      .filter({ hasText: 'tenant.ts' })
      .first()
      .click();

    await expect(analysis.graphNode('src/tenant.ts')).toBeVisible();

    await app.shot('a file found on the diagram');

    // ------------------------------------------------------------ requirements 4 and 6: legend

    await analysis.legendButton.click();

    await expect(app.page.getByText(en.analysis.graph.legendDirect)).toBeVisible();
    await expect(app.page.getByText(en.analysis.graph.legendConceptual)).toBeVisible();
    // exact: true — the toolbar's own project-filter button reads "Filter projects", a substring
    // match away from colliding with the legend's heading.
    await expect(app.page.getByText(en.analysis.graph.legendProjects, { exact: true })).toBeVisible();

    await app.shot('the diagram legend');

    // Closes the legend before opening the next popover — otherwise it stays open, and its
    // portalled content sits over whatever the project filter draws underneath it.
    await app.page.keyboard.press('Escape');

    // ------------------------------------------------------------ the project filter

    // `docs/` and `src/` have no manifest, so `ProjectLocator` falls back to the top-level
    // directory — the same two projects `stubTwoClusterResult` already split into two clusters.
    await analysis.projectFilterButton.click();
    await analysis.projectFilterItem('docs').click();

    // Off the diagram entirely, not merely dimmed — and its cluster goes with it, because a
    // cluster with nothing left in it draws nothing.
    await expect(analysis.graphNode('docs/notes.md')).toHaveCount(0);
    await expect(analysis.graphNode('docs/changelog.md')).toHaveCount(0);
    await expect(analysis.graphContainer('first-half')).toHaveCount(0);

    // The other project is unaffected by hiding this one.
    await expect(analysis.graphNode('src/cache.ts')).toBeVisible();
    await expect(analysis.graphContainer('second-half')).toBeVisible();

    const banner = analysis.projectFilterBanner;
    await expect(banner).toBeVisible();
    await expect(banner).toContainText('docs');

    await app.shot('a project filtered off the diagram');

    // The filter popover is still open over the canvas; close it before reaching for the banner's
    // own button underneath it.
    await app.page.keyboard.press('Escape');

    await analysis.projectFilterBannerShowAllButton.click();

    await expect(analysis.graphNode('docs/notes.md')).toBeVisible();
    await expect(banner).toHaveCount(0);
  } finally {
    await provider.stop();
  }
});

test('nothing on the diagram moves when it is dragged', async ({ diffhacker, repos }) => {
  const provider = await StubProvider.start();

  try {
    // Verification step 7, which can only be checked here: `nodesDraggable={false}` is a prop, and
    // a prop is not evidence. This is the pointer actually being dragged across a real WebView.
    const repo = repos.clean();
    repo.write('src/cache.ts', 'export const cache = () => 0;\n');
    repo.write('src/tenant.ts', 'export type Tenant = string;\n');
    repo.commitAll('a baseline to change');

    repo.write('src/cache.ts', 'export const cache = (tenant: string) => tenant.length;\n');
    repo.write('src/tenant.ts', 'export type Tenant = { id: string };\n');

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

    provider.answers(stubTwoClusterResult(['src/cache.ts', 'src/tenant.ts']));

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });

    const dragged = analysis.graphNode('src/cache.ts');
    const neighbour = analysis.graphNode('src/tenant.ts');

    await expect(dragged).toBeVisible({ timeout: 30_000 });
    await expect(neighbour).toBeVisible();

    /**
     * Where the dragged node sits *relative to its neighbour*.
     *
     * Screen coordinates are the wrong measure here, because dragging the canvas pans it — which
     * requirement 9 explicitly wants — so every box legitimately moves together. What must not
     * change is a node's position within the graph, and the gap between two of them is exactly
     * that, measured through the same transform as everything else.
     */
    const gap = async () => {
      const [a, b] = await Promise.all([dragged.boundingBox(), neighbour.boundingBox()]);
      expect(a).not.toBeNull();
      expect(b).not.toBeNull();
      return { x: Math.round(a!.x - b!.x), y: Math.round(a!.y - b!.y) };
    };

    const before = await gap();
    const startedAt = (await dragged.boundingBox())!;

    await app.page.mouse.move(startedAt.x + startedAt.width / 2, startedAt.y + startedAt.height / 2);
    await app.page.mouse.down();
    await app.page.mouse.move(startedAt.x + 240, startedAt.y + 180, { steps: 12 });
    await app.page.mouse.up();

    // The drag did something — the canvas panned — so this is not a test that passes because
    // nothing happened at all.
    const pannedTo = (await dragged.boundingBox())!;
    expect(Math.round(pannedTo.x)).not.toBe(Math.round(startedAt.x));

    // And the node did not move within the graph. There is no manual layout in this product and
    // nothing to persist (§0.6), so a node that moved would be a feature nobody asked for.
    expect(await gap()).toEqual(before);

    await app.shot('a node that did not move when dragged');
  } finally {
    await provider.stop();
  }
});

test('the long-form result is a click away rather than gone', async ({ diffhacker, repos }) => {
  const provider = await StubProvider.start();

  try {
    // Iteration 7 rendered the model's full text on the page. Iteration 8 folded it behind a
    // disclosure, and the thing worth proving is that it is folded rather than lost — it is still
    // the only place the model's own words can be read.
    const repo = repos.clean();
    repo.write('src/cache.ts', 'export const cache = () => 0;\n');
    repo.commitAll('a baseline to change');
    repo.write('src/cache.ts', 'export const cache = (tenant: string) => tenant.length;\n');

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

    provider.answers(stubTwoClusterResult(['src/cache.ts']));

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });

    // The band carries the summary and the risks whether or not anything is open.
    await expect(analysis.summaryHeading).toBeVisible();
    await expect(
      app.page.getByText('The fixture has no tests, so nothing proves the change works.'),
    ).toBeVisible();

    const prose = app.page.getByText('A line was added to src/cache.ts.');
    await expect(prose).toHaveCount(0);

    // Two disclosures deep since Iteration 9: the overview band, then the long-form result inside
    // it. Folded, not gone — it is still the only place the model's whole answer reads as prose.
    await analysis.overviewToggle.click();
    await analysis.detailsToggle.click();

    await expect(prose).toBeVisible();
    await expect(analysis.entryBadge.first()).toBeVisible();

    await app.shot('the long-form result behind its disclosure');
  } finally {
    await provider.stop();
  }
});

test('the legend stays inside the window however many projects there are', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    // The project list is the one part of the legend with no natural length: one entry per project
    // in the change, and a repository can have as many as it likes. A `Makefile` is enough to make
    // a directory a project (`ProjectLocator`), so this is fourteen of them.
    const repo = repos.clean();
    const modules = Array.from({ length: 14 }, (_, index) => `module${index}`);

    for (const module of modules) {
      repo.write(`${module}/Makefile`, 'all:\n\techo built\n');
      repo.write(`${module}/main.ts`, 'export const value = 0;\n');
    }

    repo.commitAll('a baseline across many projects');

    for (const module of modules) {
      repo.write(`${module}/main.ts`, 'export const value = 1;\n');
    }

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

    provider.answers(stubTwoClusterResult(modules.map((module) => `${module}/main.ts`)));

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });
    await expect(analysis.graphNode('module0/main.ts')).toBeVisible({ timeout: 30_000 });

    await analysis.legendButton.click();

    const legend = app.page.locator('[data-radix-popper-content-wrapper]').last().locator('> *');
    await expect(legend).toBeVisible();

    const box = (await legend.boundingBox())!;
    const windowHeight = await app.page.evaluate(() => window.innerHeight);

    // It is longer than the room it has — which is what makes the rest of this worth asserting.
    const overflowing = await legend.evaluate((el) => el.scrollHeight > el.clientHeight + 1);
    expect(overflowing, 'the fixture did not make the legend long enough to overflow').toBe(true);

    // And it is inside the window anyway, rather than running off the bottom with the projects
    // nobody can reach.
    expect(box.y + box.height).toBeLessThanOrEqual(windowHeight);

    // The end of the list is reachable by scrolling. Ten projects get a colour of their own and
    // the rest collapse behind one row, so this is the note that says so.
    await legend.evaluate((el) => el.scrollTo(0, el.scrollHeight));
    await expect(
      app.page.getByText(fill(en.analysis.graph.legendOtherProjects, { count: 4 })),
    ).toBeVisible();

    await app.shot('a legend with more projects than fit');
  } finally {
    await provider.stop();
  }
});
