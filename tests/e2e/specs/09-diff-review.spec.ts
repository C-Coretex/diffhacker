import { expect, test } from '../src/fixtures.ts';
import { screens } from '../src/screens.ts';
import { StubProvider, stubReviewResult } from '../src/stubProvider.ts';
import { en, fill } from '../src/strings.ts';

/**
 * Reading the code — Iteration 10, and the point at which this application stops supplementing the
 * alphabetical file list and starts replacing it.
 *
 * Almost nothing here can be proved anywhere else. Monaco does not run under jsdom: it measures a
 * DOM that has no layout, so every unit test in `src/ui` mocks it and asserts what it was *asked*
 * for. Whether a real editor starts at all — inside a WebView, from a custom scheme, with a classic
 * worker, under a Content-Security-Policy that grants no `unsafe-eval` — is a question only a real
 * window can answer, and it is the single largest risk this iteration took.
 *
 * The rest is the review itself: opening each awkward kind of file, following the graph rather than
 * a list, and marking three hundred files' worth of progress in a way that survives a restart.
 *
 * The provider is a scripted endpoint on localhost, as everywhere else here.
 */

const apiKey = 'sk-e2e-diff-9c41b7d2ae06';

/** Where the fixture's node names a region rather than a whole file. */
const region = { path: 'src/cache.ts', startLine: 20, endLine: 24 };

test('a reviewer reads every kind of file, follows the graph, and their progress survives a restart', async ({
  diffhacker,
  repos,
}) => {
  const provider = await StubProvider.start();

  try {
    const repo = repos.reviewable();

    // The order the fan is built from: the first three point at the hub, the hub at the last two.
    // The hub is the modified file, which is the one with a real two-sided diff to read.
    const changed = [
      'src/brand-new.ts',
      'src/new-name.ts',
      'assets/logo.png',
      'src/cache.ts',
      'src/gone.ts',
      'src/huge.ts',
    ];

    let app = await diffhacker.launch();
    const { welcome, settings, analysis } = screens(app.page);

    await settings.openButton.click();
    await settings.addProvider({
      name: 'Stub provider',
      model: 'stub-model',
      apiKey,
      baseUrl: provider.baseUrl,
    });

    // A custom editor command pointing at an executable that does not exist. Verification step 9
    // asks for a clear message and no crash when the editor is missing, and a renamed `code` reaches
    // the host as exactly this.
    await settings.setEditorCommands(
      'diffhacker-no-such-editor --diff {left} {right}',
      'diffhacker-no-such-editor --goto {file}:{line}',
    );

    await settings.backButton.click();

    await welcome.open(repo.root);
    await analysis.openButton.click();

    provider.answers(stubReviewResult(changed, region));

    await analysis.runButton.click();
    await expect(analysis.rerunButton).toBeVisible({ timeout: 60_000 });

    for (const path of changed) {
      await expect(analysis.graphNode(path)).toBeVisible({ timeout: 30_000 });
    }

    await app.shot('the analysed change, before any file is opened');

    // ------------------------------------------------- requirement 1: a real diff, at the right line

    // Watches for a policy violation from here on. Iteration 10's largest open question was whether
    // Monaco could live inside the policy Iteration 1 set — it ships web workers, injects style
    // elements and, in some builds, evaluates code — and the instruction was to stop and ask rather
    // than weaken it. This is what turns "we did not have to" into something checked: the collector
    // is armed before any editor exists and read after several have been created and destroyed.
    await app.page.evaluate(() => {
      const violations: string[] = [];
      (window as unknown as { cspViolations: string[] }).cspViolations = violations;

      document.addEventListener('securitypolicyviolation', (event) => {
        violations.push(`${event.violatedDirective} ← ${event.blockedURI}`);
      });
    });

    // Double-click, not click: the single click is spent opening the explanation card, and this
    // iteration takes a different gesture rather than taking that one back.
    await analysis.openDiff('src/cache.ts');

    // Monaco started. Under a CSP with no 'unsafe-eval', from diffhacker://app, with a classic
    // worker — everything this iteration was told to stop and ask about if it did not hold.
    await expect(analysis.monaco).toBeVisible({ timeout: 30_000 });
    await expect(analysis.monacoEditor).toBeVisible();

    // Requirement 1's second half, and verification step 2: the node names lines 20 to 24, so the
    // editor opens there and marks them — not at the top of the file with the reviewer left to look.
    await expect(analysis.highlightedRegion.first()).toBeVisible({ timeout: 15_000 });

    // The line number beside the highlighted row is the node's, which is what "scrolled to" means.
    await expect(analysis.diffPanel).toContainText(
      fill(en.analysis.diff.region, { start: region.startLine, end: region.endLine }),
    );

    await app.shot('the diff, opened at the lines the node is about');

    // ------------------------------------------------- requirement 4: why the reviewer is here

    // The explanation and the risks travel with the code, so nothing sends them back to the graph
    // to remember what they were reading for.
    await expect(analysis.nodeExplanation).toBeVisible();
    await expect(analysis.diffPanel).toContainText('A line was added to src/cache.ts.');

    // ------------------------------------------------- requirement 2: the two modes and the hunks

    await expect(analysis.monaco).toHaveAttribute('data-side-by-side', 'true');
    await analysis.toggleLayoutButton.click();
    await expect(analysis.monaco).toHaveAttribute('data-side-by-side', 'false');
    await analysis.toggleLayoutButton.click();

    await analysis.nextHunkButton.click();
    await app.shot('inline and side-by-side, and a way between the changes');

    // ------------------------------------------------- requirement 5: a choice, not a next button

    // Three predecessors and two successors, each one labelled with where it goes rather than only
    // with a file name. This is verification step 5.
    await expect(analysis.nodeNavigator).toBeVisible();
    await expect(analysis.predecessorChoices).toHaveCount(3);
    await expect(analysis.successorChoices).toHaveCount(2);

    // The one that leaves the cluster says so, and carries the model's own account of the link.
    const aside = analysis.neighbour('src/huge.ts');
    await expect(aside).toContainText(
      fill(en.analysis.diff.crossesClusters, { container: 'The aside' }),
    );
    await expect(aside).toContainText('is the consequence of src/cache.ts changing shape');

    await app.shot('where to go next, as a labelled choice');

    // ------------------------------------------------- requirement 9: a very large file, measured

    const started = Date.now();
    await aside.click();
    await expect(analysis.diffPanel).toHaveAttribute('data-node-id', 'src/huge.ts');

    // Past a megabyte the viewer drops highlighting and says so. The point is not the notice — it is
    // that the window is still answering: the assertion below only resolves if it is.
    await expect(analysis.degradedNotice).toBeVisible({ timeout: 30_000 });
    await expect(analysis.monaco).toHaveAttribute('data-degraded', 'true');

    const elapsed = Date.now() - started;
    console.log(`[iteration 10] the large file opened in ${elapsed} ms`);

    // Generous, because this is a guard against a freeze rather than a benchmark: the number above
    // is what gets read, and this only fails if the interface stopped being usable.
    expect(elapsed).toBeLessThan(30_000);

    await app.shot('a file too large to highlight, opened anyway');

    // ------------------------------------------------- requirement 8: every awkward kind

    // Binary: a statement and the facts, never a wall of bytes.
    await analysis.openDiff('assets/logo.png');
    await expect(analysis.diffUnavailable).toHaveAttribute('data-kind', 'binary');
    await expect(analysis.diffUnavailable).toContainText(en.analysis.diff.binaryHeading);
    await expect(analysis.monaco).toHaveCount(0);
    await app.shot('a binary file, stated rather than dumped');

    // Deleted: no working-tree side, and the committed one still readable.
    await analysis.openDiff('src/gone.ts');
    await expect(analysis.monaco).toBeVisible();
    await expect(analysis.monaco).toContainText('doomed');

    // Added: no committed side.
    await analysis.openDiff('src/brand-new.ts');
    await expect(analysis.monaco).toBeVisible();
    await expect(analysis.monaco).toContainText('fresh');

    // Renamed: both paths shown, and the committed side read from the old one — a rename read from
    // the new path would find nothing and draw as an addition.
    await analysis.openDiff('src/new-name.ts');
    await expect(analysis.renamedFrom).toContainText('src/old-name.ts');
    await expect(analysis.monaco).toContainText('moved');
    await app.shot('a renamed file, with both of its paths');

    // ------------------------------------------------- requirement 3: the editor that is not there

    await expect(analysis.openInCustomEditorButton).toBeVisible();
    await analysis.openInCustomEditorButton.click();

    // Verification step 9: a clear message, no crash. The window is still here afterwards, which the
    // next assertion depends on.
    await expect(analysis.editorError).toContainText(en.error.editor_not_found);
    await app.shot('an editor that is not installed, said plainly');

    // ------------------------------------------------- the controls that live on the box itself

    // The same three things — read the diff, hand it to an editor, mark it read — are on the box, so
    // a reviewer who already knows which file they want never has to summon a card to reach a button.
    // They arrive with the pointer, which is why the box is hovered first.
    //
    // The message from the panel is dismissed first, so what appears next was produced by the button
    // on the box rather than left over from the one in the header.
    await analysis.dismissEditorErrorButton.click();
    await expect(analysis.editorError).toHaveCount(0);

    await analysis.pressOnBox('src/cache.ts', analysis.nodeEditorButton('custom', 'src/cache.ts'));
    await expect(analysis.editorError).toContainText(en.error.editor_not_found);
    await analysis.dismissEditorErrorButton.click();

    await analysis.pressOnBox('src/cache.ts', analysis.nodeReviewedButton('src/cache.ts'));
    await expect(analysis.graphNode('src/cache.ts')).toHaveAttribute('data-reviewed', 'true');

    // Put back: the reading-order walk below marks all six from the panel, and a mark left here would
    // make that walk assert a number it did not produce.
    await analysis.pressOnBox('src/cache.ts', analysis.nodeReviewedButton('src/cache.ts'));
    await expect(analysis.graphNode('src/cache.ts')).not.toHaveAttribute('data-reviewed');

    // Clicking a box opens its explanation card. Pressing a button *on* the box says the reading is
    // over — so the card goes, rather than landing on top of the panel that just opened.
    await analysis.fitViewButton.click();
    await analysis.clickForCard(analysis.graphNode('src/gone.ts'));
    await analysis.nodeOpenDiffButton('src/gone.ts').click();

    await expect(analysis.diffPanel).toHaveAttribute('data-node-id', 'src/gone.ts');

    // Off the diagram before the card is counted: opening a file re-centres the canvas, and a pointer
    // left where it was would be resting on whatever box slid under it.
    await app.page.mouse.move(4, 4);
    await expect(analysis.hoverCard).toHaveCount(0);
    await app.shot('the diff opened from the button on the box, with the card put away');

    // ------------------------------------------------- reading the code, not only the diff

    await analysis.openDiff('src/cache.ts');
    await expect(analysis.monaco).toBeVisible();

    // The node names lines 20–24, so the unchanged runs start unfolded — folding is precisely what
    // would hide the lines it points at.
    await expect(analysis.monaco).toHaveAttribute('data-hide-unchanged', 'false');

    await analysis.wholeFileButton.click();
    await expect(analysis.monaco).toHaveAttribute('data-hide-unchanged', 'true');
    await analysis.wholeFileButton.click();
    await expect(analysis.monaco).toHaveAttribute('data-hide-unchanged', 'false');

    // The explanation folds away and stays folded, because giving the code the height is a statement
    // about how this reviewer reads rather than about one file.
    await expect(analysis.nodeExplanation).toBeVisible();
    await analysis.explanationToggle.click();
    await expect(analysis.nodeExplanation).toHaveCount(0);

    await analysis.openDiff('src/new-name.ts');
    await expect(analysis.nodeExplanation).toHaveCount(0);

    await analysis.explanationToggle.click();
    await expect(analysis.nodeExplanation).toBeVisible();

    // ------------------------------------------------- the explanation, given more room by dragging

    // Trading code height for explanation height is the whole point of a resizable divider between
    // them: dragging it up should grow the explanation's own box and shrink Monaco's by roughly the
    // same amount, not just move a line that does nothing.
    const explanationBefore = (await analysis.diffExplanationPanel.boundingBox())!;
    const monacoBefore = (await analysis.monaco.boundingBox())!;
    const handle = (await analysis.diffExplanationSplitter.boundingBox())!;

    await app.page.mouse.move(handle.x + handle.width / 2, handle.y + handle.height / 2);
    await app.page.mouse.down();
    await app.page.mouse.move(handle.x + handle.width / 2, handle.y - 120, { steps: 8 });
    await app.page.mouse.up();

    const explanationAfter = (await analysis.diffExplanationPanel.boundingBox())!;
    const monacoAfter = (await analysis.monaco.boundingBox())!;

    expect(explanationAfter.height).toBeGreaterThan(explanationBefore.height + 60);
    expect(monacoAfter.height).toBeLessThan(monacoBefore.height - 60);

    await app.shot('the explanation, given more room by dragging');

    // Full screen, and the way back out. The diagram is hidden rather than thrown away, so the box
    // that was current is still current when it returns.
    await analysis.fullScreenButton.click();
    await expect(analysis.diffPanel).toHaveAttribute('data-full-screen', 'true');
    await expect(analysis.diffSplitter).toHaveCount(0);
    await expect(analysis.graphNode('src/new-name.ts')).toBeHidden();
    await app.shot('the diff, with the window to itself');

    await analysis.fullScreenButton.click();
    await expect(analysis.diffPanel).toHaveAttribute('data-full-screen', 'false');
    await expect(analysis.graphNode('src/new-name.ts')).toHaveAttribute('data-current', 'true');

    // ------------------------------------------------- a cluster, opened whole

    // A cluster is the unit a reviewer actually reads — it is what the model decided belongs together
    // — so it opens as a list of its files, in the analysis's own reading order.
    await analysis.fitViewButton.click();
    await analysis.containerOpenAllButton('the-change').click();

    await expect(analysis.containerStrip).toBeVisible();
    await expect(analysis.containerStrip.locator('li')).toHaveCount(5);
    await expect(analysis.diffPanel).toHaveAttribute('data-node-id', 'src/brand-new.ts');

    // Five in the cluster, not six in the change: "next" now means the next file of what was opened.
    await expect(analysis.readingOrderPosition).toContainText(
      fill(en.analysis.diff.readingOrderPosition, { position: 1, total: 5 }),
    );

    await analysis.queueFile('src/cache.ts').click();
    await expect(analysis.diffPanel).toHaveAttribute('data-node-id', 'src/cache.ts');
    await app.shot('a whole cluster, opened as a list');

    // And it can be put down without closing the file being read.
    await analysis.leaveContainerButton.click();
    await expect(analysis.containerStrip).toHaveCount(0);
    await expect(analysis.diffPanel).toHaveAttribute('data-node-id', 'src/cache.ts');

    // ------------------------------------------------- requirement 6: position, always visible

    await analysis.openDiff('src/cache.ts');
    await expect(analysis.graphNode('src/cache.ts')).toHaveAttribute('data-current', 'true');

    // Dragged as far as it goes. "Full width" stops short of covering the diagram, because the
    // reviewer's position has to stay visible in the graph at all times — verification step 10.
    const bounds = (await analysis.diffSplitter.boundingBox())!;
    await app.page.mouse.move(bounds.x + 2, bounds.y + bounds.height / 2);
    await app.page.mouse.down();
    await app.page.mouse.move(0, bounds.y + bounds.height / 2, { steps: 10 });
    await app.page.mouse.up();

    await expect(analysis.graphNode('src/cache.ts')).toBeVisible();
    await expect(analysis.graphNode('src/cache.ts')).toHaveAttribute('data-current', 'true');
    await app.shot('the panel at its widest, with the current node still on the diagram');

    // Back to something readable for the rest of the journey.
    await app.page.mouse.move(bounds.x + 2, bounds.y + bounds.height / 2);
    await app.page.mouse.down();
    await app.page.mouse.move(bounds.x, bounds.y + bounds.height / 2, { steps: 10 });
    await app.page.mouse.up();

    // ------------------------------------------------- requirement 5 again: the linear path

    await expect(analysis.readingOrderPosition).toContainText(
      fill(en.analysis.diff.readingOrderPosition, { position: 4, total: 6 }),
    );

    await analysis.readingOrderNext.click();
    await expect(analysis.diffPanel).toHaveAttribute('data-node-id', 'src/gone.ts');

    // ------------------------------------------------- requirement 7: marking, and surviving

    // Walked as the linear path rather than by picking boxes off the diagram, which is verification
    // step 6 — "follow the recommended reading order from start to finish" — and step 7 in the same
    // pass. The order the model gave is the order these arrive in, and every one of them is marked
    // on the way through, which is what a real review of three hundred files looks like.
    await analysis.openDiff(changed[0]!);

    for (const [index, path] of changed.entries()) {
      await expect(analysis.diffPanel).toHaveAttribute('data-node-id', path);

      await expect(analysis.readingOrderPosition).toContainText(
        fill(en.analysis.diff.readingOrderPosition, { position: index + 1, total: changed.length }),
      );

      await analysis.toggleReviewedButton.click();
      await expect(analysis.graphNode(path)).toHaveAttribute('data-reviewed', 'true');

      if (index < changed.length - 1) {
        await analysis.readingOrderNext.click();
      }
    }

    // The end of the path, and it says so rather than wrapping round to the start.
    await expect(analysis.readingOrderNext).toBeDisabled();

    await expect(analysis.reviewProgress).toContainText(
      fill(en.analysis.diff.reviewedProgress, { reviewed: 6, total: 6 }),
    );

    await app.shot('every file marked reviewed');

    // ------------------------------------------------- Monaco lived inside the policy, unchanged

    // Read after six editors have been created and destroyed, one per file. §0.2.13 is enforced by
    // this policy and by nothing else, so an iteration that quietly needed a hole in it would show
    // up here rather than in a review months later.
    const violations = await app.page.evaluate(
      () => (window as unknown as { cspViolations: string[] }).cspViolations,
    );

    expect(
      violations,
      'Monaco provoked a Content-Security-Policy violation. The policy is not to be widened to '
        + 'suit it — stop and ask (iteration 10, "Raise before implementing").',
    ).toEqual([]);

    // Verification step 7: restart, reopen, and find every mark still there. The marks live on the
    // stored analysis, so this is reading them back off disk rather than out of a page that never
    // went away.
    const root = await app.stop();
    app = await diffhacker.launch({ root });

    const reopened = screens(app.page);
    await reopened.welcome.open(repo.root);
    await reopened.analysis.openButton.click();

    await expect(reopened.analysis.graphNode('src/cache.ts')).toBeVisible({ timeout: 30_000 });

    for (const path of changed) {
      await expect(reopened.analysis.graphNode(path)).toHaveAttribute('data-reviewed', 'true');
    }

    await expect(reopened.analysis.reviewProgress).toContainText(
      fill(en.analysis.diff.reviewedProgress, { reviewed: 6, total: 6 }),
    );

    // And unmarking is not one-way: a reviewer who marked something by mistake can take it back,
    // and that has to reach the database as well.
    await reopened.analysis.openDiff(changed[0]!);
    await reopened.analysis.readingOrderNext.click();
    await reopened.analysis.readingOrderNext.click();
    await reopened.analysis.readingOrderNext.click();
    await expect(reopened.analysis.diffPanel).toHaveAttribute('data-node-id', 'src/cache.ts');
    await reopened.analysis.toggleReviewedButton.click();

    await expect(reopened.analysis.graphNode('src/cache.ts')).not.toHaveAttribute('data-reviewed');
    await expect(reopened.analysis.reviewProgress).toContainText(
      fill(en.analysis.diff.reviewedProgress, { reviewed: 5, total: 6 }),
    );

    await app.shot('the marks, after a restart');
  } finally {
    await provider.stop();
  }
});
