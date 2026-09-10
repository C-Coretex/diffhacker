# Iteration 10 — Diff viewer and navigation

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | [9](iteration-09-explanations.md) |
| **Blocks** | 12 |
| **Status** | Complete, apart from macOS and Linux and one deliberate omission — see **Where it stands** |

## Goal

Close the loop from "understand the change" to "review the code".

## Where it stands

All ten numbered requirements are implemented. The decisions taken along the way — how Monaco fits
inside the unchanged Content-Security-Policy, why double-clicking no longer zooms, why unchanged-region
folding is off for a node that names lines, where reviewed marks live, and how the external editor is
handed two paths — are in [docs/decisions.md](../decisions.md#reading-the-code), together with the
answers to **Raise before implementing**.

**The gesture.** Requirement 1's warning was taken at its word: Iteration 9 spent the single click on
keeping a hover card open, so `onNodeClick` still pins and the diff is opened another way. There are
three — a row of buttons **on the box** (open the diff, hand it to VS Code or Visual Studio, mark it
read), a **double-click** on the box, and the **Open the diff** button on the card. Pressing any
button on a box dismisses the pinned card first, because pressing it says the reading is over.

**A cluster opens whole.** Its title bar carries *Open every file*, and a double-click on the region
does the same: the panel lists every file in the cluster in the analysis's reading order, marks the
ones already read, and previous/next then walk that list rather than the whole change. Leaving the
queue is one button, and following an edge out of the cluster leaves it too.

**In the panel.** Previous and next are named buttons at the top rather than chevrons at the bottom,
because they are what a reviewer presses on every one of three hundred files. The explanation folds
away and stays folded across files. *Whole file* turns off Monaco's unchanged-region folding, since
"is this safe" is often a question about the code the diff did not touch. And full screen is a mode
of its own, entered and left by one button or by `Escape` — separate from the splitter, whose travel
still stops short of the left edge so that dragging never costs the reviewer sight of where they are.

**Deliberately not done: reviewed marks do not survive a re-analysis.** The marks live in schema 6's
`reviewed_json` column on the analysis row, which is the literal reading of "persisted with the
analysis"; a re-run writes a new row and therefore starts with nothing marked. Carrying them forward
would be behaviour the iteration did not ask for, so it is reported rather than smuggled in.
`SqliteAnalysisStoreTests.Marks_belong_to_one_analysis_and_do_not_leak_into_the_next_run` pins it, so
changing it later is a decision rather than a discovery.

**One thing beyond the requirements.** `changedFileFactsInfo` gained `previousPath`. Without it the
committed side of a renamed file is read from the new path, finds nothing, and every rename draws as
an addition — and requirement 8's "renamed file (both paths shown)" has nothing to show. It is pinned
in `AnalysisAgreementTests` with the other per-file facts.

**Measured, not assumed.** Requirement 9's threshold is one megabyte, above which the editor is given
plaintext with the minimap off and says so. In the end-to-end suite a ~1.7 MB file opens in about
180 ms; the host still refuses anything over five megabytes before it reaches the bridge.

**Not verified: macOS and Linux.** CI is deliberately deferred and this machine is Windows, so
WebView2 is the only renderer any of this has run on — the same gap Iterations 8 and 9 reported, and
a wider one here. Monaco's worker, its fonts and the Content-Security-Policy interaction have only
been exercised on Chromium; WKWebView and WebKitGTK need not agree about a classic worker served from
a custom scheme, and that is the first thing to check when those platforms are tried.

## Context

The reviewer reads the graph, reads the code, moves to the next node. **This is what makes the
app replace the alphabetical file list rather than supplement it.**

Graph traversal is not linear — several nodes can point at one node — so "next" cannot be a
single arrow. Forcing it into one would quietly reimpose the linear file list this product
exists to escape.

## Fixed technical decisions

- **Monaco `DiffEditor`**, bundled locally, no CDN (§0.2.13 and the CSP from Iteration 1).
- Presented in a **resizable side panel** that can expand to full width. **The graph stays
  visible by default.**
- **"Open in VS Code"** shells out to the `code` CLI with diff arguments. The external editor
  command is user-configurable.

## Requirements

1. Clicking a node opens the diff for that file with changed lines highlighted. When the node
   targets a specific in-file location, **scroll to and highlight it**.

   > **Read this before implementing requirement 1.** Iteration 9 spent the single click: clicking a
   > node, edge or cluster now keeps its hover card open, because hovering alone lost the card while
   > the reviewer was reaching for it. So opening the diff needs a different gesture. The card is the
   > obvious home for it — requirement 4 below already wants the explanation and risks beside the
   > diff, and they are on the card already — with a double-click as the alternative. Decide it
   > deliberately rather than quietly taking the click back.
2. Side-by-side and inline modes, syntax highlighting, expandable context around hunks,
   hunk-to-hunk navigation.
3. "Open in VS Code" button with diff enabled. **Handle VS Code not being installed.** Also add button "Open in VS" (visual studio) if possible
4. The node's **explanation and risks stay visible alongside the diff** — the reviewer should
   not return to the graph to remember why they are here.
5. Navigation that **follows the graph, not the file list**. Because a node can have several
   predecessors and successors, present the connected nodes as an **explicit labelled choice**
   rather than a single "next" button. Additionally support following the **recommended
   reading order** as a linear path.
6. The reviewer's **current position is always highlighted in the graph**.
7. **Reviewed / unreviewed marking** per node, toggled by the user, shown on the node box, with
   a per-container and overall progress indicator. **Persisted with the analysis.** This is
   what makes a 300-node review survivable.
8. Deleted, added, binary, renamed and untracked files all open sensibly.
9. Very large files do not freeze the UI.
10. Basic keyboard shortcuts where cheap — next node, open diff, mark reviewed, collapse
    container. **Nice-to-have; do not gold-plate** (§0.6).

## Out of scope

Grouping modes, unchanged intermediate nodes, cost reporting. Annotations and notes are in
[future improvements](../future-improvements.md) and stay there.

## Done when

A reviewer can complete a full review of a large changeset inside the app, moving node to node
along the graph and tracking what they have seen.

## How to verify

1. Do a real review of a real 100+ file changeset end to end, in the app, without opening
   another tool. Note every point where you wanted to leave.
2. Click a node with an in-file location: the diff opens **scrolled to and highlighting** that
   location, not at the top of the file.
3. Open each awkward file type and confirm each is sensible, not broken:
   - deleted file (no "after" side),
   - added and untracked file (no "before" side),
   - binary file (a clear statement, not a wall of bytes),
   - renamed file (both paths shown).
4. Open a multi-megabyte file: the UI stays responsive. Measure it.
5. From a node with three predecessors and two successors, confirm the navigation offers a
   labelled choice — and that each label says something useful about where it goes.
6. Follow the recommended reading order from start to finish as a linear path.
7. Mark 20 nodes reviewed, restart the app, reopen the analysis: all 20 are still marked and
   the progress indicators are correct.
8. Confirm reviewed state is keyed to **node IDs** — Iteration 11 requires it to survive a
   grouping-mode switch.
9. Uninstall / rename the `code` CLI and click "Open in VS Code": a clear message, no crash.
10. Confirm current position is visible in the graph at all times, including while the diff
    panel is expanded to full width.

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **Monaco bundling under the CSP.** Monaco ships web workers and dynamic imports and is the
  most likely dependency to fight the strict CSP and custom scheme from Iteration 1. If it
  needs a CSP relaxation, **stop and ask** — do not weaken the policy unilaterally.
- **Where reviewed state is persisted.** "Persisted with the analysis" could mean inside the
  analysis JSON document or in a side table keyed by analysis + node id. The second survives
  re-analysis better; the first is simpler and matches the export in Iteration 13. This affects
  Iterations 11, 12 and 13, so decide it deliberately.
- **What "sensibly" means for binary files** — a message, a hex view, or an external-open
  offer.
- **Large-file threshold and fallback.** Requirement 9 needs a number and a behaviour: no
  syntax highlighting, no diff computation, or a warning-and-confirm.
- **External editor command default and configuration UI**, including editors that are not VS
  Code, since the plan says the command is user-configurable.
- **Whether reviewed state should invalidate when the file changes on disk** after being marked
  — related to the stale-analysis detection in Iteration 13.
