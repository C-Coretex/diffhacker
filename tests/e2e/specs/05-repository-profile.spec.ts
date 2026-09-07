import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { StubProvider, stubProfileDocument } from '../src/stubProvider.ts';
import { en, fill } from '../src/strings.ts';

/**
 * The repository knowledge base, end to end.
 *
 * This is the first spec that runs a real LLM conversation through the application, against a
 * scripted local endpoint rather than a real provider. That makes it the first time
 * `analysis.progress` and `analysis.toolCall` travel the real bridge into the real window —
 * the end-to-end test `docs/decisions.md` has been asking for since Iteration 1's self-test was
 * removed, and which Iteration 5 could not write because nothing started a run.
 *
 * The documentation assertions are the other half. Writing into the user's repository is the one
 * exception to DiffHacker being read-only, so "cancel writes nothing" is checked against the
 * filesystem rather than against the interface.
 */

const apiKey = 'sk-e2e-profile-2c7f4a19be6d';

test('a repository is profiled, edited, and its documentation written only after a preview', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const repo = repos.clean();
    repo.write('readme.md', '# Fixture\n\nA repository for the end-to-end suite.\n');
    repo.write('src/main.ts', 'export const main = () => 0;\n');
    repo.commitAll('documentation and a source file');

    const app = await diffhacker.launch();
    const { welcome, settings, profile, documentation } = screens(app.page);

    // ------------------------------------------------------------ point at the stub provider

    await settings.openButton.click();
    await expect(settings.heading).toBeVisible();

    await settings.addProvider({
      name: 'Stub provider',
      model: 'stub-model',
      apiKey,
      baseUrl: provider.baseUrl,
    });

    await expect(settings.profile('Stub provider')).toBeVisible();
    await settings.backButton.click();

    // ------------------------------------------------------------ no profile yet

    await welcome.open(repo.root);

    // Requirement 10: analysis can proceed without a profile, and the interface says plainly
    // that the results will be weaker.
    await expect(app.page.getByText(en.profile.missingBody)).toBeVisible();
    await app.shot('the repository screen warns there is no profile');

    await profile.openButton.click();
    await expect(profile.heading).toBeVisible();
    await expect(profile.missingNotice).toBeVisible();

    // ------------------------------------------------------------ run the profile

    // Scripted so the run reads documentation before it explores code — requirement 2, and the
    // order the stored trace is checked for.
    provider
      .callsTools(
        { name: 'get_project_profile' },
        {
          name: 'report_progress',
          arguments: { message: 'Reading the readme', phase: 'exploring' },
        },
      )
      .callsTools({ name: 'read_file', arguments: { path: 'readme.md' } })
      .answers(stubProfileDocument);

    await profile.generateButton.click();

    // The live view: the model's own words, and the mechanical log underneath them. Both arrive
    // as JSON-RPC notifications over the real bridge.
    await expect(profile.runHeading).toBeVisible();
    // Exact: the tool log below also shows the arguments the model passed, which contain the
    // same sentence. The progress line is the one that is only ever the message itself.
    await expect(app.page.getByText('Reading the readme', { exact: true })).toBeVisible();
    await expect(app.page.getByText(en.progress.phase.exploring)).toBeVisible();
    await expect(profile.toolRow('read_file').first()).toBeVisible();
    await app.shot('a profile run in flight');

    // ------------------------------------------------------------ the stored profile

    await expect(profile.regenerateButton).toBeVisible({ timeout: 30_000 });
    await expect(profile.purposeField).toHaveValue(stubProfileDocument.purpose);
    await expect(app.page.getByText(en.profile.generatedWarning)).toBeVisible();
    await app.shot('the stored profile');

    // §0.2.9 holds on the first real prompt: the model was told which files exist, never their
    // contents. The readme's text only ever reached it through a tool call it made itself.
    const opening = JSON.stringify(provider.requests[0]?.messages ?? []);
    expect(opening).toContain('readme.md');
    expect(opening).not.toContain('A repository for the end-to-end suite.');

    // ------------------------------------------------------------ the user's own sections

    await profile.notesField.fill('The fixture is deliberately tiny.');
    await profile.instructionsField.fill('Ignore the generated/ folder.');
    await profile.saveIn('yours').click();
    await expect(app.page.getByText(en.profile.saved)).toBeVisible();

    // ------------------------------------------------------------ regenerate

    // Requirement 5: the generated half is replaced, the user's half is not touched.
    provider
      .reset()
      .callsTools({ name: 'read_file', arguments: { path: 'src/main.ts' } })
      .answers({ ...stubProfileDocument, purpose: 'A completely different description.' });

    await profile.regenerateButton.click();

    await expect(profile.purposeField).toHaveValue('A completely different description.', {
      timeout: 30_000,
    });

    await expect(profile.notesField).toHaveValue('The fixture is deliberately tiny.');
    await expect(profile.instructionsField).toHaveValue('Ignore the generated/ folder.');
    await app.shot('regenerated, with the notes untouched');

    // ------------------------------------------------------------ preview, then cancel

    await documentation.previewButton.click();
    await expect(documentation.dialog).toBeVisible();

    // Every file, with its full content, before anything is written.
    await expect(documentation.file('docs/ARCHITECTURE.md')).toBeVisible();
    await expect(documentation.file('docs/MODULES.md')).toBeVisible();
    await expect(documentation.file('docs/CONVENTIONS.md')).toBeVisible();
    await expect(documentation.dialog.getByText('A completely different description.')).toBeVisible();
    await app.shot('the documentation preview');

    await documentation.cancelButton.click();
    await expect(documentation.dialog).toBeHidden();

    // Verification step 4, against the filesystem rather than the interface.
    expect(existsSync(join(repo.root, 'docs'))).toBe(false);

    // ------------------------------------------------------------ write

    await documentation.previewButton.click();
    await expect(documentation.dialog).toBeVisible();
    await documentation.writeButton.click();

    await expect(
      app.page.getByText(fill(en.documentation.wrote, { count: 3, path: repo.root })),
    ).toBeVisible();

    for (const name of ['ARCHITECTURE.md', 'MODULES.md', 'CONVENTIONS.md']) {
      const written = join(repo.root, 'docs', name);

      expect(existsSync(written)).toBe(true);
      expect(readFileSync(written, 'utf8')).toContain('Generated by DiffHacker');
    }

    expect(readFileSync(join(repo.root, 'docs', 'ARCHITECTURE.md'), 'utf8'))
      .toContain('A completely different description.');

    await app.shot('after the documentation was written');

    // ------------------------------------------------------------ an overwrite is shown first

    await documentation.previewButton.click();
    await expect(documentation.dialog).toBeVisible();
    await expect(documentation.dialog.getByText(en.documentation.exists).first()).toBeVisible();
    await documentation.cancelButton.click();

    // ------------------------------------------------------------ restart

    const root = await app.stop();
    const restarted = await diffhacker.launch({ root });
    const after = screens(restarted.page);

    await after.welcome.open(repo.root);
    await after.profile.openButton.click();

    await expect(after.profile.purposeField).toHaveValue('A completely different description.');
    await expect(after.profile.notesField).toHaveValue('The fixture is deliberately tiny.');
    await expect(after.profile.instructionsField).toHaveValue('Ignore the generated/ folder.');
    await expect(restarted.page.getByText(/from commit [0-9a-f]{8} by stub-model/)).toBeVisible();
    await restarted.shot('the profile survived a restart');
  } finally {
    await provider.stop();
  }
});

test('a run that is cancelled leaves no profile behind', async ({ diffhacker, repos }) => {
  const provider = await StubProvider.start();

  try {
    const repo = repos.clean();
    const app = await diffhacker.launch();
    const { welcome, settings, profile } = screens(app.page);

    await settings.openButton.click();
    await settings.addProvider({
      name: 'Stub provider',
      model: 'stub-model',
      apiKey,
      baseUrl: provider.baseUrl,
    });

    await settings.backButton.click();
    await welcome.open(repo.root);
    await profile.openButton.click();

    // The stub never answers, so the run is genuinely in flight when it is stopped rather than
    // having quietly finished first.
    provider.hangs();

    await profile.generateButton.click();
    await expect(profile.runHeading).toBeVisible();

    await profile.stopButton.click();

    await expect(app.page.getByText(en.profile.cancelled)).toBeVisible({ timeout: 30_000 });
    await expect(profile.missingNotice).toBeVisible();
    await expect(profile.generateButton).toBeVisible();
    await app.shot('a cancelled run stored nothing');
  } finally {
    await provider.stop();
  }
});
