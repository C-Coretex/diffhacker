# Iteration 12 — Unchanged intermediate nodes

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | [8](iteration-08-graph-rendering.md), [10](iteration-10-diff-viewer.md) |
| **Blocks** | — |
| **Status** | Not started |

## Goal

Optionally reveal the files in between.

## Context

By default the graph shows only **changed** dependencies: if A depends on C through an
unchanged B, the graph draws A → C. Sometimes the reviewer needs to see B.

This is an **option, not the default**, and unchanged files must be unmistakably marked as not
part of the diff. The risk this iteration has to avoid is diluting the graph — a reviewer who
cannot tell at a glance which nodes are actually part of the change has lost the thing the
product gave them.

## Requirements

1. A toggle that expands relationships through unchanged intermediate files, turning A → C into
   A → B → C.
2. Unchanged nodes are **visually distinct and clearly labelled as not part of the changeset**.
   **No change statistics on them.**
3. Unchanged nodes are **explained by the LLM** — what they are and what role they play in the
   path — but are **excluded from** the reading order, from risk aggregation, and from
   reviewed/unreviewed tracking.
4. Expansion is **depth-limited**. If a path expands beyond a reasonable depth, **collapse it
   with an indicator** rather than drawing it.
5. Clicking an unchanged node opens **the file, not a diff**.
6. The toggle state is **remembered per analysis**.

## Out of scope

Any change to how changed files are grouped, ranked or explained. Unchanged nodes are an
overlay on the existing graph, not a re-analysis of it.

## Done when

The user can expand and collapse intermediate unchanged files without losing readability.

## How to verify

1. Build a fixture where A and C both change and depend on each other through an unchanged B.
   Toggle on: A → B → C. Toggle off: A → C. Both readable.
2. Confirm at a glance, from a screenshot, which nodes are in the changeset and which are not.
   If it takes a second look, the treatment is not distinct enough.
3. Confirm unchanged nodes carry an explanation but **no** +/− statistics.
4. Confirm the reading order, the aggregated risk list and the reviewed-progress totals are
   **unchanged** by toggling — assert the numbers, do not eyeball them.
5. Construct a path that exceeds the depth limit and confirm it collapses with a visible
   indicator rather than rendering.
6. Click an unchanged node: the file opens, not a diff.
7. Toggle on, restart the app, reopen the analysis: the toggle is still on. Open a *different*
   analysis: its own state applies.
8. Toggle on and off repeatedly at ~300 nodes and confirm layout stays stable and performant.

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **Where unchanged-node data comes from — this is the design question of the iteration.**
  Nothing in the Iteration 7 contract carries unchanged intermediates or their explanations,
  and §0.2.1 forbids the app computing dependencies itself. So either the analysis pass must
  produce them up front (paid for by every user, including those who never toggle), or the
  toggle triggers a lazy LLM pass (a charge from clicking a toggle). Decide this **before**
  writing code; it is the same trade-off as Iteration 11 requirement 2 and should probably be
  resolved the same way.
- **The depth limit number.** "Reasonable depth" is unspecified and directly controls both
  readability and cost.
- **Container membership for unchanged nodes.** Iteration 7 validation requires every node in
  exactly one container. An unchanged node bridging two containers has no obvious home.
  Confirm the rule: nearest container, its own container, or exemption from the validation
  rule.
- **Node state overlap.** §0.6 already defines an `unchanged-but-relevant` state on the node
  contract. Confirm whether these nodes reuse it or are a distinct concept, so the badge and
  border language stays coherent.
- **Interaction with grouping modes** ([Iteration 11](iteration-11-grouping-modes.md)) if that
  iteration is already built.
