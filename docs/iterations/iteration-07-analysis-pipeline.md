# Iteration 7 — Analysis pipeline and the graph result

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | [5](iteration-05-repository-toolbox.md), [6](iteration-06-repository-knowledge-base.md) |
| **Blocks** | 8, 9, 10, 11, 12, 13 |
| **Status** | Not started |

## Goal

Turn the changeset into a complete, validated, persisted graph result. **This is the central
iteration.**

## Context

The LLM receives the changed-file list, the project profile, the custom instructions and the
task instructions, then explores the repository through the toolbox and returns a full
description of the change: how it clusters, how it flows, what to read first, what each part
does, what is risky.

The application **validates and stores** that; it does not second-guess it (§0.2.1). Nothing
is shown to the user until the whole result exists (§0.2.8).

## Fixed technical decisions

- **Single pass.** One conversation produces the whole result, regardless of changeset size.
  Multi-pass strategies are deferred. If a changeset genuinely cannot fit, surface the
  **context-overflow condition from [Iteration 4](iteration-04-llm-provider-layer.md)** as an
  actionable error rather than silently truncating the file list.
- The result contract lives in `/schema` as JSON Schema and is **the same artifact** used for
  the LLM's structured output, the C# records and the TypeScript types.
- Persist to SQLite as a **versioned JSON document** with indexed columns for repository,
  model, created-at and schema version.
- Progress is driven by the LLM's `report_progress` calls, **combined with deterministic
  signals**: tool calls made, elapsed time, tokens consumed.

## Requirements

1. Define the result contract covering:
   - **Containers** — id, title, summary, explanation, risks, display order, member nodes.
   - **Nodes** — id (path-derived, stable), file path, optional in-file location as symbol
     name and/or line range, title, what changed, why it changed, how it affects other parts,
     notable implementation details, importance rank, states.
   - **Node states** — changed, added, deleted, unchanged-but-relevant, risky, entry point.
     **Not mutually exclusive.**
   - **Edges** — source, target, kind (`direct` | `conceptual`), explanation of the
     relationship and how the change affects it.
   - **Layout intent** — the entry node of each container, the rank of every other node within
     its container, and the ordering of containers. The renderer obeys this.
   - **Risks** — per node, per edge, per container, and overall. **Stored in fields separate
     from explanations, never merged into them.**
   - **Overall summary** — what this change does as a whole, in a few sentences.
   - **Reading order** — a recommended traversal across the whole changeset.
2. Build the prompt from the changed-file list with stats and metadata, the project profile,
   the user's custom instructions, and task instructions. **Never inject file contents or
   diffs** (§0.2.9).
3. Run the tool-calling loop until the LLM produces a complete result, then **validate**:
   - every changed file is represented by at least one node,
   - every node belongs to exactly one container,
   - every edge references existing nodes,
   - every container has exactly one entry node,
   - the graph's intended reading direction is unambiguous and cycles are reported.
4. On validation failure, feed the **specific** failure back to the LLM and let it repair the
   result. Cap repair rounds. **Never silently drop or invent data to make validation pass.**
   If repair exhausts, **fail loudly** and show the user what was wrong.
5. Persist the completed analysis with model, provider, token usage, cost, duration,
   timestamp and schema version. **Reopening never re-runs the LLM.**
6. Live progress: current phase from `report_progress`, tool calls made, tokens consumed,
   elapsed time. Cancellation at any point.
7. Retain the **full ordered tool-call trace** for the run.
8. Compute **deterministic statistics** alongside the LLM output: file and line counts,
   per-container size, status counts, languages, projects, highest fan-in and fan-out nodes,
   longest chain, risky-node count.
9. **The validation rules in requirement 3 are the most important tests in the project.** Test
   them against deliberately malformed output: missing files, dangling edges, containers with
   no entry node, duplicate node IDs, cycles.

## Out of scope

Rendering. The result of this iteration is a validated JSON document in SQLite and whatever
minimal UI proves it exists.

## Done when

An analysis of a real repository produces a validated, persisted result satisfying every
invariant, **on both a small and a very large changeset**.

## How to verify

1. Run against a ~20-file changeset and a ~500-file changeset. Both complete, both validate.
2. **Completeness (§0.2.5):** assert programmatically that the set of file paths in the
   changeset equals the set covered by nodes. Not a spot check — an assertion.
3. Feed the validator handcrafted malformed results, one per rule, and confirm each produces a
   *specific* diagnostic naming the offending id or path:
   - a changed file with no node,
   - a node in two containers, and a node in none,
   - an edge to a non-existent node,
   - a container with zero entry nodes, and one with two,
   - duplicate node IDs,
   - a cycle.
4. Confirm the repair loop feeds back the *specific* failure — inspect the repair message,
   not just that a retry happened.
5. Exhaust the repair cap and confirm the run fails loudly with the validation errors shown,
   and that **no partial or patched result was persisted as if it were valid**.
6. Grep the outbound prompt for file contents and diff hunks. Zero hits (§0.2.9).
7. Reopen a persisted analysis and confirm, from the provider request log, that **no LLM call
   was made**.
8. Cancel mid-run: clean unwind, partial usage reported, nothing persisted as complete.
9. Confirm node IDs are stable: run twice on the same changeset and compare the ID sets.
10. Confirm risks are in their own fields and never concatenated into explanation prose.

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **Repair round cap.** Requirement 4 says "cap repair rounds" without a number. Each round
  costs real money on a large changeset, so this is a user-visible economic decision.
- **Cycles: report or reject?** Requirement 3 says cycles are *reported* and the reading
  direction must be *unambiguous*. Those can conflict. Confirm whether a cycle fails
  validation, triggers repair, or is accepted and annotated for the renderer.
- **"Represented by at least one node" vs one-node-per-file (§0.6).** Confirm that a file
  yielding several nodes is allowed by validation and how the disambiguator in the node ID is
  formed, since Iterations 10–12 key reviewed-state off node IDs.
- **Deleted files and binary files as nodes.** The completeness invariant covers every changed
  file, including binaries the LLM cannot read. Confirm what the LLM is expected to say about
  them and that validation does not treat a thin node as a failure.
- **Importance rank scale.** Iteration 9 de-emphasises trivial changes based on it. Confirm
  whether it is an ordinal per container, a global rank, or a bounded score.
- **Where the analysis is triggered from in the UI**, and what the user sees while a run is in
  flight, given §0.2.8 forbids showing a partial graph.
