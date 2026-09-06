# Iteration 6 — Repository knowledge base

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | [5](iteration-05-repository-toolbox.md) |
| **Blocks** | 7 |
| **Status** | Not started |

## Goal

Give the LLM standing context about the project before it ever sees a diff.

## Context

A diff is unreadable without knowing what the project is. The profile is produced **once per
repository**, stored, reused by every subsequent analysis, and is much cheaper than a diff
run.

There is a second, related feature here: if the repository has no architecture documentation,
the app can generate it. **This is the only place in the entire application that writes to the
user's repository** (§0.2.12), and it is gated accordingly.

## Fixed technical decisions

- The profile is stored in **SQLite, not in the repository**, unless the user explicitly uses
  the documentation generator.
- The documentation generator is **opt-in, requires explicit confirmation, and shows a full
  preview of every file it will write before writing.** It **never** overwrites an existing
  file without showing a diff and getting confirmation. It writes to the repository root or a
  `docs/` directory, user-selectable.
- The profile has a **hard size budget** — it is paid for on every analysis run.

## Requirements

1. An **"Analyse repository"** action that runs the LLM over the whole repository via the
   toolbox and produces a structured project profile: purpose, high-level architecture,
   projects/modules and how they relate, layering conventions, notable patterns, entry points,
   test layout.
2. The profile builder **reads existing repository documentation first** when present —
   `README`, `ARCHITECTURE.md`, ADR directories, `CONTRIBUTING`, `CLAUDE.md`, `AGENTS.md`.
   This is the cheapest high-quality context available and **must be used before exploring
   code**.
3. A separate, opt-in **"Generate project documentation"** action that writes standard
   documentation files derived from the profile — architecture, module map, conventions — into
   the repository, subject to the preview-and-confirm gate above. **Never automatic. Never
   silent.**
4. Persist the profile per repository with the commit it was generated from and a timestamp.
5. The profile is **user-editable**. Manual edits survive regeneration — keep user-authored
   content in a distinct section that regeneration does not touch.
6. A **custom instructions** free-text field stored alongside the profile, injected into every
   analysis prompt. Users write things like "this is CQRS", "ignore the `generated/` folder",
   "the Legacy project is being deleted".
7. Offer regeneration on demand. Prompt for regeneration when the repository has drifted
   substantially since the profile was made.
8. The profile is injected into every analysis prompt **and** is also retrievable as a tool.
9. Show progress via the `report_progress` mechanism, with cost and cancellation.
10. Analysis can proceed **without** a profile, but the UI warns clearly that results will be
    weaker (§0.6).
11. Security: the MCP tools for LLM can look only in the selected repository. They can't get out of this repository, even if Path ../../../.... by the LLM is provided.
      Also sensitive files are not sent (like .env file, sensitive files can also be configured)
12. We also want to log in UI what tools with what input, output and execution time were used (in real time, so user would see the progress)

## Out of scope

Diff analysis. The profile describes the repository as it stands; it knows nothing about the
current changeset.

## Done when

A repository can be profiled, the profile is stored, editable, survives restart, and the
documentation generator writes files only after explicit preview and confirmation.

## How to verify

1. Profile a real repository. The output names its actual purpose, its real modules and their
   relationships — not generic prose that would fit any codebase.
2. Confirm the run reads `README` and any existing architecture docs **before** it starts
   grepping code — check the tool-call order in the log.
3. Edit the user-authored section, regenerate, and confirm the edit survived verbatim and the
   generated section was replaced.
4. Run the documentation generator: the preview lists **every** file with full content before
   anything is written; cancel and confirm **nothing** was written to disk.
5. Run it again against a repository that already has `ARCHITECTURE.md`: a diff is shown and
   confirmation is required before the overwrite.
6. Confirm no other code path in the entire application writes into the repository — audit it,
   do not assume it.
7. Restart and confirm the profile, its commit, its timestamp and the custom instructions all
   persisted.
8. Confirm the profile respects its size budget on a very large repository.
9. Cancel a profile run mid-flight: clean unwind, partial cost reported, no half-written
   profile stored.

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **The size budget number.** "Hard size budget" is unspecified and it is paid on every
  analysis run. Propose a token or character budget with the cost reasoning, and let the user
  set it.
- **Documentation generator output.** Which files, with which names, in which layout? The plan
  says "architecture, module map, conventions" but the file names, structure and whether they
  cross-link are user-visible and unspecified.
- **"Drifted substantially".** Requirement 7 needs a concrete trigger — commits since the
  profile commit, files changed, time elapsed, or a combination. This is a user-visible nag,
  so confirm the rule and the threshold.
- **Profile schema location.** The profile is structured and crosses the JSON-RPC boundary and
  is editable in the UI, so it likely belongs in `/schema` like every other cross-boundary
  contract. Confirm.
- **Editable how.** Requirement 5 says user-editable, but not whether that is a rich form over
  the structured fields or a free-text block. This changes the UI substantially.
- **Multiple repositories, one profile each** — confirm nothing here is expected to work
  across repositories (§0.6 fixes scope at one repository per analysis).
