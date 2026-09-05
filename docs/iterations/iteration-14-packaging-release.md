# Iteration 14 — Packaging and release

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | All previous iterations |
| **Blocks** | — |
| **Status** | Not started |

## Goal

Ship it.

## Context

Everything up to here was for a developer running from source. This iteration is for a
stranger who found the repository and wants to try it.

## Fixed technical decisions

- **Velopack** for installers and auto-update on all three platforms.
- `dotnet publish` **self-contained per RID**; users need no .NET installation.
- Windows installers must handle the **WebView2 runtime** dependency. It is present on current
  Windows 11 and updated Windows 10 but **cannot be assumed**.
- **Licence: MIT** (see §0.7 of `CLAUDE.md`). This differs from the original planning document,
  which specified Apache-2.0 with a `NOTICE` file. **The consequence is real and must be
  respected in the copy you write:** MIT has no `NOTICE` mechanism, so attribution by
  commercial users is a **request, not a legal requirement**. Include an attribution line in the
  About screen and ask that it be preserved — but **do not write documentation implying it is
  obligatory**.

## Requirements

1. **Signed, installable builds** for Windows, macOS and Linux, produced by CI. **Notarisation
   on macOS.**
2. **First-run experience**: explain what the app does, walk the user through adding a
   repository and a provider key, and get them to a first analysis **without documentation**.
3. **Crash reporting, opt-in only**, never sending repository content. Local logging to
   `log.txt` always on, with a **"reveal log file"** action in the UI.
4. **Auto-update** via Velopack.
5. **User documentation**: what the graph means, what containers and direct/conceptual edges
   mean, how costs work, and **precisely what the app sends to the LLM provider**.
6. An explicit **privacy statement shown before the first analysis**: the app sends portions of
   the user's source code to the configured provider.
7. `LICENSE`, `CONTRIBUTING.md` and a public README with screenshots.
8. **Launch-and-screenshot smoke test per OS in CI.**

## Out of scope

New features. If something is missing, it is missing — record it in
[future improvements](../future-improvements.md) rather than sneaking it in here.

## Done when

**A developer who has never seen the project can install it and review a real changeset.**

Test this literally: hand the installer to someone who has not worked on it and watch without
helping.

## How to verify

1. Install from the produced installer on a **clean** machine per OS — not a dev machine — and
   complete a real analysis.
2. On Windows, test on a machine **without** the WebView2 runtime. The installer handles it or
   says clearly what is needed.
3. On macOS, confirm the notarised build opens without a Gatekeeper warning.
4. On Linux, confirm the WebKitGTK dependency is either bundled or clearly declared.
5. Do the first-run walkthrough as a new user, with **no documentation open**, and reach a
   rendered graph.
6. Confirm the privacy statement appears **before** the first analysis, not after, and that it
   describes what is actually sent — verify it against the real prompt and tool traffic.
7. Confirm crash reporting is **off** until explicitly enabled, and that a test crash report
   contains **no repository content, no file paths and no source**. Inspect the payload.
8. Trigger an auto-update from an older build to a newer one on each OS.
9. Click "reveal log file" and confirm it opens the right directory on all three platforms.
10. Read the README as a stranger: does it say what this is, what it costs, and what it sends?

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **Signing certificates and Apple credentials are the user's to provide.** Code signing needs
  a Windows certificate and an Apple Developer account with an app-specific password for
  notarisation. Ask how these reach CI, and **never** commit them or print them in logs. If
  they are not available, say clearly that unsigned builds are the outcome and what that means
  for users.
- **Crash reporting backend.** Sentry, or anything comparable, is an unlisted dependency and
  sends data off the machine. §0.4 requires asking before adding it; the privacy claim in
  requirement 3 depends on which one and how it is configured.
- **Distribution channels** beyond direct download — Homebrew, winget, Flatpak, AUR. Each is
  extra maintenance; none is in the requirements. Confirm before building any.
- **Velopack version and platform support** at implementation time, particularly for Linux
  packaging and macOS notarisation integration.
- **Screenshots for the README** need a real analysis of a real repository. Confirm which
  repository is acceptable to show, since screenshots leak source code and file names.
- **The attribution ask.** Confirm the exact wording for the About screen and README given MIT,
  so it reads as a genuine request rather than an unenforceable demand.
