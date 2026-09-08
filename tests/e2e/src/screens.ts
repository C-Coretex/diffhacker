import { expect, type Locator, type Page } from '@playwright/test';
import { en, fill } from './strings.ts';

/**
 * Locators for the application's screens.
 *
 * Roles and rendered resource strings, never test ids — the same convention as the component
 * tests. If a control cannot be found by its accessible name, that is worth knowing about.
 *
 * Two traps to be aware of when adding to this file. `getByText` matches substrings by default,
 * and the catalogue has real overlaps — "Uncommitted changes" is also inside the welcome card's
 * description, so a loose match there silently resolves on the wrong screen. And the card titles
 * are `<div>`s rather than headings, so `getByRole('heading')` only works for the panels that use
 * a real `<h2>`.
 */

export class WelcomeScreen {
  constructor(private readonly page: Page) {}

  get heading(): Locator {
    // A card title, not a heading element.
    return this.page.getByText(en.welcome.heading, { exact: true });
  }

  get pathField(): Locator {
    return this.page.getByLabel(en.welcome.pathLabel);
  }

  /**
   * Scoped to the form. Once a repository has been opened once, the recent list renders its own
   * "Open" button and an unscoped match resolves to both.
   */
  get openButton(): Locator {
    return this.page.locator('form').getByRole('button', { name: en.welcome.open, exact: true });
  }

  get browseButton(): Locator {
    return this.page.getByRole('button', { name: en.welcome.browse });
  }

  get error(): Locator {
    return this.page.getByRole('alert');
  }

  recentEntry(name: string): Locator {
    return this.page
      .getByRole('listitem')
      .filter({ has: this.page.getByText(name, { exact: true }) });
  }

  /** Types a path and opens it. Does not assert the outcome — some callers expect a rejection. */
  async open(path: string): Promise<void> {
    await expect(this.heading).toBeVisible();
    await this.pathField.fill(path);
    await this.openButton.click();
  }
}

export class RepositoryScreen {
  constructor(private readonly page: Page) {}

  get changeButton(): Locator {
    return this.page.getByRole('button', { name: en.repository.change });
  }

  get noCommitsWarning(): Locator {
    return this.page.getByText(en.repository.noCommits);
  }

  get normalizedNotice(): Locator {
    // The notice names the resolved path, so match the stable half of the sentence.
    return this.page.getByText(en.welcome.normalized.split('{')[0]!.trim(), { exact: false });
  }

  title(name: string): Locator {
    return this.page.getByText(name, { exact: true });
  }
}

export class ChangesetPanel {
  constructor(private readonly page: Page) {}

  get heading(): Locator {
    return this.page.getByRole('heading', { name: en.changeset.heading });
  }

  get untrackedToggle(): Locator {
    return this.page.getByLabel(en.changeset.includeUntracked);
  }

  get refreshButton(): Locator {
    return this.page.getByRole('button', { name: en.changeset.refresh });
  }

  get cleanHeading(): Locator {
    return this.page.getByText(en.changeset.cleanHeading);
  }

  get noCommitsNotice(): Locator {
    return this.page.getByText(en.changeset.noCommitsNotice);
  }

  get showMoreButton(): Locator {
    // The label carries a count, so match the stable prefix.
    return this.page.getByRole('button', { name: /^Show \d+ more$/ });
  }

  get error(): Locator {
    return this.page.getByRole('alert');
  }

  summary(files: number, added: number, removed: number): Locator {
    return this.page.getByText(
      fill(en.changeset.summary, { files, added, removed }),
      { exact: true },
    );
  }

  showingCount(shown: number, total: number): Locator {
    return this.page.getByText(fill(en.changeset.showingCount, { shown, total }), { exact: true });
  }

  /** The row for one repository-relative path. */
  row(path: string): Locator {
    return this.page
      .getByRole('listitem')
      .filter({ has: this.page.getByText(path, { exact: true }) });
  }

  /** Waits until the list has loaded, whatever it turned out to contain. */
  async waitForLoaded(): Promise<void> {
    await expect(this.heading).toBeVisible();
    await expect(this.page.getByText(en.changeset.loading)).toBeHidden();
  }

  /** Expands a row's diff and returns the `<pre>` holding it. */
  async showDiff(path: string): Promise<Locator> {
    const row = this.row(path);
    await row.getByRole('button', { name: en.changeset.diff.show }).click();
    await expect(row.getByRole('button', { name: en.changeset.diff.hide })).toBeVisible();
    await expect(row.getByText(en.changeset.diff.loading)).toBeHidden();
    return row.locator('pre');
  }
}

export class SettingsScreen {
  constructor(private readonly page: Page) {}

  get heading(): Locator {
    // A card title, not a heading element.
    return this.page.getByText(en.providers.heading, { exact: true });
  }

  get openButton(): Locator {
    return this.page.getByRole('button', { name: en.app.nav.settings });
  }

  get backButton(): Locator {
    return this.page.getByRole('button', { name: en.app.nav.back });
  }

  get addButton(): Locator {
    return this.page.getByRole('button', { name: en.providers.add });
  }

  get emptyState(): Locator {
    return this.page.getByText(en.providers.empty);
  }

  get nameField(): Locator {
    return this.page.getByLabel(en.providers.nameLabel);
  }

  get modelField(): Locator {
    return this.page.getByLabel(en.providers.modelLabel);
  }

  get apiKeyField(): Locator {
    return this.page.getByLabel(en.providers.apiKeyLabel);
  }

  /** The label gains "(optional)" for providers that have a default endpoint. */
  get baseUrlField(): Locator {
    return this.page.getByLabel(/^Base URL/);
  }

  get typeField(): Locator {
    return this.page.getByLabel(en.providers.typeLabel);
  }

  get saveButton(): Locator {
    return this.page.getByRole('button', { name: en.providers.save, exact: true });
  }

  get error(): Locator {
    return this.page.getByRole('alert');
  }

  profile(name: string): Locator {
    return this.page
      .getByRole('listitem')
      .filter({ has: this.page.getByText(name, { exact: true }) });
  }

  get inputCostField(): Locator {
    return this.page.getByLabel(en.providers.inputCostLabel);
  }

  get outputCostField(): Locator {
    return this.page.getByLabel(en.providers.outputCostLabel);
  }

  async addProvider(details: {
    name: string;
    model: string;
    apiKey: string;
    /** The optional price override. Only takes effect as a pair. */
    cost?: { input: string; output: string };
    /** Set to point at an OpenAI-compatible endpoint, which is how the stub provider is used. */
    baseUrl?: string;
  }): Promise<void> {
    await this.addButton.click();

    if (details.baseUrl) {
      await this.typeField.selectOption('openai_compatible');
      await this.baseUrlField.fill(details.baseUrl);
    }

    await this.nameField.fill(details.name);
    await this.modelField.fill(details.model);
    await this.apiKeyField.fill(details.apiKey);

    if (details.cost) {
      await this.inputCostField.fill(details.cost.input);
      await this.outputCostField.fill(details.cost.output);
    }

    await this.saveButton.click();
  }
}

export class ProfileScreen {
  constructor(private readonly page: Page) {}

  get openButton(): Locator {
    return this.page.getByRole('button', { name: en.app.nav.profile });
  }

  get backButton(): Locator {
    return this.page.getByRole('button', { name: en.app.nav.back });
  }

  get heading(): Locator {
    // A card title, not a heading element.
    return this.page.getByText(en.profile.heading, { exact: true });
  }

  get missingNotice(): Locator {
    return this.page.getByText(en.profile.missingHeading, { exact: true });
  }

  get generateButton(): Locator {
    return this.page.getByRole('button', { name: en.profile.generate, exact: true });
  }

  get regenerateButton(): Locator {
    return this.page.getByRole('button', { name: en.profile.regenerate, exact: true });
  }

  get stopButton(): Locator {
    return this.page.getByRole('button', { name: en.profile.cancel, exact: true });
  }

  get runHeading(): Locator {
    return this.page.getByText(en.toolLog.heading, { exact: true });
  }

  get purposeField(): Locator {
    return this.page.getByLabel(en.profile.purpose);
  }

  get notesField(): Locator {
    return this.page.getByLabel(en.profile.userNotes);
  }

  get instructionsField(): Locator {
    return this.page.getByLabel(en.profile.customInstructions);
  }

  get driftWarning(): Locator {
    return this.page.getByText(en.profile.driftHeading, { exact: true });
  }

  /** The tool log's row for one tool. Several calls to the same tool give several rows. */
  toolRow(tool: string): Locator {
    return this.page.getByRole('row').filter({ hasText: tool });
  }

  /** Scoped: both cards on this screen have a Save button. */
  saveIn(section: 'generated' | 'yours'): Locator {
    const title = section === 'generated' ? en.profile.generatedSections : en.profile.userSections;

    return this.page
      .locator('[data-slot="card"]')
      .filter({ has: this.page.getByText(title, { exact: true }) })
      .getByRole('button', { name: en.profile.save, exact: true });
  }
}

export class DocumentationPanel {
  constructor(private readonly page: Page) {}

  get previewButton(): Locator {
    return this.page.getByRole('button', { name: en.documentation.preview });
  }

  get dialog(): Locator {
    return this.page.getByRole('alertdialog');
  }

  get writeButton(): Locator {
    return this.dialog.getByRole('button', { name: en.documentation.write });
  }

  get cancelButton(): Locator {
    return this.dialog.getByRole('button', { name: en.documentation.cancel });
  }

  get targetSelect(): Locator {
    return this.page.getByLabel(en.documentation.targetLabel);
  }

  file(path: string): Locator {
    return this.dialog.getByText(path, { exact: true });
  }
}

/** Every screen for one page, so a spec reads as a journey rather than as selectors. */

/** The analysis screen: one run over the working tree, and the result it produced. */
export class AnalysisScreen {
  constructor(private readonly page: Page) {}

  get openButton(): Locator {
    return this.page.getByRole('button', { name: en.app.nav.analysis, exact: true });
  }

  get backButton(): Locator {
    return this.page.getByRole('button', { name: en.app.nav.back });
  }

  get heading(): Locator {
    // A card title, not a heading element.
    return this.page.getByText(en.analysis.heading, { exact: true });
  }

  get emptyNotice(): Locator {
    return this.page.getByText(en.analysis.emptyHeading, { exact: true });
  }

  get runButton(): Locator {
    return this.page.getByRole('button', { name: en.analysis.run, exact: true });
  }

  get rerunButton(): Locator {
    return this.page.getByRole('button', { name: en.analysis.rerun, exact: true });
  }

  get stopButton(): Locator {
    return this.page.getByRole('button', { name: en.analysis.cancel, exact: true });
  }

  get runHeading(): Locator {
    return this.page.getByText(en.toolLog.heading, { exact: true });
  }

  get summaryHeading(): Locator {
    return this.page.getByText(en.analysis.summaryHeading, { exact: true });
  }

  get entryBadge(): Locator {
    return this.page.getByText(en.analysis.entryNode, { exact: true });
  }

  /** The tool log's row for one tool. Several calls to the same tool give several rows. */
  toolRow(tool: string): Locator {
    return this.page.getByRole('row').filter({ hasText: tool });
  }
}

export function screens(page: Page) {
  return {
    welcome: new WelcomeScreen(page),
    repository: new RepositoryScreen(page),
    changeset: new ChangesetPanel(page),
    settings: new SettingsScreen(page),
    profile: new ProfileScreen(page),
    analysis: new AnalysisScreen(page),
    documentation: new DocumentationPanel(page),
  };
}
