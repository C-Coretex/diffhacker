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

  /**
   * Tests whatever is currently typed, saved or not. Ambiguous once a saved row's own test
   * button is also on screen — scope with `.first()`/a container when both are visible.
   */
  get testButton(): Locator {
    return this.page.getByRole('button', { name: en.providers.test });
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

  /** Iteration 8 requirement 16: the context window, overridden the same way a price is. */
  get contextWindowField(): Locator {
    return this.page.getByLabel(en.providers.contextWindowLabel);
  }

  /** The configurable runaway guards: how many tool calls and tokens a run on this profile may spend. */
  get maxToolCallsField(): Locator {
    return this.page.getByLabel(en.providers.maxToolCallsLabel);
  }

  get maxTotalTokensField(): Locator {
    return this.page.getByLabel(en.providers.maxTotalTokensLabel);
  }

  async addProvider(details: {
    name: string;
    model: string;
    apiKey: string;
    /** The optional price override. Only takes effect as a pair. */
    cost?: { input: string; output: string };
    /** The optional context-window override. Unlike a price, it stands alone. */
    contextWindow?: string;
    /** The optional tool-call budget override. Stands alone, like the context window. */
    maxToolCalls?: string;
    /** The optional token budget override. Stands alone, like the context window. */
    maxTotalTokens?: string;
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

    if (details.contextWindow) {
      await this.contextWindowField.fill(details.contextWindow);
    }

    if (details.maxToolCalls) {
      await this.maxToolCallsField.fill(details.maxToolCalls);
    }

    if (details.maxTotalTokens) {
      await this.maxTotalTokensField.fill(details.maxTotalTokens);
    }

    await this.saveButton.click();
  }

  // -------------------------------------------------- Iteration 10: the external editor commands

  get editorDiffCommandField(): Locator {
    return this.page.getByLabel(en.editors.diffLabel);
  }

  get editorOpenCommandField(): Locator {
    return this.page.getByLabel(en.editors.openLabel);
  }

  get saveEditorCommandsButton(): Locator {
    return this.page.getByRole('button', { name: en.editors.save, exact: true });
  }

  /** Configures the "anything else" editor, which is the one a test can point wherever it likes. */
  async setEditorCommands(diff: string, open: string): Promise<void> {
    await this.editorDiffCommandField.fill(diff);
    await this.editorOpenCommandField.fill(open);
    await this.saveEditorCommandsButton.click();
    await expect(this.page.getByText(en.editors.saved, { exact: true })).toBeVisible();
  }

  // ------------------------------------------------------ What an analysis asks for, by default

  /** The card holding the analysis defaults. */
  get analysisDefaults(): Locator {
    return this.page.getByTestId('analysis-defaults');
  }

  /**
   * One default part's checkbox. `part` is the kebab-case slug: `change-clusters`,
   * `implementation-groups`, `risks`, `node-explanations`, `edge-explanations` or
   * `container-explanations`.
   */
  analysisDefault(part: AnalysisPartSlug): Locator {
    return this.page.getByTestId(`default-option-${part}`);
  }

  /** One of the three verbosity buttons in the defaults. */
  analysisDefaultVerbosity(verbosity: 'brief' | 'medium' | 'detailed'): Locator {
    return this.page.getByTestId(`default-option-verbosity-${verbosity}`);
  }

  get saveAnalysisDefaultsButton(): Locator {
    return this.page.getByTestId('analysis-defaults-save');
  }

  /** Saves the defaults and waits for the host to have taken them. */
  async saveAnalysisDefaults(): Promise<void> {
    await this.saveAnalysisDefaultsButton.click();
    await expect(this.analysisDefaults.getByText(en.analysis.parts.saved, { exact: true })).toBeVisible();
  }
}

/** The parts an analysis can be told to skip, as their controls' test ids spell them. */
export type AnalysisPartSlug =
  | 'change-clusters'
  | 'implementation-groups'
  | 'risks'
  | 'node-explanations'
  | 'edge-explanations'
  | 'container-explanations';

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
    return this.page.getByRole('listitem').filter({ hasText: tool });
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

/**
 * The prompt a run shows when it hits a configured budget limit, asking whether to raise it and
 * keep going or stop. Shared between the analysis screen and the profile screen, so it is its own
 * class rather than duplicated on both.
 */
export class BudgetPromptDialog {
  constructor(private readonly page: Page) {}

  get dialog(): Locator {
    return this.page.getByTestId('budget-prompt-dialog');
  }

  get continueButton(): Locator {
    return this.page.getByTestId('budget-prompt-continue');
  }

  get stopButton(): Locator {
    return this.page.getByTestId('budget-prompt-stop');
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
    return this.page.getByRole('listitem').filter({ hasText: tool });
  }

  // ---------------------------------------------------------------- Iteration 8: the diagram

  /**
   * The long-form result Iteration 7 rendered on the page, which Iteration 8 folded behind a
   * disclosure — the diagram now says the same thing in a form that fits on one screen.
   */
  get detailsToggle(): Locator {
    return this.page.getByRole('button', { name: en.analysis.detailsHeading, exact: true });
  }

  /** One box on the diagram, addressed by the node id — which is the file path (§0.6). */
  graphNode(nodeId: string): Locator {
    return this.page.getByTestId(`graph-node-${nodeId}`);
  }

  graphContainer(containerId: string): Locator {
    return this.page.getByTestId(`graph-container-${containerId}`);
  }

  /**
   * A cluster's title bar — the only part of an expanded container that explains it.
   *
   * The region below is working canvas, and asking it what the cluster is about put a card over the
   * files the reviewer was reading.
   */
  graphContainerHeader(containerId: string): Locator {
    return this.page.getByTestId(`graph-container-header-${containerId}`);
  }

  get graphSearch(): Locator {
    return this.page.getByLabel(en.analysis.graph.searchLabel);
  }

  get collapseAllButton(): Locator {
    return this.page.getByRole('button', { name: en.analysis.graph.collapseAll, exact: true });
  }

  get expandAllButton(): Locator {
    return this.page.getByRole('button', { name: en.analysis.graph.expandAll, exact: true });
  }

  get legendButton(): Locator {
    return this.page.getByRole('button', { name: en.analysis.graph.legend, exact: true });
  }

  // -------------------------------------------------------------- the project filter

  get projectFilterButton(): Locator {
    return this.page.getByTestId('project-filter-trigger');
  }

  /** One project's checkbox in the open filter popover. */
  projectFilterItem(project: string): Locator {
    return this.page.getByTestId(`project-filter-item-${project}`);
  }

  /** "Show all", inside the popover — distinct from the banner's own copy of the same button. */
  get projectFilterShowAllButton(): Locator {
    return this.page.getByTestId('project-filter-show-all');
  }

  /** The standing warning that some projects are hidden right now. */
  get projectFilterBanner(): Locator {
    return this.page.getByTestId('project-filter-banner');
  }

  get projectFilterBannerShowAllButton(): Locator {
    return this.projectFilterBanner.getByTestId('project-filter-banner-show-all');
  }

  /** The live context meter, on the run panel. */
  get contextMeter(): Locator {
    return this.page.getByRole('button', { name: en.toolLog.contextLabel });
  }

  // ------------------------------------------------------ Iteration 11: grouping modes

  /**
   * The diagram's wrapper, which carries the grouping it is drawing. Asserted against rather than
   * the toolbar's prose, so "which picture is this?" has one answer that does not depend on copy.
   */
  get graphSurface(): Locator {
    return this.page.locator('[data-grouping-mode]');
  }

  /** One of the two grouping buttons. `mode` is the wire value, as the schema spells it. */
  groupingOption(mode: 'dependency_flow' | 'change_clusters'): Locator {
    return this.page.getByTestId(`grouping-${mode}`);
  }

  /** The one-line explanation of the grouping on screen — requirement 3. */
  get groupingExplanation(): Locator {
    return this.page.getByTestId('grouping-explanation');
  }

  /**
   * The run options beside the Analyse button: what the next run asks for. Its text summarises it —
   * the verbosity and how many parts are off — and `data-changed` says whether it differs from the
   * defaults set in Settings.
   */
  get runOptionsButton(): Locator {
    return this.page.getByTestId('run-options-button');
  }

  /** The open run-options popover. */
  get runOptions(): Locator {
    return this.page.getByTestId('run-options');
  }

  /** One part's checkbox in the run options. Only present while the popover is open. */
  runOption(part: AnalysisPartSlug): Locator {
    return this.page.getByTestId(`run-option-${part}`);
  }

  /** One of the three verbosity buttons in the run options. */
  runOptionVerbosity(verbosity: 'brief' | 'medium' | 'detailed'): Locator {
    return this.page.getByTestId(`run-option-verbosity-${verbosity}`);
  }

  /** Opens the run options, if they are not open already. */
  async openRunOptions(): Promise<void> {
    if (!(await this.runOptions.isVisible())) {
      await this.runOptionsButton.click();
    }

    await expect(this.runOptions).toBeVisible();
  }

  /** Closes the run options, so the Analyse button is not behind the popover. */
  async closeRunOptions(): Promise<void> {
    await this.page.keyboard.press('Escape');
    await expect(this.runOptions).toHaveCount(0);
  }

  /**
   * Sets one part for the next run only, through the popover, and closes it again. The defaults in
   * Settings are untouched — which is the difference the specs exist to prove.
   */
  async setRunOption(part: AnalysisPartSlug, on: boolean): Promise<void> {
    await this.openRunOptions();
    await this.runOption(part).setChecked(on);
    await this.closeRunOptions();
  }

  /** The provenance line's "Not asked for: …", present only when a run skipped a part. */
  get skippedParts(): Locator {
    return this.page.getByTestId('analysis-skipped-parts');
  }

  /** The toolbar's view-only toggle: draw each abstraction with its implementations as one box. */
  get mergeImplementationsToggle(): Locator {
    return this.page.getByTestId('merge-implementations');
  }

  /** The merged box drawn for an abstraction, named by the abstraction's node id. */
  mergedBox(abstractionNodeId: string): Locator {
    return this.page.getByTestId(`graph-merged-${abstractionNodeId}`);
  }

  // ------------------------------------ Run transparency, the library, and staleness

  /** One figure on the live strip: `phase`, `elapsed`, `tool-calls`, `turn`, `tokens` or `cost`. */
  runStat(name: 'phase' | 'elapsed' | 'tool-calls' | 'turn' | 'tokens' | 'cost'): Locator {
    return this.page.getByTestId(`run-${name}`);
  }

  /** The History button, which only exists once the repository has at least one stored run. */
  get historyButton(): Locator {
    return this.page.getByTestId('analysis-history-button');
  }

  /** The library's rows, most recent first, each carrying its analysis id. */
  get historyEntries(): Locator {
    return this.page.getByTestId('analysis-history-entry');
  }

  get confirmDeleteButton(): Locator {
    return this.page.getByRole('button', { name: en.analysis.library.deleteConfirm, exact: true });
  }

  /** Shown while an earlier run than the latest is on screen. */
  get earlierRunNotice(): Locator {
    return this.page.getByTestId('earlier-run-notice');
  }

  get openLatestButton(): Locator {
    return this.page.getByRole('button', { name: en.analysis.library.openLatest, exact: true });
  }

  /** The analysis surface, carrying what the last freshness check said: unchecked, fresh or stale. */
  get freshness(): Locator {
    return this.page.locator('[data-freshness]');
  }

  get staleBanner(): Locator {
    return this.page.getByTestId('stale-analysis-banner');
  }

  /** The tool-call inspector's section in the overview band. */
  get inspector(): Locator {
    return this.page.getByTestId('tool-call-inspector');
  }

  /** The inspector's rows, in the order the model made the calls. */
  get inspectorRows(): Locator {
    return this.inspector.getByTestId('tool-call-row');
  }

  /**
   * Tells the page its window came back into focus — what alt-tabbing back from an editor does.
   * Dispatched rather than performed, because the suite cannot move the operating system's focus
   * away from a window it is driving.
   */
  async refocus(): Promise<void> {
    await this.page.evaluate(() => window.dispatchEvent(new Event('focus')));
  }

  // ------------------------------------------------------- Iteration 9: explanations

  /** The band's toggle, which unfolds the rest of the overview. */
  get overviewToggle(): Locator {
    return this.page.getByRole('button', { name: en.analysis.overview.toggle, exact: true });
  }

  /** Folds the summary and the risk column away, leaving the strip and the risk count. */
  get summaryToggle(): Locator {
    return this.page.getByRole('button', { name: en.analysis.summaryHeading, exact: true });
  }

  get riskRegister(): Locator {
    return this.page.getByTestId('risk-register');
  }

  get readingOrder(): Locator {
    return this.page.getByTestId('reading-order');
  }

  get clusterList(): Locator {
    return this.page.getByTestId('cluster-list');
  }

  /** The one explanation card. There is never more than one on screen. */
  get hoverCard(): Locator {
    return this.page.getByTestId('graph-hover-card');
  }

  /** The risk column inside whichever card is showing — node, edge or cluster. */
  get hoverRisks(): Locator {
    return this.hoverCard.getByTestId('risk-column');
  }

  get closeCardButton(): Locator {
    return this.hoverCard.getByRole('button', { name: en.analysis.hover.close });
  }

  get copyPathButton(): Locator {
    return this.hoverCard.getByRole('button', { name: en.analysis.hover.copyPath });
  }

  /** Clicks something on the diagram and waits for the card it opens. */
  async clickForCard(element: Locator): Promise<Locator> {
    await element.click();
    await expect(this.hoverCard).toBeVisible();
    return this.hoverCard;
  }

  // --------------------------------------------------- Iteration 10: the diff and the review

  get diffPanel(): Locator {
    return this.page.getByTestId('diff-panel');
  }

  /** Monaco's host element. Present only when there is a text diff to draw. */
  get monaco(): Locator {
    return this.page.getByTestId('monaco-diff');
  }

  /** The statement shown instead of an editor: binary, too large, or absent on both sides. */
  get diffUnavailable(): Locator {
    return this.page.getByTestId('diff-unavailable');
  }

  get closeDiffButton(): Locator {
    return this.page.getByTestId('close-diff');
  }

  get toggleReviewedButton(): Locator {
    return this.page.getByTestId('toggle-reviewed');
  }

  get diffSplitter(): Locator {
    return this.page.getByTestId('diff-splitter');
  }

  /** The vertical divider between the code and the explanation strip under it. */
  get diffExplanationSplitter(): Locator {
    return this.page.getByTestId('diff-explanation-splitter');
  }

  /** The explanation strip's own resizable box, distinct from `nodeExplanation`'s prose inside it. */
  get diffExplanationPanel(): Locator {
    return this.page.getByTestId('diff-explanation-panel');
  }

  get degradedNotice(): Locator {
    return this.page.getByTestId('degraded-notice');
  }

  get nodeNavigator(): Locator {
    return this.page.getByTestId('node-navigator');
  }

  get predecessorChoices(): Locator {
    return this.page.getByTestId('predecessors').locator('li');
  }

  get successorChoices(): Locator {
    return this.page.getByTestId('successors').locator('li');
  }

  get toggleLayoutButton(): Locator {
    return this.page.getByTestId('toggle-layout');
  }

  get nextHunkButton(): Locator {
    return this.page.getByTestId('next-hunk');
  }

  get renamedFrom(): Locator {
    return this.page.getByTestId('renamed-from');
  }

  get nodeExplanation(): Locator {
    return this.diffPanel.getByTestId('node-explanation');
  }

  /** The region marker Monaco draws over the lines a node is about. */
  get highlightedRegion(): Locator {
    return this.monaco.locator('.diffhacker-node-region');
  }

  /** Monaco's own root, so a test can tell "the element exists" from "the editor started". */
  get monacoEditor(): Locator {
    return this.monaco.locator('.monaco-diff-editor');
  }

  /**
   * The one place an external editor's failure is reported.
   *
   * Over the workspace rather than inside the panel, because the buttons that ask for an editor are
   * on two surfaces now — the panel's header and every box on the diagram — and a 260-pixel box has
   * nowhere to put a sentence.
   */
  get editorError(): Locator {
    return this.page.getByTestId('editor-error');
  }

  get dismissEditorErrorButton(): Locator {
    return this.editorError.getByRole('button', { name: en.analysis.diff.dismissEditorError });
  }

  get readingOrderPosition(): Locator {
    return this.page.getByTestId('reading-order-position');
  }

  get readingOrderNext(): Locator {
    return this.page.getByTestId('reading-order-next');
  }

  get readingOrderPrevious(): Locator {
    return this.page.getByTestId('reading-order-previous');
  }

  get fullScreenButton(): Locator {
    return this.page.getByTestId('toggle-full-screen');
  }

  /** Folds and unfolds the unchanged runs Monaco hides by default. */
  get wholeFileButton(): Locator {
    return this.page.getByTestId('toggle-whole-file');
  }

  get explanationToggle(): Locator {
    return this.page.getByTestId('toggle-explanation');
  }

  /** The cluster the reviewer opened whole, listed in the panel. */
  get containerStrip(): Locator {
    return this.page.getByTestId('container-strip');
  }

  queueFile(nodeId: string): Locator {
    return this.page.getByTestId(`queue-${nodeId}`);
  }

  get leaveContainerButton(): Locator {
    return this.page.getByTestId('leave-container');
  }

  // ---- the controls drawn on a box, which appear when the pointer is on it

  nodeOpenDiffButton(nodeId: string): Locator {
    return this.page.getByTestId(`node-open-diff-${nodeId}`);
  }

  nodeReviewedButton(nodeId: string): Locator {
    return this.page.getByTestId(`node-toggle-reviewed-${nodeId}`);
  }

  nodeEditorButton(editor: string, nodeId: string): Locator {
    return this.page.getByTestId(`node-open-in-${editor}-${nodeId}`);
  }

  /** "Open every file", on a cluster's title bar. */
  containerOpenAllButton(containerId: string): Locator {
    return this.page.getByTestId(`container-open-all-${containerId}`);
  }

  /** One labelled choice in the navigation, addressed by where it goes. */
  neighbour(nodeId: string): Locator {
    return this.page.getByTestId(`neighbour-${nodeId}`);
  }

  /** The overall progress indicator on the band's strip; the first is the overall one. */
  get reviewProgress(): Locator {
    return this.page.getByTestId('review-progress').first();
  }

  get openInCustomEditorButton(): Locator {
    return this.page.getByTestId('open-in-custom');
  }

  get fitViewButton(): Locator {
    return this.page.getByRole('button', { name: en.analysis.graph.fitView, exact: true });
  }

  /**
   * Opens a node's diff the way the interface offers it: a double-click on its box.
   *
   * Fits the diagram first, and that is not a workaround for the test — it is a consequence of
   * requirement 6. Opening a file pans the diagram to centre it, so the box a reviewer wants *next*
   * may be off the visible canvas; a person zooms out or pans, and a test has to do the same. React
   * Flow moves the canvas by transform rather than by scrolling, so Playwright's own scroll-into-view
   * cannot do it.
   */
  async openDiff(nodeId: string): Promise<void> {
    await this.fitViewButton.click();
    await this.graphNode(nodeId).dblclick();
    await expect(this.diffPanel).toHaveAttribute('data-node-id', nodeId);
  }

  /**
   * Presses one of the buttons drawn on a box.
   *
   * They arrive with the pointer — three hundred permanent button rows would compete with the thing
   * the diagram is for — so the box is hovered first. That is what a person does; `opacity: 0` is
   * still "visible" to Playwright, but `pointer-events: none` is not clickable, and hovering is what
   * lifts both.
   */
  async pressOnBox(nodeId: string, button: Locator): Promise<void> {
    await this.fitViewButton.click();
    await this.graphNode(nodeId).hover();
    await button.click();
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
    budgetPrompt: new BudgetPromptDialog(page),
  };
}
