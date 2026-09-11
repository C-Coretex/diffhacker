import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { StubProvider, stubAnalysisResult } from '../src/stubProvider.ts';
import { en } from '../src/strings.ts';

/**
 * Configurable run limits: a run that hits a tool-call or token ceiling pauses and asks whether to
 * continue, rather than failing outright the way a hard stop always used to.
 *
 * The provider profile's own override is what makes this reachable without waiting through five
 * hundred real tool calls — two is a limit a three-call script can hit deliberately. Continuing
 * raises it and the run finishes; stopping ends it exactly as the old hard stop did, and §0.2.8
 * still holds: nothing partial ever reaches the screen either way.
 */

const apiKey = 'sk-e2e-budget-2b8f114ac930';

test('continuing past a reached limit lets the run finish', async ({ diffhacker, repos }) => {
  const provider = await StubProvider.start();

  try {
    const repo = repos.clean();
    repo.write('readme.md', '# Fixture\n\nA repository for the budget-limit suite.\n');
    repo.write('src/main.ts', 'export const main = () => 0;\n');
    repo.commitAll('a baseline to change');
    repo.write('src/main.ts', 'export const main = (tenant: string) => tenant.length;\n');

    const app = await diffhacker.launch();
    const { welcome, settings, changeset, analysis, budgetPrompt } = screens(app.page);

    await settings.openButton.click();
    await settings.addProvider({
      name: 'Stub provider',
      model: 'stub-model',
      apiKey,
      baseUrl: provider.baseUrl,
      // Two rather than the default five hundred, so a three-call script can reach it on purpose.
      maxToolCalls: '2',
    });

    await expect(settings.profile('Stub provider')).toBeVisible();
    await settings.backButton.click();

    await welcome.open(repo.root);
    await expect(changeset.heading).toBeVisible();

    await analysis.openButton.click();
    await expect(analysis.heading).toBeVisible();

    const changed = ['src/main.ts'];

    // Each call lands in its own turn, so the budget is checked between them: one call, a second
    // that reaches the two-call ceiling, and a third that only fires once the reviewer continues.
    provider
      .callsTools({ name: 'get_project_profile' })
      .callsTools({ name: 'get_file_diff', arguments: { path: 'src/main.ts' } })
      .callsTools({ name: 'get_file_diff', arguments: { path: 'src/main.ts' } })
      .answers(stubAnalysisResult(changed));

    await analysis.runButton.click();
    await expect(analysis.runHeading).toBeVisible();

    // The prompt, arriving over the same bridge as everything else about this run.
    await expect(budgetPrompt.dialog).toBeVisible({ timeout: 30_000 });
    await expect(app.page.getByText(/reached its limit of 2 tool call/)).toBeVisible();

    // Nothing has been produced yet, and nothing here says otherwise (§0.2.8).
    await expect(analysis.summaryHeading).toHaveCount(0);

    await app.shot('a run paused at its tool-call limit');

    await budgetPrompt.continueButton.click();
    await expect(budgetPrompt.dialog).toHaveCount(0);

    // The raised limit lets the third call through, and the run completes normally.
    await expect(analysis.rerunButton).toBeVisible({ timeout: 30_000 });
    await expect(analysis.summaryHeading).toBeVisible();
    await expect(analysis.graphNode('src/main.ts')).toBeVisible({ timeout: 30_000 });

    // Four requests: the two tool-call turns that reached the limit, the third that only fired
    // because continuing raised it, and the turn that submitted the answer. Continuing did not
    // silently drop the call that triggered it.
    expect(provider.requests.length).toBe(4);

    await app.shot('the run finished after being told to continue');
  } finally {
    await provider.stop();
  }
});

test('stopping at a reached limit ends the run with nothing produced', async ({ diffhacker, repos }) => {
  const provider = await StubProvider.start();

  try {
    const repo = repos.clean();
    repo.write('readme.md', '# Fixture\n\nA repository for the budget-limit suite.\n');
    repo.write('src/main.ts', 'export const main = () => 0;\n');
    repo.commitAll('a baseline to change');
    repo.write('src/main.ts', 'export const main = (tenant: string) => tenant.length;\n');

    const app = await diffhacker.launch();
    const { welcome, settings, changeset, analysis, budgetPrompt } = screens(app.page);

    await settings.openButton.click();
    await settings.addProvider({
      name: 'Stub provider',
      model: 'stub-model',
      apiKey,
      baseUrl: provider.baseUrl,
      maxToolCalls: '2',
    });

    await expect(settings.profile('Stub provider')).toBeVisible();
    await settings.backButton.click();

    await welcome.open(repo.root);
    await expect(changeset.heading).toBeVisible();

    await analysis.openButton.click();
    await expect(analysis.heading).toBeVisible();

    // Only two turns scripted: a run that is told to stop must never ask the stub for a third.
    provider
      .callsTools({ name: 'get_project_profile' })
      .callsTools({ name: 'get_file_diff', arguments: { path: 'src/main.ts' } });

    await analysis.runButton.click();
    await expect(analysis.runHeading).toBeVisible();
    await expect(budgetPrompt.dialog).toBeVisible({ timeout: 30_000 });

    await budgetPrompt.stopButton.click();
    await expect(budgetPrompt.dialog).toHaveCount(0);

    // The same translated code a hard stop has always produced — this is that path, chosen by the
    // reviewer instead of forced on them.
    await expect(app.page.getByText(en.runFailure.llm_budget_exceeded, { exact: true })).toBeVisible();
    await expect(analysis.summaryHeading).toHaveCount(0);
    await expect(analysis.emptyNotice).toBeVisible();

    // Stopping must not have let a third request through.
    expect(provider.requests.length).toBe(2);

    await app.shot('a run stopped at its tool-call limit');
  } finally {
    await provider.stop();
  }
});
