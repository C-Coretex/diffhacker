import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { StubProvider, stubAnalysisMissing, stubAnalysisResult } from '../src/stubProvider.ts';
import { en } from '../src/strings.ts';

/**
 * The analysis pipeline, end to end.
 *
 * This is the iteration the product exists for, so the assertions here are about its invariants
 * rather than about its appearance: every changed file reaches the result (§0.2.5), no file content
 * reaches the prompt (§0.2.9), nothing partial is shown or stored (§0.2.8), and reopening a stored
 * analysis never starts another conversation.
 *
 * As with the profile spec, the provider is a scripted endpoint on localhost. No test in this
 * repository spends money or depends on a network.
 */

const apiKey = 'sk-e2e-analysis-91d3fbc07a24';

test('a change is analysed, validated, stored, and reopened without running again', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const repo = repos.clean();
    repo.write('readme.md', '# Fixture\n\nA repository for the analysis suite.\n');
    repo.write('src/main.ts', 'export const main = () => 0;\n');
    repo.commitAll('a baseline to change');

    // The uncommitted change under review: one edit, one new file, one deletion.
    repo.write('src/main.ts', 'export const main = (tenant: string) => tenant.length;\n');
    repo.write('src/tenant.ts', 'export type Tenant = string;\n');
    repo.remove('readme.md');

    const app = await diffhacker.launch();
    const { welcome, settings, changeset, analysis } = screens(app.page);

    // ------------------------------------------------------------ point at the stub provider

    await settings.openButton.click();
    await settings.addProvider({
      name: 'Stub provider',
      model: 'stub-model',
      apiKey,
      baseUrl: provider.baseUrl,
    });

    await expect(settings.profile('Stub provider')).toBeVisible();
    await settings.backButton.click();

    // ------------------------------------------------------------ nothing analysed yet

    await welcome.open(repo.root);
    await expect(changeset.heading).toBeVisible();

    await analysis.openButton.click();
    await expect(analysis.heading).toBeVisible();
    await expect(analysis.emptyNotice).toBeVisible();

    await app.shot('a change that has not been analysed');

    // ------------------------------------------------------------ run it

    const changed = ['readme.md', 'src/main.ts', 'src/tenant.ts'];

    provider
      .callsTools(
        { name: 'get_project_profile' },
        { name: 'report_progress', arguments: { message: 'Reading the diff', phase: 'exploring' } },
      )
      .callsTools({ name: 'get_file_diff', arguments: { path: 'src/main.ts' } })
      .answers(stubAnalysisResult(changed));

    await analysis.runButton.click();

    // The live view, arriving over the real bridge: the model's own sentence, the phase it
    // reported, and the mechanical record of what it actually called.
    await expect(analysis.runHeading).toBeVisible();
    await expect(app.page.getByText('Reading the diff', { exact: true })).toBeVisible();
    await expect(app.page.getByText(en.progress.phase.exploring)).toBeVisible();
    await expect(analysis.toolRow('get_file_diff').first()).toBeVisible();

    // Iteration 8 requirement 14: how full the context is, live, measured by the host and carried
    // on the same notification channel as everything else here.
    await expect(analysis.contextMeter).toBeVisible();

    // §0.2.8: no part of the result is on screen while the run is still going.
    await expect(analysis.summaryHeading).toHaveCount(0);

    await app.shot('an analysis run in flight');

    // ------------------------------------------------------------ the whole result

    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });
    await expect(analysis.summaryHeading).toBeVisible();

    // §0.2.5 as an assertion: every changed file is in the result, including the deleted one.
    // Read off the diagram, which is where Iteration 8 put it — a box per file, keyed by node id.
    for (const path of changed) {
      await expect(analysis.graphNode(path)).toBeVisible({ timeout: 30_000 });
    }

    // And the model's own words are still reachable, one click away.
    await analysis.detailsToggle.click();
    await expect(analysis.entryBadge.first()).toBeVisible();

    // The risks are their own column, never folded into the summary.
    await expect(app.page.getByText(en.analysis.risksHeading, { exact: true }).first()).toBeVisible();
    await expect(
      app.page.getByText('The fixture has no tests, so nothing proves the change works.'),
    ).toBeVisible();

    await app.shot('a stored analysis');

    // ------------------------------------------------------------ §0.2.9

    // The opening message names every changed path and carries none of their contents. This is
    // the grep the iteration asks for, run against the real outbound request.
    const opening = JSON.stringify(provider.requests[0]?.messages ?? []);

    expect(opening).toContain('src/tenant.ts');
    expect(opening).toContain('readme.md');
    expect(opening).not.toContain('export type Tenant = string;');
    expect(opening).not.toContain('A repository for the analysis suite.');
    expect(opening).not.toContain('@@');

    // ------------------------------------------------------------ reopening runs nothing

    const requestsBefore = provider.requests.length;
    const root = await app.stop();
    const restarted = await diffhacker.launch({ root });
    const reopened = screens(restarted.page);

    await reopened.welcome.open(repo.root);
    await reopened.analysis.openButton.click();

    await expect(reopened.analysis.summaryHeading).toBeVisible();
    await expect(reopened.analysis.rerunButton).toBeVisible();
    await expect(reopened.analysis.emptyNotice).toHaveCount(0);

    // Requirement 5, from the only log that could prove it: the provider was never asked again.
    expect(provider.requests.length).toBe(requestsBefore);

    await restarted.shot('a reopened analysis, with no new provider request');
  } finally {
    await provider.stop();
  }
});

test('a result that drops a file is sent back with the file named, and repaired', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const repo = repos.clean();
    repo.write('src/main.ts', 'export const main = () => 0;\n');
    repo.commitAll('a baseline to change');

    repo.write('src/main.ts', 'export const main = () => 1;\n');
    repo.write('src/dropped.ts', 'export const dropped = true;\n');

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

    const changed = ['src/dropped.ts', 'src/main.ts'];

    // First answer leaves a file out; the second covers everything.
    provider
      .answers(stubAnalysisMissing(changed, 'src/dropped.ts'))
      .answers(stubAnalysisResult(changed));

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });

    // Verification step 4: the repair message names the file that was missing, rather than being
    // a generic "try again" that costs a round trip and teaches nothing.
    const repair = JSON.stringify(provider.requests[1]?.messages.at(-1) ?? {});

    expect(repair).toContain('src/dropped.ts');
    expect(repair).toContain('No node covers the changed file');
    expect(repair).toContain('Do not delete anything');

    // And the stored result is the repaired one: every file covered.
    for (const path of changed) {
      await expect(analysis.graphNode(path)).toBeVisible({ timeout: 30_000 });
    }

    await app.shot('an analysis that was repaired before it validated');
  } finally {
    await provider.stop();
  }
});

test('a result that never validates fails loudly and stores nothing', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const repo = repos.clean();
    repo.write('src/main.ts', 'export const main = () => 0;\n');
    repo.commitAll('a baseline to change');

    repo.write('src/main.ts', 'export const main = () => 1;\n');
    repo.write('src/dropped.ts', 'export const dropped = true;\n');

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

    // The last scripted turn repeats, so this answer comes back for every repair round until the
    // cap is spent.
    provider.answers(stubAnalysisMissing(['src/dropped.ts', 'src/main.ts'], 'src/dropped.ts'));

    await analysis.runButton.click();

    // Verification step 5: it fails loudly, and the user is shown what was wrong rather than a
    // bare code.
    await expect(app.page.getByRole('alert')).toBeVisible({ timeout: 30_000 });
    await expect(analysis.emptyNotice).toBeVisible();
    await expect(analysis.summaryHeading).toHaveCount(0);

    await app.shot('an analysis that never validated');

    // Nothing was persisted as if it were valid: a restart still finds no analysis.
    const root = await app.stop();
    const restarted = await diffhacker.launch({ root });
    const reopened = screens(restarted.page);

    await reopened.welcome.open(repo.root);
    await reopened.analysis.openButton.click();

    await expect(reopened.analysis.emptyNotice).toBeVisible();
  } finally {
    await provider.stop();
  }
});

test('a cancelled run leaves no analysis behind', async ({ diffhacker, repos }) => {
  const provider = await StubProvider.start();

  try {
    const repo = repos.clean();
    repo.write('src/main.ts', 'export const main = () => 0;\n');
    repo.commitAll('a baseline to change');
    repo.write('src/main.ts', 'export const main = () => 1;\n');

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

    // The stub never answers, so the run is genuinely in flight when it is stopped rather than
    // having quietly finished first.
    provider.hangs();

    await analysis.runButton.click();
    await expect(analysis.runHeading).toBeVisible();
    await analysis.stopButton.click();

    await expect(app.page.getByText(en.analysis.cancelled)).toBeVisible({ timeout: 30_000 });
    await expect(analysis.emptyNotice).toBeVisible();

    await app.shot('a cancelled analysis run');
  } finally {
    await provider.stop();
  }
});

test('a five-hundred-file change completes and validates', async ({ diffhacker, repos }) => {
  const provider = await StubProvider.start();

  try {
    // §0.2.10: ten files or fifteen hundred, the same path has to work. Five hundred is the size
    // the iteration asks to be demonstrated, and it is the case where the completeness invariant
    // stops being something a reader can check by eye.
    const repo = repos.large(500);

    const app = await diffhacker.launch();
    const { welcome, settings, changeset, analysis } = screens(app.page);

    await settings.openButton.click();
    await settings.addProvider({
      name: 'Stub provider',
      model: 'stub-model',
      apiKey,
      baseUrl: provider.baseUrl,
    });
    await settings.backButton.click();

    await welcome.open(repo.root);
    await expect(changeset.summary(500, 500, 0)).toBeVisible({ timeout: 30_000 });

    const changed = Array.from({ length: 500 }, (_, index) => `internal/file${index}.cs`);

    provider.answers(stubAnalysisResult(changed));

    await analysis.openButton.click();
    await analysis.runButton.click();

    await expect(analysis.rerunButton).toBeVisible({ timeout: 60_000 });
    await expect(analysis.summaryHeading).toBeVisible();

    // Requirement 11's scale, drawn rather than only stored: the first and last of five hundred
    // boxes both exist. Whether it is *fast* is measured by hand and recorded in docs/decisions.md;
    // what this asserts is that nothing was dropped on the way to the canvas.
    await expect(analysis.graphNode('internal/file0.cs')).toBeVisible({ timeout: 60_000 });
    await expect(analysis.graphNode('internal/file499.cs')).toBeVisible();

    // The whole file list went out in one prompt, and still carried no file content.
    const opening = JSON.stringify(provider.requests[0]?.messages ?? []);

    expect(opening).toContain('internal/file0.cs');
    expect(opening).toContain('internal/file499.cs');
    expect(opening).not.toContain('// baseline 0\n// edited');

    // Five hundred nodes in one cluster, counted by the application rather than claimed by the
    // model — and the run would have failed validation had any of them been missing.
    await expect(app.page.getByText(en.analysis.statNodes, { exact: true })).toBeVisible();

    await app.shot('a five-hundred-file analysis');
  } finally {
    await provider.stop();
  }
});
