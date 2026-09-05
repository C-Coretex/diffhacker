# Iteration 2 — Shell, settings, repository selection

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | [Iteration 1](iteration-01-foundation.md) |
| **Blocks** | 3, 4 |
| **Status** | Not started |

## Goal

The user can open the app, choose a repository, and configure an LLM provider.

## Context

Two things every analysis depends on: a repository path and working credentials. Users bring
their own API key. Provider choice is a first-class runtime setting, not a build-time one.

This is also the iteration where the app stops looking like a scaffold. Empty, loading and
error states are requirements here, not polish to be added later — see §0.2.14.

## Fixed technical decisions

- `ISecretStore` with per-OS backends: **DPAPI / Credential Manager** on Windows,
  **Keychain** on macOS, **libsecret** on Linux, plus an **encrypted-file fallback** for
  Linux systems with no keyring daemon (common on headless and minimal installs). The UI
  states honestly which backend is active.
- Settings and the recent-repository list live in **SQLite in the per-user app data
  directory**, never inside the user's repository.
- API keys are stored **only** in `ISecretStore` and **never cross the JSON-RPC bridge into
  the WebView** (§0.2.13).

## Requirements

1. Repository selection by local filesystem path, with a native folder picker. Validate that
   the path is a Git repository; clear error if not.
2. Recent repositories list: reopen in one click, remove entries, handle paths that no longer
   exist.
3. Settings screen for LLM providers: provider type, model identifier, API key, optional base
   URL. Multiple providers can be configured; one is active.
4. Model identifier is **free text with suggestions**, not a fixed dropdown — hardcoded model
   lists rot.
5. "Test connection" performs a minimal **real** request against the configured provider and
   reports success or the actual error.
6. Detect `git` on `PATH` at startup. If absent, show a clear, actionable message; the app is
   non-functional without it.
7. Empty states, loading states and error states designed properly. This is a shipping
   product.

## Out of scope

Diffs, analysis, the graph. Also: the full `ILlmSession` abstraction with budgets, retries
and tool calling — that is [Iteration 4](iteration-04-llm-provider-layer.md). "Test
connection" here needs only the smallest request that proves the credentials work.

## Done when

The user can add a repo, configure a provider, test the connection, restart the app, and find
everything still there.

## How to verify

1. Pick a non-repository folder → clear, specific error. Pick a repository → accepted.
2. Add three repositories, restart, confirm all three are listed. Delete one from disk and
   confirm the list handles it without crashing and offers to remove the entry.
3. Configure two providers, mark one active, restart, confirm the active one persisted.
4. Test connection with a valid key → success. With a deliberately wrong key → the provider's
   *actual* error, not a generic failure.
5. Inspect the SQLite file and confirm **no API key is in it**. Inspect `log.txt` and confirm
   no key was logged. Inspect the JSON-RPC traffic and confirm no key crossed the bridge.
6. On Linux without a keyring daemon, confirm the encrypted-file fallback engages and the UI
   says so.
7. Rename `git` out of `PATH` and restart: actionable message, no crash.

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **Native folder picker across three platforms.** Photino's picker support varies. If any
  platform needs an extra dependency or a fallback path-entry field, say so before building
  it.
- **Encrypted-file fallback key derivation.** Where does the encryption key come from when
  there is no keyring — a machine-derived key, or a user passphrase prompt? This changes the
  UX and the honest security claim, so it is a user decision.
- **Test-connection cost.** The minimal real request still costs tokens on some providers.
  Confirm whether the UI should warn.
- **Provider types to expose in the picker.** Iteration 4 fixes the set at OpenAI, Anthropic,
  Gemini, Grok, DeepSeek and "OpenAI-compatible endpoint". Confirm whether Iteration 2 lists
  all six now, or only the shape and one working type.
- **What "validate that the path is a Git repository" means** for edge cases you will hit
  again in Iteration 3: bare repos, worktrees, submodule directories, a subdirectory of a
  repository rather than its root.
