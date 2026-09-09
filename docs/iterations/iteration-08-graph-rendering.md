# Iteration 8 — Graph rendering

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | [7](iteration-07-analysis-pipeline.md) |
| **Blocks** | 9, 10, 11, 12 |
| **Status** | Complete, apart from macOS and Linux — see **Where it stands** below |

## Goal

Draw the diagram.

## Where it stands

Every numbered requirement is implemented. The decisions taken along the way — the rank-to-ELK
mapping, the project palette, the node box, the collapsed container, the search scope and the shell
layout — are recorded in [docs/decisions.md](../decisions.md#the-diagram), and the answers to
**Raise before implementing** are with them.

**Not verified: step 10.** WKWebView and WebKitGTK were not exercised. CI is deliberately deferred
and this machine is Windows, so WebView2 is the only renderer the diagram has actually run on —
reported rather than assumed, consistent with how the repository already treats the macOS/Linux gap.
Fonts, borders and dashes are the things most likely to differ.

**Not measured: the frame rate in step 8.** Layout time and collapse time are measured and recorded;
frame rate while panning needs a real compositor and a hand on the mouse, so it is reported as
unmeasured. `onlyRenderVisibleElements` stays off, as the fixed decisions require.

Two things were fixed that Iteration 8 did not cause. `LlmBudget.MaxTotalTokens` disagreed with both
its own test and CLAUDE.md; and the end-to-end suite's stub provider could not speak server-sent
events, which had been failing every analysis and profile journey in it. Both are in
[docs/decisions.md](../decisions.md).

Requirements 14, 15 and 16 are not about the graph, and are described under
[The diagram](../decisions.md#the-diagram) and in the schema descriptions rather than here.

## Context

**The diagram is the product.** A directed graph of information-dense boxes, grouped into
containers, laid out top-down so the reviewer starts at the top of a container and walks
downstream.

The user arranges nothing. The LLM decided the ordering and the app respects it.

## Fixed technical decisions

- **React Flow parent/child nodes are containers.** Containers are a first-class concept in
  the library; do not build them by hand.
- **ELK.js `layered`** computes coordinates, in a **Web Worker** so relayout never blocks the
  UI. ELK is chosen over dagre specifically because it supports compound (nested) graphs.
- **The LLM decides hierarchy and ranking; ELK decides pixels.** Feed the LLM's entry node and
  ranks into ELK as layer and position constraints. **Never ask the LLM for coordinates.**
- Nodes are custom React components — plain JSX and Tailwind. **No canvas drawing code.**
- `onlyRenderVisibleElements` goes behind a feature flag, **off by default**. At the expected
  scale it is unnecessary; turn it on only if profiling demands it.
- **Do not introduce Cytoscape, Sigma or any WebGL renderer.** They trade rich HTML nodes for
  scale that is not needed.

## Requirements

1. Render **one diagram** containing all containers, each visually delimited and labelled with
   its title.
2. Node boxes show: file name, intelligently truncated path context, change stats (+/− lines),
   status, and a short summary line.
3. Directed edges with arrowheads, drawn top-down within containers, following the LLM's
   ranking, with the entry node at the top.
4. **Direct edges solid, conceptual edges dashed**, with a visible legend.
5. Node state visible via **border style plus a corner badge**: changed, added, deleted,
   unchanged-but-relevant, risky, entry point. **States can co-occur** (§0.6).
6. **Node fill colour encodes project/module**, with a legend.
7. Cross-container edges are drawn, **styled faintly, and excluded from layout influence**.
8. Containers are collapsible and expandable. **Individual nodes are not collapsible.**
9. Zoom, pan, fit-to-view and a minimap. **The user cannot move nodes.** There is no manual
   layout and nothing to persist.
10. Search box that finds a node by file name and focuses and highlights it.
11. Responsive and readable at ~300 nodes: **profile it and fix what is slow.**
12. **Snapshot-test the ELK layout output** for a fixed input graph so layout regressions are
    visible.
13. Add a search fields, which would find (highlight and show) node with this file name (if it's in the changeset)
14. Also show current context size in UI (during analysis) and on hover - statistics of what eats the context (reasoning, tools, messages...)
15. Also in the prompts ask LLM to limit its character count in descriptions of summary of changes for nodes and graphs. THe user doesn't need boilerplate of text, he needs short and concise summary of what it is and what does it do.
16. Also add context size (default + user override) the same as cost.

## Out of scope

Hover content and diffs — those are Iterations 9 and 10. A node here shows its summary line
and nothing more on interaction.

## Done when

A real analysis of a 100+ file changeset renders as a readable, navigable graph on all three
platforms.

## How to verify

1. Load a real 100+ file analysis. Read it cold: can you tell where to start in each container
   without being told? If not, the ranking is not reaching ELK correctly.
2. Confirm the entry node of every container is visually at the top of that container.
3. Confirm direct vs conceptual edges are distinguishable **at a glance and at low zoom**, and
   that the legend explains both.
4. Construct a node carrying three co-occurring states (changed + risky + entry point) and
   confirm all three read clearly at once.
5. Collapse and expand every container; confirm layout stays stable and edges reattach
   sensibly.
6. Confirm cross-container edges are faint and confirm — in the ELK input, not by eye — that
   they were excluded from layout.
7. Try to drag a node. Nothing moves.
8. Profile at ~300 nodes: measure initial layout time, frame rate while panning, and time to
   collapse a container. Record the numbers; fix what is slow rather than enabling
   `onlyRenderVisibleElements`.
9. Run the ELK snapshot test, change a layout constant, and confirm the snapshot fails.
10. Render on WebView2, WKWebView and WebKitGTK. Fonts, borders and dashes differ between
    them — check all three.

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **Mapping LLM ranks to ELK constraints.** ELK's layer-assignment and in-layer ordering
  options are the crux of this iteration. Propose the mapping before building it; if the LLM's
  rank cannot be expressed faithfully, that is worth surfacing rather than approximating
  quietly.
- **Project colour palette.** Fill colour encodes project/module, but the number of projects is
  unbounded and the palette must work in **both light and dark themes** and remain
  distinguishable for colour-blind users. Propose the palette and the overflow behaviour when
  projects outnumber colours.
- **Node box size.** Requirement 2 packs five pieces of information into a box that must stay
  readable at ~300 nodes. Propose the box design and the truncation rule for path context.
- **Collapsed container appearance** — what a collapsed container shows, and what happens to
  edges that crossed its boundary.
- **Search scope.** Requirement 10 says by file name. Confirm whether it should also match
  container titles and node titles, since the user will expect it to.
- **Where the graph lives in the app shell**, given Iteration 9 adds a persistent overview
  panel and Iteration 10 adds a resizable diff panel. Propose the layout once, for all three.
