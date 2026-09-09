# Iteration 9 — Explanations, hover cards, details panel

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | [8](iteration-08-graph-rendering.md) |
| **Blocks** | 10 |
| **Status** | Complete, apart from macOS and Linux and the one bar only a person can set — see **Where it stands** |

## Goal

Surface everything the LLM wrote, at the right moment.

## Where it stands

Every numbered requirement is implemented. The decisions taken along the way — the band that
replaced the left rail, the hover timings, the pinning gesture, where de-emphasis stops, the edge hit
areas and the two things jsdom cannot do — are recorded in
[docs/decisions.md](../decisions.md#explanations), and the answers to **Raise before implementing**
are with them.

Three things went beyond what the requirements asked for, all at the user's request after seeing it
running:

- **The left rail is gone.** The summary and risks sit across the top and the diagram has the full
  width. That is requirement 5's overview panel, relocated — see the decision for why it is also the
  right place given Iteration 10. The summary and risk column fold away as well, independently of
  the overview below them.
- **Clicking anything on the diagram keeps its card open** — a node, an edge, a cluster. Hovering
  alone was not enough: the card vanished while you were reaching for it. **This spends the gesture
  Iteration 10 was going to use for the diff viewer**, so that iteration needs a button on the card
  (its requirement 4 already puts the explanation and risks beside the diff, so the card is the
  natural place) or a double-click. Flagged rather than left to be discovered.
- **The colour scheme is selectable** — follow the system, light, or dark — from a control in the
  header. It followed the OS and only the OS before, which is a guess rather than a preference.

**Not verified: macOS and Linux.** CI is deliberately deferred and this machine is Windows, so
WebView2 is the only renderer any of this has run on — the same gap Iteration 8 reported. Two things
here are more likely than most to differ: Radix's collision handling, which decides that a card flips
rather than covers its node; and the clipboard, since `diffhacker://app` is a custom scheme and
whether a WebView treats it as a secure context is the host's business. `copyText` falls back to
`document.execCommand` for exactly that reason, and the end-to-end test reads the path back out of
the real clipboard so it proves whichever branch ran — but only on WebView2 so far.

**Not done by a checklist: verification step 1.** The MVP acceptance test is a person who has never
seen the change explaining it from the graph alone. That has not been run, and cannot be run by the
implementer — reported rather than assumed.

Verification steps 2–9 are covered: 2, 3 and 4 by
[08-explanations.spec.ts](../../tests/e2e/specs/08-explanations.spec.ts) in the real window, 5 three
times over (node, edge and cluster each assert a `risk-column`), 6 against the persisted document in
`AnalysisOverviewBand.test.tsx`, 7 as "nothing crosses the bridge while hovering" in
`AnalysisScreen.test.tsx`, and 8 and 9 in both suites.

## Context

Explanations are read on **hover**, not click — click is reserved for opening the file
(Iteration 10).

All content already exists in the persisted result; **nothing is generated here** (§0.2.8).

Risks are deliberately kept in their own column so they are never mixed into the description
of what changed. A reviewer scanning for danger should not have to read prose to find it.

## Fixed technical decisions

- **No LLM call may happen on hover. Ever.** Everything renders from the persisted analysis.
- Risks render in a **visually separate column or region** of the card, never inline with the
  explanation prose.

## Requirements

1. Hovering a **node** shows: what changed, why it changed, how it affects other parts,
   notable implementation details, and change statistics. Risks in the separate column.
2. Hovering an **edge** shows: what the relationship is, whether it is direct or conceptual,
   and how this change affects it.
3. Hovering a **container** shows the cluster-level explanation, with its risks in the separate
   column.
4. Hover cards are readable at any zoom, **do not obscure the element they describe**, handle
   long text without overflowing, and are **pinnable** so the user can scroll long content and
   select text.
5. A persistent **overview panel** containing: the overall summary, the recommended reading
   order, the container list with sizes, the aggregate statistics from
   [Iteration 7](iteration-07-analysis-pipeline.md), **all flagged risks collected in one
   place**, and run metadata — model, provider, tokens, cost, duration.
6. **Trivial changes are visibly de-emphasised** relative to substantive ones, based on the
   LLM's importance rank.
7. Every node offers a **copy-path** action.

## Out of scope

The diff viewer. Clicking a node does nothing yet, or does the minimum placeholder — Iteration
10 owns it.

## Done when

**A reviewer can understand the structure and intent of a large changeset from the graph
alone, without opening a single file.**

That is the bar. Test it on a person, not on a checklist.

## How to verify

1. Take a real 100+ file analysis and have someone who has never seen the change explain, from
   the graph and hover cards alone, what the change does and where the risk is. That is the
   MVP acceptance test.
2. Hover a node near each screen edge and at minimum and maximum zoom: the card stays on
   screen, stays legible, and never covers its own node.
3. Hover a node with a very long explanation: the card scrolls or clamps; it does not overflow
   the window.
4. Pin a card, scroll it, select and copy text from it, then unpin.
5. Confirm risks are visually separated on node, edge and container cards — three separate
   checks.
6. Confirm the overview panel's risk list contains **every** risk from the result: node, edge,
   container and overall. Assert it against the persisted document.
7. Open the network/provider log while hovering for a minute. Zero LLM calls.
8. Compare a top-ranked node and a bottom-ranked node side by side; the difference in emphasis
   should be obvious without reading.
9. Copy-path from a node and confirm the exact repository-relative path lands on the
   clipboard.

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **Hover timing and pinning interaction.** Delay before showing, behaviour on moving between
  adjacent nodes, and how a card is pinned (click is reserved for opening the file in
  Iteration 10, so pinning needs a different gesture). This is the interaction most likely to
  feel wrong, and it is unspecified.
- **Where "de-emphasised" stops.** Requirement 6 must not conflict with §0.2.5 — a trivial
  change must still be visible and reachable. Confirm the treatment: opacity, size, muted
  colour, or a combination, and what the floor is.
- **Overview panel placement and dismissibility**, given Iteration 10 adds a resizable diff
  panel that can expand to full width. Propose how the two coexist.
- **Cost display.** Requirement 5 shows cost in run metadata. If pricing was unavailable at run
  time (Iteration 4), confirm what is shown instead.
- **Edge hover targets.** Thin edges at low zoom are hard to hit, especially the faint
  cross-container ones. Propose the hit-area approach.
