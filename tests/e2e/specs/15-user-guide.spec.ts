import { dirname } from 'node:path';
import { expect, test } from '../src/fixtures.ts';
import { screens, type AnalysisScreen } from '../src/screens.ts';
import { StubProvider } from '../src/stubProvider.ts';
import { en, fill } from '../src/strings.ts';
import { GUIDE_VIEWPORT, GuideCamera } from '../src/guideCamera.ts';
import {
  GUIDE_CHANGED,
  GUIDE_NODES,
  editAfterTheRun,
  guideRepository,
  stubGuideProfile,
  stubGuideResult,
} from '../src/guideFixture.ts';
import { GUIDE_STEPS } from '../../../src/ui/src/help/guideSteps.ts';

/**
 * The user guide — Help's step-by-step walkthrough, and `docs/user-guide.md` — and the screenshots
 * both of them show.
 *
 * The first test is the guide, done for real: a provider added and tested, a repository opened and
 * profiled, an analysis run and read, a file reviewed, the grouping switched, the working tree edited
 * afterwards. Every step the guide describes is performed here against the real application, so a
 * step that stops being possible fails this spec before it misleads a reader — and each one is
 * photographed as it happens. `npm run docs:screenshots` is this spec with `--update-snapshots`,
 * which is what writes the pictures into `src/ui/src/help/screenshots/` for the application to bundle.
 *
 * The second test is the Help screen itself: reachable from every screen, returning to where it was
 * opened, and showing every step with an image that actually loaded over `diffhacker://`.
 *
 * As everywhere in this suite the provider is a scripted endpoint on localhost; nothing reaches a
 * real one, and the key typed below is not a key.
 */

const apiKey = 'sk-guide-3f9a0c71d2e84b56';

test('the step-by-step guide can be followed from start to finish', async ({ diffhacker, repos }) => {
  const provider = await StubProvider.start(['example-model', 'example-model-mini']);

  try {
    const repo = guideRepository(repos);
    const app = await diffhacker.launch();
    const { page } = app;
    const { welcome, settings, repository, changeset, profile, analysis, help } = screens(page);

    await page.setViewportSize(GUIDE_VIEWPORT);
    await page.getByRole('button', { name: en.theme.light, exact: true }).click();

    // The guide's pictures show the application as it is after the offer has been answered.
    await help.offerDismissButton.click();
    await expect(help.offer).toHaveCount(0);

    const camera = new GuideCamera(page, {
      [dirname(repo.root)]: 'C:\\src',
      [provider.baseUrl]: 'http://localhost:8000/v1',
    });

    // ------------------------------------------------------------ 1. add a provider

    await settings.openButton.click();
    await settings.addButton.click();
    await settings.typeField.selectOption('openai_compatible');
    await settings.baseUrlField.fill(provider.baseUrl);
    await settings.nameField.fill('Work account');
    await settings.modelField.fill('example-model');
    await settings.apiKeyField.fill(apiKey);

    const form = page.locator('[data-slot="card"]').filter({ has: settings.apiKeyField });
    await camera.region('provider', form, form.getByText(en.providers.add, { exact: true }), settings.apiKeyField);

    // ------------------------------------------------------------ 2. test the connection

    await form.getByRole('button', { name: en.providers.test }).click();
    const succeeded = form.getByText(fill(en.providers.testSucceeded, { count: 2 }));
    await expect(succeeded).toBeVisible();

    await camera.region('testConnection', form, settings.apiKeyField, succeeded);

    await settings.saveButton.click();
    await expect(settings.profile('Work account')).toBeVisible();

    // ------------------------------------------------------------ 3. analysis defaults

    await camera.element('defaults', settings.analysisDefaults);
    await settings.backButton.click();

    // ------------------------------------------------------------ 4. open a repository

    // Opened once and put down again, so the start screen has a recent repository to show — which is
    // what it looks like from the second time on, and the second time is most of the times.
    await welcome.open(repo.root);
    await changeset.waitForLoaded();
    await repository.changeButton.click();

    await expect(welcome.recentEntry('acme-shop')).toBeVisible();
    await welcome.pathField.fill(repo.root);
    await camera.window('openRepository');

    // ------------------------------------------------------------ 5. the changed files

    await welcome.openButton.click();
    await changeset.waitForLoaded();
    await expect(changeset.row('packages/api/src/middleware/rateLimit.ts')).toBeVisible();
    await camera.window('changes');

    // ------------------------------------------------------------ 6. the repository profile

    await profile.openButton.click();
    await expect(profile.missingNotice).toBeVisible();

    provider
      .reset()
      .callsTools(
        { name: 'get_repository_tree' },
        { name: 'report_progress', arguments: { message: 'Reading the workspace layout', phase: 'exploring' } },
      )
      .callsTools({ name: 'read_file', arguments: { path: 'CHANGELOG.md' } })
      .answers(stubGuideProfile);

    await profile.generateButton.click();
    await expect(profile.regenerateButton).toBeVisible({ timeout: 30_000 });
    await expect(profile.purposeField).toHaveValue(stubGuideProfile.purpose);

    // From the top: what the model found is the part of the screen the step is about.
    await page.locator('main').evaluate((main) => main.scrollTo(0, 0));
    await camera.window('profile');
    await profile.backButton.click();

    // ------------------------------------------------------------ 7. run options

    await analysis.openButton.click();
    await expect(analysis.emptyNotice).toBeVisible();

    await analysis.openRunOptions();
    await camera.window('runOptions');
    await analysis.closeRunOptions();

    // ------------------------------------------------------------ 8. a run in progress

    // A turn that never answers, so the picture is of a run genuinely in flight — and stopping it is
    // the guide's own advice about what Stop does.
    provider
      .reset()
      .callsTools(
        { name: 'get_project_profile' },
        {
          name: 'report_progress',
          arguments: { message: 'Following a request from the middleware to the 429 it returns', phase: 'analysing' },
        },
      )
      .callsTools({ name: 'list_changed_files' })
      .callsTools(
        { name: 'get_file_diff', arguments: { paths: [GUIDE_NODES.MIDDLEWARE] } },
        { name: 'read_file', arguments: { path: GUIDE_NODES.LIMITER } },
      )
      .callsTools({ name: 'search_text', arguments: { pattern: 'rateLimit(' } })
      .hangs();

    await analysis.runButton.click();
    await expect(
      page.getByText('Following a request from the middleware to the 429 it returns', { exact: true }),
    ).toBeVisible();
    await expect(analysis.runStat('tool-calls')).toHaveText('6', { timeout: 15_000 });
    await expect(analysis.runStat('elapsed')).not.toHaveText('0:00', { timeout: 10_000 });

    await camera.window('run');

    await analysis.stopButton.click();
    await expect(page.getByText(en.analysis.cancelled)).toBeVisible({ timeout: 30_000 });

    // And the run that finishes.
    provider
      .reset()
      .callsTools({ name: 'get_project_profile' }, { name: 'list_changed_files' })
      .callsTools({ name: 'get_file_diff', arguments: { paths: [GUIDE_NODES.MIDDLEWARE] } })
      .answers(stubGuideResult());

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 60_000 });
    await expect(analysis.graphNode(GUIDE_NODES.LIMITER)).toBeVisible({ timeout: 30_000 });
    await expect(analysis.freshness).toHaveAttribute('data-freshness', 'fresh', { timeout: 30_000 });

    // §0.2.5, on the fixture the guide shows: every changed file is on the diagram.
    for (const path of GUIDE_CHANGED) {
      await expect(page.locator(`[data-testid^="graph-node-${path}"]`).first()).toBeAttached();
    }

    // ------------------------------------------------------------ 9. summary and risks

    await expect(analysis.summaryHeading).toBeVisible();
    await camera.window('overview');

    // ------------------------------------------------------------ 10. the diagram

    // The summary folded, as a reviewer does once it is read, so the picture is mostly diagram — and
    // centred at full size on the policies, between the contract above and the middleware below,
    // rather than fitted: fifteen boxes fitted into one window are too small to read in a screenshot.
    await analysis.summaryToggle.click();
    await centreOn(analysis, 'core/src/config.ts');
    await camera.window('diagram');

    // ------------------------------------------------------------ 11. an explanation card

    // A click, not a hover: hovering does nothing on this diagram (08-explanations.spec.ts).
    const card = await analysis.clickForCard(analysis.graphNode(GUIDE_NODES.MIDDLEWARE));
    await expect(card).toContainText('Rate-limit middleware');
    await camera.window('explanation');

    await page.locator('.react-flow__pane').click({ position: { x: 12, y: 12 } });
    await expect(analysis.hoverCard).toHaveCount(0);

    // ------------------------------------------------------------ 12. the diff

    await analysis.openDiff(GUIDE_NODES.CLIENT);
    await expect(analysis.monacoEditor).toBeVisible({ timeout: 30_000 });
    // Monaco computes the diff in a worker; the picture waits for the decorations it produces.
    await expect(analysis.monaco.locator('.line-insert').first()).toBeVisible({ timeout: 30_000 });
    await expect(analysis.nodeExplanation).toContainText('Retry-After');
    await camera.window('diff');

    // ------------------------------------------------------------ 13. marked reviewed

    await analysis.toggleReviewedButton.click();
    await expect(analysis.toggleReviewedButton).toHaveText(en.analysis.diff.markUnreviewed);
    await expect(analysis.reviewProgress).toContainText('1');
    await camera.window('reviewed');

    // ------------------------------------------------------------ 14. change clusters

    await analysis.closeDiffButton.click();
    await analysis.groupingOption('change_clusters').click();
    await expect(analysis.graphSurface).toHaveAttribute('data-grouping-mode', 'change_clusters');
    await expect(analysis.graphContainer('operations')).toBeAttached({ timeout: 30_000 });
    await centreOn(analysis, 'middleware/rateLimit.ts');
    await camera.window('grouping');

    // ------------------------------------------------------------ 15. out of date

    editAfterTheRun(repo);

    await expect
      .poll(
        async () => {
          await analysis.refocus();
          return analysis.freshness.getAttribute('data-freshness');
        },
        { timeout: 30_000 },
      )
      .toBe('stale');

    await expect(analysis.staleBanner).toContainText('1 file(s) edited since');
    await camera.window('current');
  } finally {
    await provider.stop();
  }
});

/**
 * Brings one box to the middle of the canvas at full size, the way a reviewer would — the search box,
 * then Enter — and puts the search away again.
 *
 * Waits for the pan to arrive and stop, because a screenshot taken during the animation is a smear of
 * boxes: the viewport's transform has to have moved from where it was and then held still between two
 * reads.
 */
async function centreOn(analysis: AnalysisScreen, query: string): Promise<void> {
  const viewport = analysis.graphSurface.locator('.react-flow__viewport');
  const before = await viewport.getAttribute('style');

  await analysis.graphSearch.fill(query);
  await analysis.graphSearch.press('Enter');

  let previous: string | null = null;
  await expect
    .poll(
      async () => {
        const now = await viewport.getAttribute('style');
        const settled = now !== before && now === previous;
        previous = now;
        return settled;
      },
      { intervals: [250], timeout: 10_000 },
    )
    .toBe(true);

  await analysis.graphSearch.fill('');
  await analysis.graphSearch.blur();
}

test('help is one click from any screen, and its guide shows every step with its picture', async ({
  diffhacker,
  repos,
}) => {
  const app = await diffhacker.launch();
  const { page } = app;
  const { welcome, settings, analysis, help } = screens(page);

  // ------------------------------------------------------------ the offer on the start screen

  await expect(help.offer).toBeVisible();
  await help.offerOpenButton.click();

  await expect(help.heading).toBeVisible();
  await expect(help.position).toHaveText(fill(en.help.guide.position, { current: 1, total: GUIDE_STEPS.length }));
  await app.shot('help opens on the first step of the guide');

  // ------------------------------------------------------------ every step, with its screenshot

  for (const [index, step] of GUIDE_STEPS.entries()) {
    await expect(help.position).toHaveText(
      fill(en.help.guide.position, { current: index + 1, total: GUIDE_STEPS.length }),
    );
    await expect(help.stepHeading).toHaveText(en.help.guide.steps[step.id].title);

    // Loaded, not merely present: an <img> whose URL the host could not serve is still in the DOM.
    await expect(help.missingScreenshot).toHaveCount(0);
    await expect(help.screenshot).toHaveAttribute('alt', en.help.guide.steps[step.id].alt);
    await expect
      .poll(() => help.screenshot.evaluate((image: HTMLImageElement) => image.complete && image.naturalWidth))
      .toBeGreaterThan(0);

    if (index < GUIDE_STEPS.length - 1) await help.nextButton.click();
  }

  await app.shot('the last step of the guide');

  // Finish goes back to where Help was opened from, and opening the guide did not dismiss the offer.
  await help.finishButton.click();
  await expect(welcome.heading).toBeVisible();
  await expect(help.offer).toBeVisible();

  // ------------------------------------------------------------ the reference sections

  await help.openButton.click();
  await help.section('faq').click();

  const readOnly = help.faqItem('readOnly');
  await readOnly.getByText(en.help.faq.items.readOnly.question).click();
  await expect(readOnly).toHaveAttribute('open', '');
  await expect(readOnly).toContainText('Write to repository');
  await app.shot('an answer in the FAQ');

  await help.backButton.click();
  await expect(welcome.heading).toBeVisible();

  // ------------------------------------------------------------ from Settings and from Analysis

  await settings.openButton.click();
  await help.openButton.click();
  await expect(help.heading).toBeVisible();
  await help.backButton.click();
  await expect(settings.heading).toBeVisible();
  await settings.backButton.click();

  // The analysis screen is where a reviewer is most likely to stop and ask, and where Back would
  // otherwise have gone to the repository screen instead.
  const repo = repos.clean();
  repo.write('readme.md', 'changed\n');
  await welcome.open(repo.root);
  await analysis.openButton.click();
  await expect(analysis.emptyNotice).toBeVisible();

  await help.openButton.click();
  await expect(help.heading).toBeVisible();
  await help.backButton.click();
  await expect(analysis.emptyNotice).toBeVisible();
});
