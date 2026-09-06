# Iteration 5 — Repository toolbox for the LLM

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | [3](iteration-03-git-layer.md), [4](iteration-04-llm-provider-layer.md) |
| **Blocks** | 6, 7 |
| **Status** | Complete, with one item explicitly deferred — see below |

> **Deferred to Iteration 7.** Verification step 7 asks for `report_progress` arriving in the UI
> during a live session. The pipe is built and both halves are tested — `ToolProgressNotifierTests`
> for the host, `methods.test.ts` for the renderer — but nothing in the application starts an
> analysis until Iteration 7, so no notification travels the real bridge into the real window yet.
> That is the E2E test [docs/decisions.md](../decisions.md) asks for; it belongs with the first
> real producer.
>
> Decisions taken here that later iterations should not re-open are recorded under
> [The toolbox: what the LLM can and cannot see](../decisions.md).

## Goal

Build the tools the LLM uses to explore the repository. **This is the heart of the product.**

## Context

The app does not analyse code; it lets the LLM analyse code (§0.2.1, §0.2.2). The initial
prompt contains only the changed-file list and instructions — everything else the LLM must
fetch itself.

**The quality of the final graph is bounded by the quality of these tools.** A weak grep, a
badly truncated result, or a tool description the model misreads shows up later as a bad
graph, and it will look like a prompt problem when it is not.

## Fixed technical decisions

- Use the official **`ModelContextProtocol`** C# SDK, **main package** (not `.AspNetCore` —
  there is no HTTP server).
- Define each tool **once** with `[McpServerTool]` attributes. Consume the same definitions
  two ways: **in-process** by the analysis orchestrator, and **over stdio** by external
  agents. **Never maintain two definitions of one tool.**
- Tool XML doc comments become the descriptions the model reads. **Write them as prompt text,
  not as API documentation.** They are a deliverable of this iteration, not boilerplate.
- The toolbox must run **headlessly with no window**, and ship as a **standalone executable**
  other agents (including Claude Code) can point at.

## Requirements

1. Implement, at minimum:
   - list changed files, with status, stats, language and project metadata,
   - get the diff for one or more named files,
   - read a file, or a line range, at either side of the comparison,
   - grep/regex search across the repository, filterable by path glob, language, and
     changed-only, returning file + line + surrounding context,
   - glob/filename search,
   - list a directory,
   - get the repository tree or a subtree, depth-limited,
   - look up metadata for a path: project/module, language, size, changed or not,
   - retrieve the stored project profile ([Iteration 6](iteration-06-repository-knowledge-base.md)),
   - **report progress** — see requirement 2.
2. A **`report_progress`** tool the LLM calls to announce what stage it is at, with a short
   human-readable message and optional phase label. This is how the UI shows meaningful
   progress instead of a spinner. Emit it over the JSON-RPC notification channel.
3. Every tool result is **token-budgeted**: hard result caps, explicit truncation markers, and
   pagination/continuation so the LLM can request more rather than receiving a wall of text.
4. Every tool is **strictly sandboxed** to the selected repository. No path traversal outside
   it, no writes, no command execution, no network access.
5. Log every tool call and its result size for later inspection and cost analysis.
6. Correct behaviour on repositories with very large files, deep trees, thousands of changed
   files, and non-UTF-8 encodings.
7. **Static-analysis-backed tools are explicitly out of scope. Leave the seam; add nothing.**
8. Tests against fixture repositories covering each tool, including truncation and pagination
   behaviour.

## Out of scope

The analysis prompt, the graph. Also anything language-aware: no symbol search, no
call-hierarchy, no AST queries (§0.2.3 and requirement 7).

## Done when

The toolbox is callable in-process and over MCP, every tool is tested, and **a manual LLM
session using only these tools can correctly answer questions about a repository it was never
shown directly.**

## How to verify

1. Run the standalone executable and connect Claude Code (or any MCP client) to it over
   stdio. Every tool appears with a usable description.
2. Perform the "done when" test for real: pick a repository, and using only the toolbox, ask
   a model questions it can only answer by exploring — "what does this project do", "where is
   authentication handled", "which changed file is most connected to the others". Record the
   session; a weak answer means a weak tool, not a weak model.
3. Sandbox tests: `../../etc/passwd`, absolute paths outside the repo, symlinks pointing
   outside the repo, and `.git/` internals. All refused.
4. Confirm there is no write path and no process-execution path in the tool surface at all —
   not "it is not called", but "it does not exist".
5. Truncation: request something enormous and confirm the result is capped, the truncation
   marker is explicit, and the continuation token actually returns the next page.
6. Non-UTF-8 file: readable or cleanly reported, never garbled silently.
7. `report_progress` calls arrive in the UI as JSON-RPC notifications during a live session.
8. Confirm one definition per tool serves both the in-process and stdio paths — a grep for a
   duplicated tool name should find one.

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **MCP SDK version.** §0.3 pins `ModelContextProtocol` v2.x main package. Verify it exists at
  that major version, supports stdio, and works with the .NET target from Iteration 1.
- **Result caps are user-visible economics.** The default cap per tool result and the default
  page size directly drive token cost and graph quality. Propose numbers with reasoning rather
  than choosing silently.
- **Grep engine.** A regex search over a large repository either uses .NET regex over streamed
  files or an external tool. An external tool is an unlisted dependency *and* would violate
  the no-command-execution rule in requirement 4 — flag the tension rather than resolving it
  yourself.
- **`.gitignore` and hidden files.** Confirm whether tools see gitignored files, `.git/`,
  build output and `node_modules`. Cost and answer quality both hinge on this.
- **Standalone executable shape.** Confirm whether it ships as a separate published binary, a
  mode of the host executable, or both — Iteration 14 packages whatever you decide.
- **Profile tool before Iteration 6 exists.** Requirement 1 lists a tool for a thing not built
  yet. Confirm it ships now returning "no profile", or is deferred to Iteration 6.
