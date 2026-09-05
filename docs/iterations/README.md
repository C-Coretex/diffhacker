# DiffHacker — implementation plan

Fourteen sequential iterations. Each file below is one Claude Code session.

## How to use these files

Shared context — what the product is, the invariants, the fixed technology decisions, the
ask-vs-decide rules — lives in [CLAUDE.md](../../CLAUDE.md) at the repository root. Claude
Code loads it automatically, so you do **not** need to paste it.

**To start an iteration, one line is enough:**

```
Implement docs/iterations/iteration-03-git-layer.md
```

If you are working somewhere that does not auto-load `CLAUDE.md` (a plain chat window, a
different tool), paste `CLAUDE.md` first, then the iteration file.

## The rules that make this work

- **One session, one iteration.** Do not pull work forward from a later iteration because it
  looks adjacent. If iteration N genuinely needs something from N+3, say so and ask.
- **Read `Raise before implementing` first.** Every iteration file ends with the questions
  that iteration is known to open. Batch them with anything else §0.4 of `CLAUDE.md` covers,
  ask once, then build.
- **The requirements are the contract.** Implement all of them, or report explicitly which
  ones you did not and why. Partial work reported as complete is the one unrecoverable
  failure mode here.
- **`Done when` is a bar to demonstrate, not to assume.**

## The iterations

| # | File | Goal | Depends on |
|---|---|---|---|
| 1 | [Foundation](iteration-01-foundation.md) | A running, empty, correctly structured app on all three platforms, with the contract pipeline in place | — |
| 2 | [Shell, settings, repository selection](iteration-02-shell-settings-repository.md) | The user can open the app, choose a repository, and configure an LLM provider | 1 |
| 3 | [Git layer](iteration-03-git-layer.md) | Produce the changeset everything else operates on | 1, 2 |
| 4 | [LLM provider layer](iteration-04-llm-provider-layer.md) | One internal contract, five providers, tool calling on all of them | 1, 2 |
| 5 | [Repository toolbox](iteration-05-repository-toolbox.md) | The tools the LLM uses to explore the repository | 3, 4 |
| 6 | [Repository knowledge base](iteration-06-repository-knowledge-base.md) | Standing project context, produced once and reused | 5 |
| 7 | [Analysis pipeline](iteration-07-analysis-pipeline.md) | Turn the changeset into a complete, validated, persisted graph result | 5, 6 |
| 8 | [Graph rendering](iteration-08-graph-rendering.md) | Draw the diagram | 7 |
| 9 | [Explanations](iteration-09-explanations.md) | Hover cards, risks column, overview panel | 8 |
| 10 | [Diff viewer and navigation](iteration-10-diff-viewer.md) | Close the loop from "understand the change" to "review the code" | 9 |
| 11 | [Grouping modes](iteration-11-grouping-modes.md) | Dependency flow vs change clusters | 7, 8 |
| 12 | [Unchanged intermediate nodes](iteration-12-unchanged-intermediate-nodes.md) | Optionally reveal the files in between | 8, 10 |
| 13 | [Cost, transparency, analysis library](iteration-13-cost-transparency-library.md) | Make expensive runs predictable and repeatable | 7 |
| 14 | [Packaging and release](iteration-14-packaging-release.md) | Ship it | all |

### ⏸ MVP line

**Iterations 1–9 are the minimum useful product:** local diff → LLM-built grouped graph →
explanations. Everything after that is what makes it good to live in.

Iterations 11, 12 and 13 do not strictly depend on 10 and can be reordered against each
other if priorities change. 14 is last.

## Not in this plan

[docs/future-improvements.md](../future-improvements.md) records what was deliberately
excluded — branch comparison, PR integration, multi-pass analysis, semantic zoom, static
analysis, export, and the rest. It exists so those ideas are not forgotten *and* not
accidentally built early.
