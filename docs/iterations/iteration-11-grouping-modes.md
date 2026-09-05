# Iteration 11 — Grouping modes

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | [7](iteration-07-analysis-pipeline.md), [8](iteration-08-graph-rendering.md) |
| **Blocks** | — |
| **Status** | Not started |

## Goal

Let the user choose between dependency structure and thematic clustering.

## Context

Two groupings of the same change are both valuable and **they conflict**.

**Dependency mode** keeps a full change-dependency path intact even when it crosses concerns.
**Cluster mode** groups by theme, so a change touching database, auth and API splits across
three containers and the full dependency path is not shown.

The first is more useful and is the default. The second is a genuinely good complementary
view, not a consolation prize.

## Requirements

1. A user-selectable grouping mode:
   - **Dependency flow** (default) — containers follow direct and conceptual change
     dependencies, **preserving complete change paths**.
   - **Change clusters** — containers group by theme or concern, without preserving full
     dependency trees.
2. Switching modes **must not re-run the analysis** if the stored result can express both. If
   a second LLM pass is genuinely required, run it **lazily on first switch** and **persist
   it** so subsequent switches are instant.
3. The active mode is visible, with a **one-line explanation of what each shows**, so the user
   understands why the same change looks different.
4. **Both modes obey the completeness invariant** (§0.2.5).
5. Reading order and entry points are computed **per mode**.
6. **Reviewed markings are keyed to nodes, not to containers**, so they survive a mode switch.

## Out of scope

New node content. Both modes describe the same change with the same node explanations; only
the grouping, entry points and reading order differ.

## Done when

The user can toggle both groupings of one changeset and both are complete and coherent.

## How to verify

1. Run one analysis, switch modes back and forth, and assert programmatically that the node
   set is **identical** in both and equal to the changeset (§0.2.5).
2. Confirm dependency mode really does keep a cross-concern dependency path in one container —
   construct a fixture change that spans database, auth and API and check it.
3. Confirm cluster mode splits that same change across containers by theme.
4. Mark nodes reviewed in one mode, switch, and confirm the markings are all still there and
   correctly attributed.
5. Switch modes twice and confirm the second switch does not call the LLM — check the provider
   log, and check the persisted result grew rather than being regenerated.
6. Confirm each mode has its own valid entry points and reading order, and that both pass the
   Iteration 7 validation rules.
7. Read the one-line explanation cold: does it actually explain why the picture changed?

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **The central question: one result or two?** Requirement 2 hinges on whether the Iteration 7
  contract can carry both groupings. It cannot today. Decide **before writing code** between:
  (a) extending the `/schema` result so a single LLM pass produces both groupings, or (b) a
  lazy second pass producing a second grouping document. Option (a) raises the cost of every
  analysis including for users who never switch; option (b) means the first switch costs money
  and time. This is a user-visible economic trade-off, so it is the user's call.
- **Schema migration.** If the contract changes, existing persisted analyses were written
  against an older schema version. Confirm whether old analyses gain the new mode, show a
  single mode, or require re-analysis.
- **Does cluster mode have edges?** Requirement 1 says it does not preserve dependency trees,
  but it does not say whether edges are drawn at all, redrawn within clusters, or all become
  cross-container. This is a significant rendering decision.
- **Default per repository or global**, and whether the chosen mode is remembered per analysis
  (Iteration 12 remembers its toggle per analysis — confirm consistency).
- **Cost display for a lazy second pass** — the user should not be surprised by a charge from
  clicking a toggle.
