# Iteration 13 — Cost, transparency, analysis library

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | [7](iteration-07-analysis-pipeline.md) |
| **Blocks** | — |
| **Status** | Not started |

## Goal

Make expensive runs predictable and repeatable.

## Context

An analysis of a large changeset against a frontier model is a **real, visible cost**. A user
who does not know what a run will cost will hesitate before every run, and a product people
hesitate to use is a product they stop using.

Users also need to **trust** the output, which means being able to inspect how it was
produced.

## Requirements

1. **Pre-run estimate**: file count, approximate input size, estimated cost range for the
   selected model, shown **before** the run starts.
2. **Live run view**: phase from `report_progress`, elapsed time, tool calls executed, tokens
   consumed, running cost.
3. **Tool-call inspector** — the full ordered trace of what the LLM requested and how large
   each response was.
4. **Analysis library per repository**: previous runs listed with model, cost, date and
   changeset size. Reopen any **instantly** without re-running.
5. **Re-run with a different model or provider**, keeping both results for comparison.
6. **Detect stale analyses** — the working tree has moved since the run — and prompt for
   re-analysis rather than silently showing outdated data.
7. **Configurable limits**: max tool calls per run, max tokens per run, hard stop with a clear
   **partial-result explanation** when exceeded.
8. **Export and import** an analysis result as a file, so a reviewer can hand their analysis to
   a colleague.

## Out of scope

Changing how analysis works. This iteration observes, records and replays; it does not touch
the pipeline's behaviour beyond enforcing the limits in requirement 7.

## Done when

The user always knows what a run will cost, what it did, and can return to any previous run.

## How to verify

1. Run three analyses of different sizes; compare the pre-run estimate against the actual cost
   each time. If the estimate is not in the right order of magnitude it is not useful — say so
   rather than shipping a number that misleads.
2. Watch the live view during a real run: phase, elapsed time, tool-call count, tokens and cost
   all move, and the phase text comes from the LLM's `report_progress` calls.
3. Open the tool-call inspector after a run: every call is present, **in order**, with its
   response size. Cross-check the count against `log.txt`.
4. Run four analyses on one repository, restart, and confirm all four are listed with correct
   model, cost, date and size. Reopen one and confirm — from the provider log — that **no LLM
   call was made** (§ Iteration 7, requirement 5).
5. Re-run the same changeset on a second model and confirm both results are retained and
   distinguishable.
6. Change a file on disk after a run, reopen the analysis, and confirm the stale prompt
   appears. Revert the change and confirm it does not.
7. Set max tool calls to something small and run: the hard stop fires, and the message explains
   what was and was not produced. Confirm a partial result is never presented as a complete
   one (§0.2.5, §0.2.8).
8. Export an analysis, import it on a **different machine**, and confirm it opens fully —
   including explanations, risks and reviewed state if that was the Iteration 10 decision.
9. Import a corrupted file and a file from a different schema version: clear errors, no crash.

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **Where pricing data comes from.** Requirements 1 and 2 need per-model prices, and hardcoded
  prices rot exactly the way hardcoded model lists do (Iteration 2, requirement 4). If Iteration
  4 did not settle this, settle it here: bundled table with a manual update, user-entered
  prices per provider, or no cost figure at all where price is unknown.
- **How the pre-run estimate is computed** without spending money to find out. The tool-call
  volume — the dominant cost — is not knowable in advance. Propose the basis for the range and
  how honestly its uncertainty is presented.
- **Export format and what it contains.** Whether the export includes repository paths, node
  explanations only, or enough to browse diffs offline. This is a **privacy-relevant** decision:
  an exported analysis contains descriptions of the user's source code and is handed to another
  person. Confirm what is in it and whether the UI says so.
- **Stale detection mechanism** — file mtimes, a hash of the changeset, or `HEAD` movement — and
  the threshold at which the prompt appears.
- **Default limits** for max tool calls and max tokens per run, if Iteration 4 left them open.
- **Library retention.** Whether old analyses are pruned, and whether the user can delete them.
