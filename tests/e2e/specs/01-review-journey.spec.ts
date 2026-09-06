import type { Locator } from '@playwright/test';
import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { en, fill } from '../src/strings.ts';

/**
 * One journey through the whole application, as a reviewer would actually use it: land on the
 * welcome screen, open a repository, read the changeset, open a couple of diffs, change what the
 * list includes, visit settings, come back, and reopen the repository from the recent list.
 *
 * Deliberately one long test rather than a dozen short ones. Each step depends on the state the
 * previous step left behind, and relaunching the desktop application between assertions would
 * cost more than it proved.
 */
test('a reviewer can open a repository, read its changeset and open diffs', async ({
  diffhacker,
  repos,
}) => {
  const repo = repos.awkward();
  const app = await diffhacker.launch();
  const { welcome, repository, changeset, settings } = screens(app.page);

  // ---------------------------------------------------------------- welcome screen

  await expect(welcome.heading).toBeVisible();
  await expect(app.page.getByText(en.welcome.recentEmpty)).toBeVisible();
  await expect(welcome.pathField).toBeEnabled();
  await app.shot('welcome screen, nothing opened yet');

  // ---------------------------------------------------------------- open the repository

  await welcome.open(repo.root);

  await expect(changeset.heading).toBeVisible();
  await changeset.waitForLoaded();
  await expect(repository.changeButton).toBeVisible();

  // ---------------------------------------------------------------- the changed-file list

  // Seven files: the edited one, the staged addition, the deletion, the rename, the binary, the
  // untracked file, and the .gitignore itself is committed so it does not appear. The ignored
  // file must not be here at all.
  await expect(changeset.row('src/Web/edited.ts')).toBeVisible();
  await expect(changeset.row('src/Web/addedStaged.ts')).toBeVisible();
  await expect(changeset.row('removed.txt')).toBeVisible();
  await expect(changeset.row('docs/renamed.md')).toBeVisible();
  await expect(changeset.row('assets/logo.png')).toBeVisible();
  await expect(changeset.row('src/Web/brandNew.tsx')).toBeVisible();
  await expect(changeset.row('ignored.env')).toHaveCount(0);
  await app.shot('changed-file list');

  // Statuses, spelled by the resource layer.
  //
  // `exact` throughout, and not as decoration: "Added" is a case-insensitive substring of
  // "addedStaged.ts" and "New" of "brandNew.tsx", so a loose match resolves to both the badge
  // and the path and the assertion fails for a reason that has nothing to do with the product.
  await expect(status(changeset.row('src/Web/edited.ts'), en.changeset.status.modified)).toBeVisible();
  await expect(status(changeset.row('src/Web/addedStaged.ts'), en.changeset.status.added)).toBeVisible();
  await expect(status(changeset.row('removed.txt'), en.changeset.status.deleted)).toBeVisible();
  await expect(status(changeset.row('docs/renamed.md'), en.changeset.status.renamed)).toBeVisible();

  // A rename is a rename, with where it came from — not an unrelated delete plus add.
  await expect(
    changeset
      .row('docs/renamed.md')
      .getByText(fill(en.changeset.renamedFrom, { path: 'docs/original.md' }), { exact: true }),
  ).toBeVisible();
  await expect(changeset.row('docs/original.md')).toHaveCount(0);

  // Staged and unstaged edits to one file appear once, with both counted: one line rewritten
  // while staged and one appended after, so two added and one removed.
  await expect(changeset.row('src/Web/edited.ts')).toHaveCount(1);
  await expect(changeset.row('src/Web/edited.ts').getByText('+2', { exact: true })).toBeVisible();
  await expect(changeset.row('src/Web/edited.ts').getByText('−1', { exact: true })).toBeVisible();

  // A binary carries no invented line counts.
  const binaryRow = changeset.row('assets/logo.png');
  await expect(binaryRow.getByText(en.changeset.binary, { exact: true })).toBeVisible();
  await expect(binaryRow.getByText(en.changeset.noLineCounts, { exact: true })).toBeVisible();

  // Untracked files are flagged, and attributed to the nearest manifest above them rather than
  // to the repository root — `src/Web/package.json`, not the one at the top.
  const untrackedRow = changeset.row('src/Web/brandNew.tsx');
  await expect(untrackedRow.getByText(en.changeset.untracked, { exact: true })).toBeVisible();
  await expect(untrackedRow.getByText('Web', { exact: true })).toBeVisible();
  await expect(untrackedRow.getByText('TypeScript', { exact: true })).toBeVisible();

  // ---------------------------------------------------------------- a text diff

  const diff = await changeset.showDiff('src/Web/edited.ts');
  await expect(diff).toContainText('diff --git a/src/Web/edited.ts b/src/Web/edited.ts');
  await expect(diff).toContainText('-export const b = 2;');
  await expect(diff).toContainText('+export const staged = 2;');
  await expect(diff).toContainText('+export const unstaged = 4;');
  await app.shot('a text diff, expanded');

  // ---------------------------------------------------------------- an untracked file's diff

  // Requirement 2: an untracked file's whole content is the added side, even though git will not
  // diff a file it does not know about.
  const untrackedDiff = await changeset.showDiff('src/Web/brandNew.tsx');
  await expect(untrackedDiff).toContainText('--- /dev/null');
  await expect(untrackedDiff).toContainText('+export const New = () => null;');
  await app.shot('an untracked file diff, built from the file itself');

  // ---------------------------------------------------------------- a binary file's diff

  await binaryRow.getByRole('button', { name: en.changeset.diff.show }).click();
  await expect(binaryRow.getByText(en.changeset.diff.binary)).toBeVisible();
  await expect(binaryRow.locator('pre')).toHaveCount(0);
  await app.shot('a binary file states itself instead of dumping bytes');

  // ---------------------------------------------------------------- the untracked toggle

  const withUntracked = await changeset.row('src/Web/brandNew.tsx').count();
  expect(withUntracked).toBe(1);

  await expect(changeset.untrackedToggle).toBeChecked();
  await changeset.untrackedToggle.uncheck();
  await changeset.waitForLoaded();

  await expect(changeset.row('src/Web/brandNew.tsx')).toHaveCount(0);
  await expect(changeset.row('src/Web/edited.ts')).toBeVisible();
  await expect(changeset.row('ignored.env')).toHaveCount(0);
  await app.shot('untracked files excluded');

  await changeset.untrackedToggle.check();
  await changeset.waitForLoaded();
  await expect(changeset.row('src/Web/brandNew.tsx')).toBeVisible();

  // Refreshing is idempotent: the same working tree, the same list.
  await changeset.refreshButton.click();
  await changeset.waitForLoaded();
  await expect(changeset.row('src/Web/brandNew.tsx')).toBeVisible();

  // ---------------------------------------------------------------- settings, and back

  await settings.openButton.click();
  await expect(settings.heading).toBeVisible();
  await expect(settings.emptyState).toBeVisible();
  await app.shot('settings screen, no provider configured');

  await settings.backButton.click();

  // Coming back does not lose the changeset, and does not re-read the working tree.
  await expect(changeset.heading).toBeVisible();
  await expect(changeset.row('src/Web/edited.ts')).toBeVisible();

  // ---------------------------------------------------------------- the recent list

  await repository.changeButton.click();
  await expect(welcome.heading).toBeVisible();

  const entry = welcome.recentEntry(nameOf(repo.root));
  await expect(entry).toBeVisible();
  await app.shot('the repository is remembered');

  await entry.getByRole('button', { name: en.welcome.open, exact: true }).click();
  await changeset.waitForLoaded();
  await expect(changeset.row('src/Web/edited.ts')).toBeVisible();
  await app.shot('reopened from the recent list');
});

/** The status badge inside one row, matched exactly so it cannot collide with the file name. */
function status(row: Locator, label: string): Locator {
  return row.getByText(label, { exact: true });
}

function nameOf(path: string): string {
  const parts = path.split(/[\\/]/).filter(Boolean);
  return parts[parts.length - 1]!;
}
