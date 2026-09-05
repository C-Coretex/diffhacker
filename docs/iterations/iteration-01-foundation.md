# Iteration 1 — Foundation

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | Nothing |
| **Blocks** | Every other iteration |
| **Status** | Not started |

## Goal

A running, empty, correctly structured application on all three platforms, with the contract
pipeline in place.

## Context

Everything downstream depends on three things existing and working: the Photino shell, the
JSON-RPC bridge between .NET and React, and the JSON Schema codegen that keeps the LLM, the
host and the renderer agreeing on data shape.

Get these right once and never touch them again. Every hour spent here is repaid ten times
over; every shortcut here is paid for in every subsequent iteration.

The repository currently contains `.gitignore`, `LICENSE`, `README.md`, `CLAUDE.md`,
`docs/`, and an empty `src/DiffHacker.slnx`. Everything else is yours to create.

## Fixed technical decisions

- Photino.NET hosts a **single native window**. UI assets are served through a **registered
  custom scheme handler, in-process**. There is no local HTTP server, no Kestrel, no
  localhost port. **This is a hard constraint** — it is what keeps the WebView off the
  network and makes the CSP meaningful.
- Photino is wrapped behind a small `IAppShell` interface. **Photino types must not appear
  anywhere else in the codebase.** Expected surface area is roughly 200 lines: create
  window, register scheme handler, send message, receive message.
- Host ↔ UI messaging is **JSON-RPC 2.0** over the Photino message channel. Do not invent a
  message format. Requests and responses carry correlation IDs; server→client notifications
  carry progress and trace events.
- The React app is built by Vite and **embedded as resources in the .NET assembly**.
- A strict **Content-Security-Policy** is applied to the WebView: no remote script, no
  remote styles, no `eval`. Every dependency, Monaco included, is bundled locally.
- `/schema` holds JSON Schema files as the **single source of truth** for all cross-boundary
  contracts. The build generates C# records into `DiffHacker.Contracts` and TypeScript types
  into `/src/ui`. **Codegen runs as part of the build, not by hand.**

## Requirements

1. Create the solution per the layout in §0.3 of `CLAUDE.md`, with project references
   enforcing that `DiffHacker.Core` does not depend on `DiffHacker.Host`.
2. Photino window opens, serves the Vite-built React app via the custom scheme handler, and
   renders on Windows, macOS and Linux.
3. JSON-RPC bridge working in both directions, with a demonstration method and a
   demonstration notification stream. Typed on both sides from generated contracts.
4. `/schema` scaffolded with at least one real contract, and codegen wired into the build for
   both C# and TypeScript. Schema files are versioned; the version is part of the contract.
5. Tailwind + shadcn/ui installed and configured; dark and light themes following the system
   setting.
6. Rolling file logging to `log.txt` in the per-user application data directory, with
   structured entries. **Secrets are never logged.**
7. Test projects created and running: xUnit for .NET, Vitest for the UI.
8. CI building and running tests on **all three operating systems** from this iteration
   onward. WebKitGTK, WKWebView and WebView2 render differently and you need that visible
   immediately.
9. `CLAUDE.md` updated: fill in the **Repository conventions** section with the real build,
   test and codegen commands, and delete the placeholder note. §0 itself is already written
   and must not be rewritten.

## Out of scope

Every product feature. No git access, no LLM, no graph, no diffs. The window may show a
placeholder screen — that is the correct outcome.

## Done when

- The app launches on Windows, macOS and Linux.
- The React UI can call a .NET method and receive a streamed notification.
- Changing a schema file and rebuilding regenerates **both** the C# and the TypeScript types.

## How to verify

1. `dotnet build src/DiffHacker.slnx` from clean succeeds, and codegen output appears without
   a manual step.
2. Launch the host on each OS; the window opens and the React app renders — confirm with a
   screenshot per OS in CI (requirement 8).
3. Click the demo control: it calls a .NET method, receives a typed result, and then receives
   at least three streamed notifications.
4. Edit a property in a `/schema` file, rebuild, and confirm the change surfaces as a
   compile error or a new member in *both* generated languages.
5. Switch the OS theme; the UI follows without a restart.
6. Confirm `log.txt` exists at the expected per-user path with structured entries.
7. Open DevTools and confirm the CSP blocks a deliberately injected remote script.

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **.NET target version.** §0.3 says "current LTS, verified against Photino support". Confirm
  which LTS you land on and that Photino.NET supports it at the version you are pinning.
- **Codegen tooling is an unlisted dependency.** JSON Schema → C# and JSON Schema → TypeScript
  needs a generator (NJsonSchema, quicktype, `json-schema-to-typescript`, or similar). §0.4
  requires you to ask before adding a dependency not in §0.3. Propose one per language with a
  reason.
- **Custom scheme name and origin.** The scheme string affects CSP, `localStorage` isolation
  and WebView2/WKWebView/WebKitGTK behaviour differences. Propose one and flag any per-platform
  divergence you hit.
- **CI runner and signing.** Signing is Iteration 14, but say now if the CI shape you are
  building would make it awkward later.
- **Resource layer.** §0.6 forbids hardcoded UI strings from the start. Propose the mechanism
  for the React side, since .NET `.resx` does not reach the WebView.
