import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { en, fill } from '../src/strings.ts';

/**
 * The repositories that break things, driven through one app instance in sequence: a clean tree,
 * a repository with no commits, a folder that is not a repository at all, a path that does not
 * exist, a bare repository, a subdirectory, and a changeset larger than the list shows at once.
 *
 * These are the states where an application either says something useful or shows an empty box
 * and leaves the user guessing.
 */
test('every awkward repository state says something useful', async ({ diffhacker, repos }) => {
  const clean = repos.clean();
  const withoutCommits = repos.withoutCommits();
  const plain = repos.plainDirectory();
  const bare = repos.bare();

  const app = await diffhacker.launch();
  const { welcome, repository, changeset } = screens(app.page);

  // ---------------------------------------------------------------- a clean working tree

  await welcome.open(clean.root);
  await changeset.waitForLoaded();

  // Requirement 9. An empty list reads as a failure; "nothing to review" reads as good news.
  await expect(changeset.cleanHeading).toBeVisible();
  await expect(app.page.getByText(en.changeset.cleanBody)).toBeVisible();
  await expect(app.page.getByRole('listitem')).toHaveCount(0);
  await app.shot('a clean working tree reports clean');

  // The toggle changes the wording, because "clean" means something different when new files
  // are being hidden.
  await changeset.untrackedToggle.uncheck();
  await changeset.waitForLoaded();
  await expect(app.page.getByText(en.changeset.cleanBodyUntrackedExcluded)).toBeVisible();
  await changeset.untrackedToggle.check();
  await changeset.waitForLoaded();

  // ---------------------------------------------------------------- no commits at all

  await repository.changeButton.click();
  await welcome.open(withoutCommits.root);
  await changeset.waitForLoaded();

  await expect(repository.noCommitsWarning).toBeVisible();
  await expect(changeset.noCommitsNotice).toBeVisible();

  // Compared against the empty tree, so everything reads as added — the staged file and the
  // untracked one alike. Only the untracked one is flagged as new to git.
  await expect(
    changeset.row('first.cs').getByText(en.changeset.status.added, { exact: true }),
  ).toBeVisible();
  await expect(
    changeset.row('second.cs').getByText(en.changeset.status.added, { exact: true }),
  ).toBeVisible();
  await expect(
    changeset.row('second.cs').getByText(en.changeset.untracked, { exact: true }),
  ).toBeVisible();
  await expect(
    changeset.row('first.cs').getByText(en.changeset.untracked, { exact: true }),
  ).toHaveCount(0);
  await app.shot('a repository with no commits');

  // ---------------------------------------------------------------- not a repository

  await repository.changeButton.click();
  await welcome.open(plain.root);

  await expect(welcome.error).toHaveText(
    fill(en.error.repository_not_a_git_repository, { path: plain.root }),
  );
  // The developer-facing message on the exception must never reach the interface.
  await expect(app.page.getByText(/fatal:/)).toHaveCount(0);
  await app.shot('a folder that is not a repository');

  // ---------------------------------------------------------------- a path that does not exist

  const missing = `${plain.root}-does-not-exist`;
  await welcome.open(missing);
  await expect(welcome.error).toHaveText(fill(en.error.repository_not_found, { path: missing }));

  // ---------------------------------------------------------------- a bare repository

  await welcome.open(bare.root);
  await expect(welcome.error).toHaveText(fill(en.error.repository_is_bare, { path: bare.root }));
  await app.shot('a bare repository is refused with a reason');

  // ---------------------------------------------------------------- a subdirectory

  // Opening a folder inside a repository resolves upwards, and the interface says so rather than
  // silently changing the path underneath the user.
  await welcome.open(withoutCommits.path('.'));
  await changeset.waitForLoaded();
  await expect(changeset.row('first.cs')).toBeVisible();
});

/**
 * A changeset bigger than one screen. Split out because it builds 260 files and reveals them in
 * pages, which is slow enough that mixing it into the sequence above would obscure a failure.
 */
test('a large changeset is revealed in pages', async ({ diffhacker, repos }) => {
  const files = 260;
  const large = repos.large(files);

  const app = await diffhacker.launch();
  const { welcome, changeset } = screens(app.page);

  await welcome.open(large.root);
  await changeset.waitForLoaded();

  // A 1500-file change is the case this product exists for. Mounting every row on first paint
  // makes the list unusable, so it arrives in pages.
  //
  // Counting rows rather than naming the last file: git orders paths lexicographically, so
  // `file259.cs` sorts between `file25.cs` and `file26.cs` and is nowhere near the end.
  await expect(changeset.summary(files, files, 0)).toBeVisible();
  await expect(changeset.showingCount(200, files)).toBeVisible();
  await expect(app.page.getByRole('listitem')).toHaveCount(200);
  await expect(changeset.row('internal/file0.cs')).toBeVisible();
  await app.shot('the first page of a large changeset');

  await changeset.showMoreButton.click();

  await expect(app.page.getByRole('listitem')).toHaveCount(files);
  await expect(changeset.showMoreButton).toHaveCount(0);
  await expect(changeset.showingCount(200, files)).toHaveCount(0);
  await app.shot('the rest of a large changeset');

  // Attribution falls back to the manifest's own directory, which for a root `go.mod` is the
  // repository itself.
  await expect(
    changeset.row('internal/file0.cs').getByText(nameOf(large.root), { exact: true }),
  ).toBeVisible();
});

/**
 * Without git the application cannot do anything, and requirement 6 of Iteration 2 says it must
 * say so plainly rather than failing at the first repository.
 */
test('without git on PATH the application says so and refuses to pretend', async ({ diffhacker }) => {
  const app = await diffhacker.launch({ withoutGit: true });
  const { welcome } = screens(app.page);

  await expect(app.page.getByText(en.environment.gitMissingHeading)).toBeVisible();
  await expect(app.page.getByText(en.environment.gitMissingBody)).toBeVisible();

  // Not just a banner: the controls that would need git are disabled, so there is no way to
  // start something that cannot work.
  await expect(welcome.pathField).toBeDisabled();
  await expect(welcome.browseButton).toBeDisabled();
  await app.shot('git is missing');
});

function nameOf(path: string): string {
  const parts = path.split(/[\\/]/).filter(Boolean);
  return parts[parts.length - 1]!;
}
