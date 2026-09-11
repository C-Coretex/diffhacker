import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { StubProvider, stubAnalysisResult, withoutParts } from '../src/stubProvider.ts';
import { en } from '../src/strings.ts';

/**
 * What an analysis asks for: defaults set in Settings, and a one-off override per run.
 *
 * Three claims only the whole application can make. First, that a part switched off in Settings is
 * taken out of the request — asserted on the wire, where the saving is, and not on the screen — and
 * that the model is told plainly not to do that work. Second, that the screen then leaves out what
 * nobody asked for rather than drawing it empty: an absent risk column, not "no risks". Third, that an
 * override in the run options is sent with that run and then forgotten, while Settings keeps what it
 * was told.
 *
 * As everywhere else here, the provider is a scripted endpoint on localhost. Nothing reaches a real
 * one.
 */

const apiKey = 'sk-e2e-analysis-parts-8c1d4e7f20a3';

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

test('parts turned off in Settings leave the request, the screen says so, and a run override is forgotten', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
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

    // ---- the defaults ----------------------------------------------------------------------
    // Every part on at brief, until the reviewer says otherwise.
    await expect(settings.analysisDefault('risks')).toBeChecked();
    await expect(settings.analysisDefaultVerbosity('brief')).toHaveAttribute('aria-pressed', 'true');

    await settings.analysisDefault('risks').uncheck();
    await settings.analysisDefault('edge-explanations').uncheck();
    await settings.saveAnalysisDefaults();
    await app.shot('analysis-defaults-in-settings');

    await settings.backButton.click();
    await welcome.open(repo.root);
    await analysis.openButton.click();
    await expect(analysis.emptyNotice).toBeVisible();

    // The run options start from them, and say so without being opened.
    await expect(analysis.runOptionsButton).toContainText('Brief · 2 part(s) off');
    await expect(analysis.runOptionsButton).toHaveAttribute('data-changed', 'false');

    // ---- a run with the defaults -------------------------------------------------------------
    provider.answers(withoutParts(stubAnalysisResult(changed), { risks: true, edgeExplanations: true }));

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });
    await expect(analysis.graphNode(changed[0]!)).toBeVisible({ timeout: 30_000 });

    const first = JSON.stringify(provider.requests[0]);

    // Taken out of the request — the schema and the prompt's guidance both — and replaced by one plain
    // instruction not to do the work.
    expect(first).not.toContain('overallRisks');
    expect(first).not.toContain('Each individual risk');
    expect(first).toContain('NOT WANTED ON THIS RUN');
    expect(first).toContain('Edge explanations.');
    expect(first).toContain('brief prose');

    // And what was kept is still asked for.
    expect(first).toContain('whyItChanged');
    expect(first).toContain('dependencyContainers');

    // The screen leaves out what nobody asked for, and says what that was.
    await expect(analysis.skippedParts).toHaveText('Not asked for: risks, relationship explanations');
    await expect(app.page.getByText(en.analysis.noRisks, { exact: true })).toHaveCount(0);

    const card = await analysis.clickForCard(analysis.graphNode(changed[0]!));
    await expect(card.getByText(`A line was added to ${changed[0]!}.`)).toBeVisible();
    await expect(analysis.hoverRisks).toHaveCount(0);
    await app.shot('an-analysis-without-risks');
    await analysis.closeCardButton.click();

    // ---- a one-off override ----------------------------------------------------------------
    await analysis.setRunOption('risks', true);
    await expect(analysis.runOptionsButton).toContainText('Brief · 1 part(s) off');
    await expect(analysis.runOptionsButton).toHaveAttribute('data-changed', 'true');

    const beforeRerun = provider.requests.length;
    provider.answers(withoutParts(stubAnalysisResult(changed), { edgeExplanations: true }));

    await analysis.rerunButton.click();
    await expect(analysis.skippedParts).toHaveText('Not asked for: relationship explanations', { timeout: 30_000 });

    const second = JSON.stringify(provider.requests.slice(beforeRerun));

    expect(second).toContain('overallRisks');
    expect(second).toContain('Edge explanations.');

    // Risks were asked for this time, so they are drawn.
    await analysis.clickForCard(analysis.graphNode(changed[0]!));
    await expect(analysis.hoverRisks).toHaveCount(1);
    await analysis.closeCardButton.click();

    // ---- and forgotten ---------------------------------------------------------------------
    // The next run starts from the defaults again...
    await expect(analysis.runOptionsButton).toContainText('Brief · 2 part(s) off');
    await expect(analysis.runOptionsButton).toHaveAttribute('data-changed', 'false');

    await analysis.openRunOptions();
    await expect(analysis.runOption('risks')).not.toBeChecked();
    await app.shot('run-options-back-to-the-defaults');
    await analysis.closeRunOptions();

    // ...and Settings still holds exactly what it was told.
    await analysis.backButton.click();
    await settings.openButton.click();
    await expect(settings.analysisDefault('risks')).not.toBeChecked();
    await expect(settings.analysisDefault('edge-explanations')).not.toBeChecked();
    await expect(settings.analysisDefault('node-explanations')).toBeChecked();
  } finally {
    await provider.stop();
  }
});
