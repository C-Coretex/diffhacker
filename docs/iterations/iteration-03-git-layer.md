# Iteration 3 — Git layer

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | [1](iteration-01-foundation.md), [2](iteration-02-shell-settings-repository.md) |
| **Blocks** | 5, 7 |
| **Status** | Not started |

## Goal

Produce the changeset everything else operates on.

## Context

Scope here is deliberately narrow. The app reviews **the current local diff only**: the
working tree against `HEAD`. There is no branch picker and no commit selection. This covers
the primary use case — an AI agent has just made a large change and it has not been committed
yet.

**Untracked files matter.** AI-generated changes routinely add brand-new files that
`git diff` alone will not show, and omitting them would silently violate the completeness
invariant (§0.2.5). This is the most common way this layer gets quietly wrong.

## Fixed technical decisions

- Shell out to the **`git` CLI** behind `IGitClient`. Not LibGit2Sharp: the CLI is exactly
  correct on rename detection, submodules, huge repositories and `--numstat` / `--porcelain`
  output, and avoids native-binary distribution friction across three OSes and two
  architectures.
- **Always use explicit machine-readable output flags. Never parse human-facing output.**
- Enforce a **hard allowlist of permitted git subcommands**. No command that mutates the
  repository may be invoked from this layer, ever (§0.2.12).

## Requirements

1. Produce the changeset as working tree vs `HEAD`, covering staged and unstaged
   modifications together.
2. Include **untracked files that are not gitignored**, represented as added files with their
   full content as the added side. Provide a toggle to exclude them, **defaulting to
   included**.
3. Per file, produce: path, previous path for renames, status (added / modified / deleted /
   renamed / copied), lines added, lines removed, hunk count, binary flag, detected language.
4. Per file, produce project/module metadata: locate the nearest project manifest of any
   ecosystem (`*.csproj`, `package.json`, `pyproject.toml`, `go.mod`, `pom.xml`,
   `Cargo.toml`, …) and fall back to the top-level directory. **This is metadata only — do
   not parse manifests semantically** (§0.2.3).
5. Retrieve the unified diff for any single file on demand, and the before/after content of
   any file at either side.
6. Aggregate statistics: total files, total lines added and removed, counts per status,
   languages touched, projects touched.
7. Handle **without crashing**: binary files, deleted files, renames, submodules, symlinks,
   empty diffs, files with no trailing newline, and repositories with no commits.
8. **Do not load an entire large changeset into memory at once.**
9. Detect and report a dirty-state edge case cleanly: if the working tree is clean, say so
   rather than producing an empty analysis.
10. Tests run against fixture repositories built in temp directories by a test helper — real
    commits, real renames, real untracked files.

## Out of scope

The LLM, the graph, rendering diffs. The file list this iteration produces is displayed
plainly; making it beautiful is not the job.

## Done when

The app displays a plain changed-file list with statuses, stats and metadata for the current
working tree, and can produce the diff for any file in it.

## How to verify

Build fixture repositories covering each of these and assert the changeset is correct:

1. Staged + unstaged edits to the same file, appearing once with combined stats.
2. An untracked new file present by default; excluded when the toggle is off; a gitignored
   file absent in both modes.
3. A rename detected as a rename, with the previous path populated — not as an add plus a
   delete.
4. A binary file flagged, with no line counts invented for it.
5. A repository with **no commits at all** — no `HEAD` to compare against.
6. A clean working tree reporting "clean", not an empty changeset.
7. A file with no trailing newline, a symlink, and a submodule directory: no crash, sensible
   representation.
8. Nearest-manifest resolution: a file under `src/Web/` with a `package.json` two levels up
   attributes to that project, not to the repository root.
9. A synthetic large changeset (hundreds of files, at least one very large file) processed
   without loading everything into memory — assert on peak allocation or on streaming
   behaviour, not just on "it finished".
10. Attempt to invoke a mutating subcommand through `IGitClient` and confirm the allowlist
    rejects it.

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **Untracked file content vs requirement 8.** Requirement 2 says untracked files carry their
  full content as the added side; requirement 8 forbids loading the whole changeset into
  memory. Propose the resolution (lazy content, size caps, streaming) — a size cap is a
  user-visible behaviour, so confirm it rather than picking one.
- **Submodule representation.** A dirty submodule is a single "changed" entry pointing at
  another repository. Confirm whether it appears as one file-like entry, is annotated, or is
  excluded — this reaches the graph in Iteration 7 and the completeness invariant applies.
- **Language detection mechanism.** §0.2.3 permits language as metadata only. If you want an
  existing mapping library rather than a hand-written extension table, that is an unlisted
  dependency and §0.4 requires asking.
- **Non-UTF-8 encodings.** Iteration 5 requires correct behaviour on them; decide here how the
  Git layer represents them so the toolbox does not have to guess.
- **Deleted-file "after" content and added-file "before" content** — confirm the shape the
  API returns (null, empty, or a typed absence) so Iteration 10's diff viewer has one rule.
