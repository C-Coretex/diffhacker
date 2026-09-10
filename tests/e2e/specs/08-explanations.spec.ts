import type { AppFactory } from '../src/appHarness.ts';
import type { RepoSet } from '../src/gitFixture.ts';
import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { StubProvider, stubTwoClusterResult } from '../src/stubProvider.ts';
import { en, fill } from '../src/strings.ts';

/**
 * Explanations — Iteration 9, and the iteration that closes the MVP. Every card here opens on a
 * click, not on hover — see `useHoverTarget.ts` for why hovering was retired.
 *
 * The unit tests check the wording and the risk arithmetic. Three things only this suite can prove,
 * because all three need a browser that actually lays things out:
 *
 * 1. **A card appears where the click landed and stays on screen.** jsdom measures every element as
 *    zero, so floating-ui never positions anything there; the whole of requirement 4 is untestable
 *    below this level.
 * 2. **An edge can be clicked at all.** React Flow draws no edges in jsdom — a node has to be
 *    measured before it is edge-worthy — so requirement 2's card has never been on a real line
 *    until here.
 * 3. **The clipboard works from a custom scheme.** `diffhacker://app` is not `https:`, and whether
 *    a WebView treats it as a secure context is the host's business. Reading the path back out of
 *    the real clipboard is the only way to know which branch of `copyText` ran.
 *
 * As everywhere else here, the provider is a scripted endpoint on localhost.
 */

const apiKey = 'sk-e2e-explain-9d31c47ba0e5';

/** Runs an analysis of a small two-cluster change and leaves the diagram on screen. */
async function analysed(diffhacker: AppFactory, repos: RepoSet, provider: StubProvider) {
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
  await expect(analysis.graphNode('docs/changelog.md')).toBeVisible({ timeout: 30_000 });

  return { app, analysis, changed };
}

/** How far off the drawn line the pointer goes: past the stroke, inside the hit area. */
const EDGE_OFFSET = 6;

/**
 * A screen point six pixels to one side of the first edge's midpoint.
 *
 * Measured off the path itself — `getPointAtLength` for the position, two nearby points for the
 * direction, `getScreenCTM` to leave React Flow's viewport transform out of it — because the
 * bounding box of an L-shaped orthogonal edge has a centre that is nowhere near the line.
 */
async function pointBesideFirstEdge(page: import('@playwright/test').Page) {
  const point = await page.evaluate((offset) => {
    const path = document.querySelector<SVGPathElement>('.react-flow__edge-path');
    if (!path) return null;

    const length = path.getTotalLength();
    const here = path.getPointAtLength(length / 2);
    const next = path.getPointAtLength(Math.min(length, length / 2 + 1));

    // The unit normal to the line at that point, so the offset is across the stroke rather than
    // along it.
    const run = Math.hypot(next.x - here.x, next.y - here.y) || 1;
    const normal = { x: -(next.y - here.y) / run, y: (next.x - here.x) / run };

    const matrix = path.getScreenCTM();
    if (!matrix) return null;

    const x = here.x + normal.x * offset;
    const y = here.y + normal.y * offset;

    return { x: x * matrix.a + y * matrix.c + matrix.e, y: x * matrix.b + y * matrix.d + matrix.f };
  }, EDGE_OFFSET);

  expect(point, 'the diagram drew no edge to hover').not.toBeNull();
  return point!;
}

test('clicking a node, an edge and a cluster explains each one, risks apart', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const { app, analysis } = await analysed(diffhacker, repos, provider);

    // ------------------------------------------------------------ requirement 1: the node card

    const entry = analysis.graphNode('docs/changelog.md');
    const card = await analysis.clickForCard(entry);

    await expect(card).toContainText('A line was added to docs/changelog.md.');
    await expect(card).toContainText('The fixture needed something to review.');
    await expect(card).toContainText('Everything downstream reads from here.');

    // Verification step 5, once: the risk is inside the risk column, not in the prose.
    await expect(analysis.hoverRisks).toContainText('The entry point changed shape.');

    // Verification step 2: the card is beside its node, not over it, and inside the window.
    // The width is read from the page rather than from `viewportSize()`, which is null when
    // Playwright is attached to a window it did not open.
    const windowWidth = await app.page.evaluate(() => window.innerWidth);
    const nodeBox = (await entry.boundingBox())!;
    const cardBox = (await card.boundingBox())!;

    expect(cardBox.x).toBeGreaterThanOrEqual(nodeBox.x + nodeBox.width);
    expect(cardBox.x + cardBox.width).toBeLessThanOrEqual(windowWidth);

    await app.shot('a node explained on click');

    // ------------------------------------------------------------ requirement 3: the cluster card

    await analysis.graphContainerHeader('first-half').click();
    await expect(analysis.hoverCard).toContainText('The first half');
    await expect(analysis.hoverRisks).toContainText('Everything in The first half has to ship together.');

    await app.shot('a cluster explained on click');

    // …and from the title bar only. The rest of a cluster's region is canvas the reviewer pans
    // across and reads their files in, so a click there must leave the open card untouched. Aimed
    // into the container's own padding — ELK leaves 24 pixels down each side — so this is empty
    // region and not a file.
    const region = (await analysis.graphContainer('first-half').boundingBox())!;
    await analysis
      .graphContainer('first-half')
      .click({ position: { x: 8, y: region.height - 8 } });

    await expect(analysis.hoverCard).toContainText('The first half');

    // A cluster this small is not much wider than its own card, so the card — sized to be readable
    // rather than squeezed into whatever gap happens to be free (the bug this exists to fix) —
    // covers part of the cluster it is open over. That is an overlay doing what overlays do: closed
    // here the same way a reviewer would, rather than clicked through.
    await analysis.closeCardButton.click();
    await expect(analysis.hoverCard).toHaveCount(0);

    // ------------------------------------------------------------ requirement 2: the edge card

    // The one thing jsdom cannot render at all. Deliberately clicked *beside* the line rather than
    // on it: six pixels off a one-and-a-half pixel stroke is a miss, and only the transparent
    // twenty-pixel hit area makes it a hit. If `interactionWidth` is ever dropped, this fails.
    const beside = await pointBesideFirstEdge(app.page);
    await app.page.mouse.click(beside.x, beside.y);

    await expect(analysis.hoverCard).toContainText(en.analysis.edgeDirect);
    await expect(analysis.hoverCard).toContainText('The second file reads from the first.');
    await expect(analysis.hoverRisks).toContainText('The second file still assumes the old shape.');

    await app.shot('a relationship explained on click');

    // Hovering, meanwhile, does nothing at all — not the node, not the edge, not the title bar.
    await entry.hover();
    await expect(analysis.hoverCard).toContainText(en.analysis.edgeDirect);
  } finally {
    await provider.stop();
  }
});

test('a card can be kept by clicking, read and dismissed, and a path copied from it', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const { app, analysis } = await analysed(diffhacker, repos, provider);

    // ------------------------------------------------------------ requirement 4: keeping a card

    // Clicking is the only gesture there is. Hovering does nothing, which is what makes reading the
    // explanation, scrolling it or selecting a path out of it possible without holding a hand still.
    await analysis.clickForCard(analysis.graphNode('src/cache.ts'));

    // The pointer moving on does not touch the card at all.
    await analysis.graphNode('docs/notes.md').hover();
    await expect(analysis.hoverCard).toContainText('src/cache.ts');

    // Clicking another box moves the card there rather than refusing. This is the case the
    // popover's own dismissal used to eat.
    await analysis.clickForCard(analysis.graphNode('src/tenant.ts'));
    await expect(analysis.hoverCard).toContainText('src/tenant.ts');

    await analysis.clickForCard(analysis.graphNode('src/cache.ts'));
    await expect(analysis.hoverCard).toContainText('src/cache.ts');

    // The button on the card does the same thing for anyone who found it that way.
    await expect(analysis.closeCardButton).toBeVisible();

    // ------------------------------------------------------------ requirement 7: copy-path

    // Read back out of the real clipboard, so this proves whichever branch of `copyText` the
    // WebView actually took.
    await analysis.copyPathButton.click();
    await expect(analysis.hoverCard).toContainText(en.analysis.hover.copied);

    const copied = await app.page.evaluate(() => navigator.clipboard.readText());
    expect(copied).toBe('src/cache.ts');

    await app.shot('a card kept open, with the path copied');

    // ------------------------------------------------------------ and putting it away

    // Two ways out, because a card that can only be dismissed one way is one somebody gets stuck
    // with: a click on the empty canvas, and Escape.
    await app.page.locator('.react-flow__pane').click({ position: { x: 12, y: 12 } });
    await expect(analysis.hoverCard).toHaveCount(0);

    await analysis.clickForCard(analysis.graphNode('src/cache.ts'));
    await app.page.keyboard.press('Escape');
    await expect(analysis.hoverCard).toHaveCount(0);
  } finally {
    await provider.stop();
  }
});

/** Where React Flow has moved the canvas to, read off the transform it actually applied. */
async function viewportTranslation(page: import('@playwright/test').Page) {
  return page.locator('.react-flow__viewport').evaluate((element) => {
    const matrix = new DOMMatrixReadOnly(getComputedStyle(element).transform);
    return { x: matrix.e, y: matrix.f };
  });
}

test('the diagram pans from a press that lands on one of its lines', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const { app, analysis } = await analysed(diffhacker, repos, provider);
    await analysis.fitViewButton.click();

    // React Flow marks every edge `nopan`, and d3-zoom refuses any press inside something carrying
    // that class — so on a dense diagram, where the lines and their twenty-pixel hit areas are most
    // of the canvas, dragging simply failed wherever the reviewer happened to grab it. Only a real
    // window can show this: jsdom draws no edges, so there is nothing there to grab.
    const before = await viewportTranslation(app.page);

    const beside = await pointBesideFirstEdge(app.page);
    await app.page.mouse.move(beside.x, beside.y);
    await app.page.mouse.down();
    await app.page.mouse.move(beside.x + 120, beside.y + 80, { steps: 12 });
    await app.page.mouse.up();

    const after = await viewportTranslation(app.page);

    expect(after.x - before.x).toBeCloseTo(120, 0);
    expect(after.y - before.y).toBeCloseTo(80, 0);

    // And the drag is not also a click: letting go must not open the card of whichever line the
    // reviewer happened to start from.
    await expect(analysis.hoverCard).toHaveCount(0);

    await app.shot('the diagram panned from a line');
  } finally {
    await provider.stop();
  }
});

test('an explanation never lands on top of the diff panel', async ({ diffhacker, repos }) => {
  const provider = await StubProvider.start();

  try {
    const { app, analysis } = await analysed(diffhacker, repos, provider);

    await analysis.openDiff('src/cache.ts');
    await expect(analysis.diffPanel).toBeVisible();

    const panel = (await analysis.diffPanel.boundingBox())!;

    // The card is portalled to the document body, so nothing in the layout holds it back — a node
    // near the right of the canvas used to put its explanation squarely over the file being read.
    // The diagram's own rectangle is the card's collision boundary now, which is a fact only a
    // window that lays things out can check.
    await analysis.fitViewButton.click();

    // Aimed at the box's own header rather than its centre: fitting a graph this small into the
    // narrowed canvas beside an open diff panel puts the second cluster's nodes low enough on screen
    // to sit under the minimap in the corner, and a point the minimap is drawing over is a point
    // Playwright cannot click through.
    const node = analysis.graphNode('src/tenant.ts');
    await node.click({ position: { x: 12, y: 8 } });
    await expect(analysis.hoverCard).toBeVisible();
    const card = analysis.hoverCard;
    const cardBox = (await card.boundingBox())!;

    expect(cardBox.x + cardBox.width).toBeLessThanOrEqual(panel.x + 1);

    await app.shot('an explanation beside the diff, never over it');
  } finally {
    await provider.stop();
  }
});

test('the overview across the top collects every risk in the result', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const { app, analysis, changed } = await analysed(diffhacker, repos, provider);

    // ------------------------------------------------------------ requirement 5: always visible

    await expect(analysis.summaryHeading).toBeVisible();
    await expect(
      app.page.getByText('Every file in the fixture gained a line, which is the whole of the change.'),
    ).toBeVisible();
    await expect(
      app.page.getByText('The fixture has no tests, so nothing proves the change works.'),
    ).toBeVisible();

    // The left rail is gone: the diagram has the full width, and Iteration 10's diff panel has a
    // side of the screen to expand into.
    const windowWidth = await app.page.evaluate(() => window.innerWidth);
    const graphBox = (await app.page.locator('.react-flow').boundingBox())!;
    expect(graphBox.width).toBeGreaterThan(windowWidth * 0.9);

    await app.shot('summary and risks across the top');

    // ------------------------------------------------------------ and folding them away

    // Prose a reviewer has already read is the first thing that should give the canvas its height
    // back. The count survives the fold, because that is the part you want without asking.
    const tallBefore = (await app.page.locator('.react-flow').boundingBox())!.height;

    await analysis.summaryToggle.click();

    await expect(
      app.page.getByText('Every file in the fixture gained a line, which is the whole of the change.'),
    ).toHaveCount(0);
    await expect(app.page.getByText(fill(en.analysis.riskTotal, { count: 5 }))).toBeVisible();
    expect((await app.page.locator('.react-flow').boundingBox())!.height).toBeGreaterThan(tallBefore);

    await app.shot('the summary folded away');

    await analysis.summaryToggle.click();
    await expect(
      app.page.getByText('Every file in the fixture gained a line, which is the whole of the change.'),
    ).toBeVisible();

    // ------------------------------------------------------------ requirement 5: the rest

    await expect(analysis.riskRegister).toHaveCount(0);
    await analysis.overviewToggle.click();

    await expect(analysis.readingOrder).toBeVisible();
    await expect(analysis.clusterList).toBeVisible();
    await expect(app.page.getByText(en.analysis.statisticsHeading)).toBeVisible();
    await expect(app.page.getByText(en.analysis.overview.runHeading)).toBeVisible();

    // Run metadata: model, tokens, cost, duration — the things that say what this cost to produce.
    await expect(app.page.getByText('stub-model', { exact: true })).toBeVisible();
    await expect(app.page.getByText('Stub provider', { exact: true })).toBeVisible();

    // Verification step 6: every risk the model wrote, from all four levels, in one list. The
    // stub writes one for the change as a whole, one per cluster, one on the file it made the
    // entry point, and one on the direct link between the first two files.
    const clusters = 2;
    const expected = 1 + clusters + 1 + 1;

    await expect(analysis.riskRegister.getByRole('listitem')).toHaveCount(expected);
    await expect(analysis.riskRegister).toContainText(
      'The fixture has no tests, so nothing proves the change works.',
    );
    await expect(analysis.riskRegister).toContainText('Everything in The first half has to ship together.');
    await expect(analysis.riskRegister).toContainText('The entry point changed shape.');
    await expect(analysis.riskRegister).toContainText('The second file still assumes the old shape.');

    // And the reading order covers the whole change, which is what makes it a path rather than a
    // list of highlights.
    await expect(analysis.readingOrder.getByRole('listitem')).toHaveCount(changed.length);

    await app.shot('the overview, unfolded');

    // ------------------------------------------------------------ requirement 6: emphasis

    // The stub ranks the entry node 5 and everything else 2, so a top-ranked and a bottom-ranked
    // box are on screen together — verification step 8, checked as the attribute the styling is
    // driven from rather than by looking at a screenshot.
    await expect(analysis.graphNode('docs/changelog.md')).toHaveAttribute('data-emphasis', 'high');
    await expect(analysis.graphNode('docs/notes.md')).toHaveAttribute('data-emphasis', 'low');

    // §0.2.5 all the same: the faded box is still there, still the same size, still reachable.
    const loud = (await analysis.graphNode('docs/changelog.md').boundingBox())!;
    const quiet = (await analysis.graphNode('docs/notes.md').boundingBox())!;

    expect(Math.round(quiet.width)).toBe(Math.round(loud.width));
    expect(Math.round(quiet.height)).toBe(Math.round(loud.height));

    // ------------------------------------------------------------ and the long-form result

    await app.page.getByRole('button', { name: en.analysis.detailsHeading, exact: true }).click();
    await expect(app.page.getByText('A line was added to src/cache.ts.').first()).toBeVisible();

    await app.shot('the long-form result, two disclosures deep');

    // A last check that nothing here is generated: the fixture's cluster titles come from the
    // stub, so the diagram is rendering the stored answer rather than asking for a new one.
    await expect(
      app.page.getByText(fill(en.analysis.overview.clusterSize, { count: 2 })).first(),
    ).toBeVisible();
  } finally {
    await provider.stop();
  }
});
