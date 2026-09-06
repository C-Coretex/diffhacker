# CLAUDE.md — DiffHacker

This file is the standing context for every Claude Code session in this repository.
It is loaded automatically. Read it before doing anything else.

Sections §0.1–§0.6 are the **product contract**. They are fixed. Implement them; do not
re-litigate them. Everything genuinely open is listed in §0.4 or in the iteration files
under [docs/iterations/](docs/iterations/).

---

## §0.1 What the product is

DiffHacker is a cross-platform desktop app for reviewing large local Git changesets.

The user points it at a local repository. It takes the current uncommitted diff —
everything the working tree has that `HEAD` does not — and produces one diagram divided
into **containers**, each a cluster of related change. Inside a container, nodes are files
(or specific places inside files), arranged so the most important node sits at the top and
the reviewer walks downstream through the consequences.

Hovering a node explains what changed there and why. Hovering an edge explains how that
relationship changed. Hovering a container explains the cluster. Risks are shown in a
separate column from explanations. Clicking a node opens the diff.

The graph is built **entirely by the LLM**. The application's job is to give the LLM tools
to explore the repository — search, grep, read, diff, metadata — and to render, persist and
navigate what the LLM produces. The app is language-agnostic and LLM-provider-agnostic.

**The problem it solves:** on a large AI-generated change, the reviewer opens 300 files in
alphabetical order and reconstructs the structure of the change in their head. This app
reconstructs it for them.

**Who it is for:** developers reviewing large changes, especially ones produced by AI agents
where 100–1000+ files change at once and the coherent intent behind them is invisible from
the file list. This is intended to become a polished open-source product used by other
developers, not a personal script.

---

## §0.2 Product invariants

Non-negotiable. They apply to every iteration.

1. **The LLM is the source of truth.** The graph, containers, edges, ordering and
   explanations are all decided by the LLM. The application does not compute the dependency
   graph itself.
2. **The application is a toolbox for the LLM.** Its core value is giving the LLM read
   access to the repository and the diff through tools. The LLM decides what to read.
3. **Language-agnostic.** No language-specific parsing. No Roslyn, no tree-sitter, no ASTs.
   The only per-language thing the app does is tag a file with its detected language as
   metadata.
4. **LLM-provider-agnostic.** Users bring their own API key. Adding a provider must not
   require touching analysis logic.
5. **Every changed file appears in the graph.** No change may be dropped, hidden or
   summarised away. Hard invariant, validated after every analysis run.
6. **Both direct and conceptual relationships.** An edge may represent a real code
   dependency or a conceptual/reading-flow relationship the LLM inferred. The LLM is
   explicitly allowed to invent conceptual edges. The two must be visually distinguishable.
7. **Edges express reading flow, not call semantics.** An edge means "to understand this
   change, read from here to there". Do not build a taxonomy of relationship verbs like
   *calls* / *implements* / *uses*.
8. **Analysis completes before display.** The full result — graph, layout, all explanations,
   risks, summary — is generated and persisted first, then shown. No progressive reveal of a
   half-built graph. No on-demand explanation generation at hover time.
9. **Token economy matters a lot.** The initial prompt contains only the changed-file list,
   project context and instructions. Everything else the LLM pulls in itself through tools.
   Never bulk-inject file contents or diffs.
10. **Any changeset size must work.** 10 files or 1500 files.
11. **Local uncommitted changes only.** The app reviews the working tree against `HEAD`.
    There is no branch picker, no commit picker, no commit-range selection, and no
    GitHub/GitLab integration anywhere in this plan.
12. **Read-only, with one explicit exception.** The app never commits, stages, checks out, or
    modifies source files. The single exception is the opt-in documentation generator in
    Iteration 6, which requires explicit user confirmation and a preview before writing.
13. **The WebView is a pure renderer.** No network access, no filesystem access, and API keys
    never reach it. All I/O happens in .NET.
14. **Production quality from iteration one.** Proper error handling, tests, logging and
    separation of concerns from the start.

---

## §0.3 Fixed technology decisions

| Concern | Decision |
|---|---|
| Shell | **Photino.NET** (WebView2 / WKWebView / WebKitGTK) |
| UI | **React 19 + TypeScript**, built with **Vite** |
| Graph | **React Flow** (`@xyflow/react` v12) |
| Layout engine | **ELK.js** (`elkjs`), `layered` algorithm, in a Web Worker |
| Diff viewer | **Monaco Editor** `DiffEditor`, bundled locally |
| UI state | **Zustand** |
| Styling | **Tailwind CSS** + **shadcn/ui** (Radix primitives) |
| Host ↔ UI protocol | **JSON-RPC 2.0** over the Photino message channel |
| Contract source of truth | **JSON Schema** in `/schema` → generated C# records + TypeScript types |
| Git access | **git CLI** behind an `IGitClient` abstraction |
| LLM abstraction | **`Microsoft.Extensions.AI`** / `IChatClient` |
| MCP | **`ModelContextProtocol`** (official C# SDK, v2.x, main package — not `.AspNetCore`) |
| Persistence | **SQLite** (`Microsoft.Data.Sqlite`), JSON documents + indexed columns |
| Secrets | `ISecretStore`, per-OS backends + encrypted-file fallback |
| Logging | Local rolling `log.txt` in the app data directory |
| .NET target | Current LTS, verified against Photino support |
| Packaging | **Velopack** (installers + auto-update) |
| Tests | xUnit (.NET), Vitest + React Testing Library (UI), Playwright (E2E) |
| Licence | **MIT** (see §0.7) |

### Solution layout

Indicative. Internal structure *within* each project is Claude Code's call.

```
/schema                        JSON Schema — contract source of truth
/src
  DiffHacker.slnx
  DiffHacker.Contracts         generated DTOs + hand-written value types
  DiffHacker.Core              analysis orchestration, validation, domain
  DiffHacker.Git               IGitClient + git CLI implementation
  DiffHacker.Llm               IChatClient wiring, provider registry, budgets
  DiffHacker.Tools             the toolbox + MCP server surface
  DiffHacker.Storage           SQLite, analysis library, settings, secrets
  DiffHacker.Host              Photino, JSON-RPC dispatcher, composition root
  /ui                          Vite + React + TypeScript
/tests
/docs
```

Hard structural rules:

- `DiffHacker.Core` **must not** reference `DiffHacker.Host`. Enforce it with project
  references and an architecture test.
- The toolbox (`DiffHacker.Tools`) **must run headlessly**, without a window.
- `Microsoft.Extensions.AI` types **must not leak into** `DiffHacker.Core`.
- Photino types **must not appear** anywhere outside the `IAppShell` implementation.

---

## §0.4 When to ask the user, and when to decide

**Ask the user directly, before implementing, when:**

- A product behaviour is unspecified or ambiguous in the iteration text.
- A decision changes what the user sees or how they interact with the app.
- A fixed technology decision in §0.3 turns out to be wrong, blocked, deprecated, or
  unavailable at the version required — report the problem and the alternatives rather than
  silently substituting.
- Two requirements conflict, or a requirement conflicts with an invariant in §0.2.
- Implementing something as specified would take substantially longer than expected, or
  force a design you believe is a mistake — say so and explain why before proceeding.
- A dependency needs to be added that is not listed in §0.3.

**Decide without asking:**

- Internal structure within a project, class and file layout, naming.
- DI registration, lifetimes, composition-root wiring.
- Which specific well-known utility library to use inside the fixed stack.
- Test structure, fixture design, mocking approach.
- Error-message wording and copy.
- Anything explicitly listed as open in the iteration's own text.

Ask in one batch where possible rather than one question at a time. When in doubt, ask.

---

## §0.5 Terminology

| Term | Meaning |
|---|---|
| **Analysis** | One complete run: the current diff plus the LLM output produced from it. |
| **Changeset** | The working tree compared against `HEAD`, including staged, unstaged and untracked non-ignored files. |
| **Container** | A cluster of interconnected change inside the diagram. Unrelated changes belong to different containers. |
| **Node** | A file, or a specific place inside a file, that participates in the change. |
| **Direct edge** | A relationship backed by an actual code-level dependency. |
| **Conceptual edge** | A relationship the LLM inferred from intent, workflow or reading order. |
| **Entry point** | The node a reviewer should start from within a container. |
| **Toolbox** | The set of repository-exploration tools the app exposes to the LLM. |
| **Project profile** | Stored, reusable knowledge about the repository, produced in Iteration 6. |

---

## §0.6 Settled product decisions

Recorded so no iteration re-opens them.

- **Node granularity:** one node per file. Split a file into multiple nodes only when it
  contains two genuinely unrelated changes.
- **Node identity:** node IDs are derived from the file path (plus a disambiguator when a
  file yields several nodes) and are stable across re-runs.
- **Cross-container edges:** drawn, styled faintly, and excluded from layout influence.
- **Colour channels:** node fill encodes project/module; node state is carried by border
  style plus a corner badge.
- **Analysis passes:** single pass only, regardless of changeset size. Multi-pass is deferred.
- **Partial re-analysis:** does not exist. Re-analysis is always the whole changeset.
- **Repository profile:** strongly prompted but skippable, with a visible warning that
  results will be weaker without it.
- **Persistence:** the whole analysis is written to disk and reopenable after restart.
- **Scope:** one repository per analysis. No workspaces.
- **Localisation:** English only, but no hardcoded UI strings — everything through a
  resource layer.
- **Telemetry:** opt-in crash reports only, never repository content. Local logging always to
  `log.txt`.
- **Keyboard navigation:** nice-to-have, not a priority.

---

## §0.7 Naming and licence — deviations from the original plan

Two things differ from the source planning document. These are decided; do not revisit.

- **Product name is `DiffHacker`, not `ChangeGraph`.** The repository, the GitHub remote and
  `src/DiffHacker.slnx` already use it. All projects are `DiffHacker.*`; the app's display
  name is DiffHacker.
- **Licence is MIT**, not Apache-2.0. Note the consequence for Iteration 14: MIT has no
  `NOTICE` mechanism, so "commercial users must credit the project" is a *request* in the
  About screen and README, not a licence obligation. Do not write documentation implying it
  is legally required.

---

## Working agreement

### The iteration model

Work is organised as 14 sequential iterations. Each lives in its own file under
[docs/iterations/](docs/iterations/) and is self-contained: goal, context, the technical
decisions already made for it, numbered requirements, out-of-scope, and a done-when bar.

**One session = one iteration.** The user will link or paste a single iteration file. This
CLAUDE.md supplies the shared context; the iteration file supplies the work.

Do not start work from a later iteration because it looks easy or adjacent. If something in
iteration N genuinely requires a piece of iteration N+3, say so and ask.

Index: [docs/iterations/README.md](docs/iterations/README.md)

### Before you write code in an iteration

1. Read the iteration file end to end, including **Raise before implementing**.
2. Batch every question from that section plus anything else §0.4 covers, and ask once.
3. Only then implement.

### Definition of done for any iteration

- Every numbered requirement is implemented or explicitly reported as not done, with why.
- The **Done when** bar is demonstrably met, not assumed.
- Tests exist and pass, on the platforms the iteration touches.
- CI is green on Windows, macOS and Linux (from Iteration 1 onward).
- No secret is ever written to `log.txt`, to SQLite in plaintext, or across the JSON-RPC
  bridge into the WebView.

### Reporting

Report outcomes faithfully. If tests fail, say so and show the output. If a requirement was
skipped, say which and why. Do not report an iteration complete when it is partially done.

---

## Repository conventions

### Prerequisites

.NET SDK 10 (pinned in `global.json`) and Node.js 24. `dotnet build` invokes `npm`, so Node is
required even for backend-only work — pass `-p:SkipUiBuild=true` to opt out.

### Commands

Run from the repository root.

| Task | Command |
|---|---|
| Restore + build (includes codegen and the UI bundle) | `dotnet build src/DiffHacker.slnx` |
| Run the app | `dotnet run --project src/DiffHacker.Host` |
| .NET tests | `dotnet test src/DiffHacker.slnx` |
| UI tests | `npm run test:run` in `src/ui` (`npm test` to watch) |
| UI type check | `npm run typecheck` in `src/ui` |
| Renderer inner loop | `npm run watch` in `src/ui`, then reload the window |
| Regenerate contracts from `/schema` | Automatic on build. Standalone: `npm run contracts` in `src/ui` |
| End-to-end tests (drives the real window) | `npm test` in `tests/e2e` (`npm install` once; build the solution first) |
| E2E report and screenshots | `npm run report` in `tests/e2e`; PNGs in `tests/e2e/artifacts/screenshots/` |
| Run the app against throwaway state | `dotnet run --project src/DiffHacker.Host -- --data-dir <path>` |

> `dotnet test` must not be passed `--nologo`: under Microsoft.Testing.Platform the flag is
> forwarded to the test executable, which rejects it and reports "Zero tests ran".

### How the pieces fit

- **Contracts.** `/schema/*.schema.json` → `tools/DiffHacker.SchemaGen` → C# records in
  `src/DiffHacker.Contracts/Generated/` and TypeScript in `src/ui/src/contracts/`. Both are
  gitignored and produced on every build. A schema's `title` is the generated type name, and
  its declared `enum` strings are what go on the wire.
- **Renderer.** Vite builds `src/ui` into `src/ui/dist`. Release embeds it in the host
  assembly; Debug serves it from disk. It is always served in-process through the
  `diffhacker://app/` scheme handler — never over HTTP.
- **Strings.** The host sends error codes and resource keys, never prose. `src/ui/src/i18n/en.ts`
  is the single resource layer; keys are compile-time checked.
- **End-to-end.** `tests/e2e` attaches Playwright to the live WebView2 window over the Chrome
  DevTools Protocol — WebView2 honours `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`, which
  `PhotinoAppShell` never overrides, so no production code knows the suite exists. Windows only:
  WKWebView and WebKitGTK expose no equivalent, and the suite skips there rather than reporting a
  pass it did not earn. This is the whole-application gate; screenshots are evidence only.

### Conventions

- **Contracts are generated, never hand-edited.** If a cross-boundary type is wrong, change
  the JSON Schema in `/schema` and rebuild. Generated files are not edited and not reviewed
  as source.
- **Schema files are versioned**, and the version is part of the contract. A persisted
  analysis records the schema version it was written with. Bumping it means editing
  `schema/contract-version.json` **and** the `/<major>.<minor>/` segment in every schema's
  `$id`, which is what the 1.0 → 1.1 → 1.2 bumps did.
- **A `$def` must not reference another `$def`.** NJsonSchema's C# record template throws
  ("Error while rendering Liquid template CSharp/Class.Constructor.Record") on a nested
  reference, so one level — root → `$def` — is all the generator supports. Flatten the inner
  object into its parent instead; `schema/changeset-result.schema.json` carries the per-status
  counts as flat properties for exactly this reason. Cross-*file* `$ref` is likewise unused:
  shared shapes are duplicated per file and reconciled with an agreement test.
- **No hardcoded user-facing strings.** Everything goes through the resource layer, even
  though the app ships English only.
- **Never parse human-facing git output.** Always use explicit machine-readable flags. In
  practice that means `-z` everywhere, `--raw` for statuses and file modes, and `--numstat` for
  line counts. The one place a patch stream is read at all — hunk counting — deliberately never
  reads a path out of it.
- **Never invoke a git subcommand that mutates the repository.** The Git layer enforces an
  allowlist in `GitProcessRunner.PermittedSubcommands`, granted per top-level subcommand. That
  granularity is why `submodule` is absent: its status query cannot be allowed without also
  allowing `submodule update`.
- **Text encodings** are decided once, in `DiffHacker.Core.Changes.TextDecoding`: a NUL in the
  first 8000 bytes means binary, then BOM, then strict UTF-8, then Latin-1 — and the result says
  which was used. Latin-1 rather than Windows-1252 because `InvariantGlobalization` is on and
  the Windows code pages would need a package.
- **Logging:** structured entries to a rolling `log.txt` in the per-user application data
  directory. Redact secrets at the sink, not at each call site.
- **Tests:** xUnit for .NET, Vitest + React Testing Library for the UI, Playwright for E2E.
  Git-layer and toolbox tests run against fixture repositories built in temp directories by
  a test helper — real commits, real renames, real untracked files. No test hits a real LLM
  provider.
- **The end-to-end suite is part of the change, not an afterthought.** Any iteration that adds a
  screen, an RPC method, a user-visible state or a new failure mode **extends `tests/e2e`** to
  cover it, and **runs the suite before reporting the work done**. It is the only thing in the
  repository that proves the layers are actually wired together — the unit suites all pass
  happily with a bridge that answers nothing. See [tests/e2e/README.md](tests/e2e/README.md) for
  the conventions; the short version is one test per journey, no fixed sleeps, assertions taken
  from `en.ts` rather than pasted, and screenshots at every meaningful step.
- **E2E runs against throwaway state, never yours.** The host is launched with `--data-dir`,
  because .NET resolves the per-user data directory through the Win32 known-folder API and no
  environment variable can redirect it. Anything that bypasses that switch will write test
  providers and API keys into the developer's real secret store.

### Continuous integration is deferred

Iteration 1 requirement 8 asked for CI on Windows, macOS and Linux, and the general definition
of done says CI must be green from Iteration 1 onward. **The user has deliberately deferred
this.** There is no `.github/workflows/` directory, and one should not be added back until
they ask for it.

Consequences to keep in mind, since nothing else covers them:

- **macOS and Linux are unverified, and now have no automated end-to-end coverage at all.**
  Everything in this repository has only ever been built and run on Windows. WebKitGTK and
  WKWebView are expected to differ from WebView2, especially around the custom scheme handler
  and `prefers-color-scheme`. The user has accepted this deliberately — see the note on the
  self-test below.
- The full local gate is `dotnet test src/DiffHacker.slnx`, `npm run test:run` in `src/ui`, and
  `npm test` in `tests/e2e`. Only the first two run anywhere but Windows.
- `tools/ci/screenshot.ps1` and `tools/ci/screenshot.sh` launch the app, capture the screen and
  close it. They were written for CI but work standalone, and are how the renderer gets a
  visual check on a given platform.

### The renderer self-test has been removed

Iteration 1 built a `--self-test` mode: the host launched, the renderer verified the bridge from
inside the page, reported a verdict through `host.reportSelfTest`, and the process exited 0 or 1.
**It is gone, deliberately, and should not be rebuilt.** So is the demo RPC surface that existed
to feed it — `DemoRpcTarget`, `DemoPanel.tsx`, and the `start-demo-*` and `progress-notification`
schemas.

Why: `tests/e2e` covers everything it covered except two checks, and both moved to
[04-shell-guarantees.spec.ts](tests/e2e/specs/04-shell-guarantees.spec.ts) — Content-Security-Policy
enforcement and the contract handshake. Driving them from a test rather than from app code is
strictly better, because the self-test was **test code compiled into the shipped renderer
bundle**, plus an RPC surface and a CLI mode that existed only for it.

The cost the user accepted:

- **macOS and Linux have no automated end-to-end coverage.** The self-test was the only thing
  that could ever have run there, since Playwright needs CDP and so needs WebView2. Its
  "redundant" checks were the ones that would have caught a broken Keychain or libsecret
  backend. The user does not want cross-platform verification, so this is settled.
- **Host→renderer notifications are only half covered.** The host side stays proven by
  `RpcBridgeTests.A_target_can_push_notifications_back_to_the_renderer`, which uses a target
  local to the test; the renderer side is proven only by `client.test.ts` against a fake
  transport. Nothing exercises a real notification travelling the real bridge into the real
  window, because after the demo target's removal nothing sends one. **Iteration 5's
  `report_progress` is the first real producer — add an end-to-end test for it there.**

### Dependencies beyond §0.3

Added during Iteration 1 with the user's approval. §0.4 still applies: ask before adding more.

| Package | Why |
|---|---|
| `StreamJsonRpc` | JSON-RPC 2.0 over the shell's message channel, via a custom `MessageHandlerBase`. |
| `NJsonSchema.CodeGeneration.{CSharp,TypeScript}` | Contract codegen. One parser for both languages, so they cannot drift. Tool-only. |
| `Serilog` + `Serilog.Sinks.File` + `Serilog.Extensions.Logging` | Rolling `log.txt` behind `ILogger<T>`. No Serilog type escapes the composition root. |
| `Microsoft.Extensions.{DependencyInjection,Logging}` | Composition root. |
| `Shouldly` | Test assertions. |

Added during Iteration 2:

| Package | Why |
|---|---|
| `Microsoft.Data.Sqlite` | §0.3's persistence choice, now actually used. Version-matched to the `Microsoft.Extensions` 10.0.11 line. |
| `Dapper` | Requested by the user: parameter binding and row mapping instead of hand-written `DbDataReader` loops. A micro-ORM — no schema management, no change tracking, no query translation — so §0.3's "SQLite, JSON documents + indexed columns" is untouched. |
| `Microsoft.Extensions.Logging.Abstractions` | The abstractions half of the already-approved logging package, so domain projects can take `ILogger<T>` without dragging the DI container in. |
| `@radix-ui/react-label`, `@radix-ui/react-alert-dialog` | shadcn/ui primitives (§0.3). The provider picker uses a styled native `<select>`, so `@radix-ui/react-select` was deliberately **not** added. |

No package was needed for the native folder picker or the secret store. PhotinoX already exposes
`ShowOpenFolder`; the three credential backends are `[LibraryImport]` bindings, which is why
`DiffHacker.Storage` — and only that project — sets `AllowUnsafeBlocks`.

### The shell: PhotinoX, not Photino.NET

§0.3 names Photino.NET. Iteration 1 established that it cannot satisfy the hard constraint of
serving the renderer through an in-process custom scheme handler: `Photino.Native` registers a
WebView2 `add_WebResourceRequested` filter but never calls
`ICoreWebView2EnvironmentOptions4::SetCustomSchemeRegistrations`, so WebView2 treats
`diffhacker://` as an unknown protocol, the handler is never invoked and the window stays
blank. That is photino.NET issue #209, closed `wontfix`; upstream has had no code commit since
2025-01-23.

**[PhotinoX](https://github.com/ivanvoyager/PhotinoX)** is the maintained fork and registers
the scheme correctly. Same `Photino.NET` namespace, targets `net10.0`, WebKitGTK 4.1 on Linux.
It is a single-maintainer project, which is the risk accepted here — mitigated by `IAppShell`,
behind which the entire dependency lives in one file.

### Things that are permanently out of scope

Listed so they are not proposed again. See
[docs/future-improvements.md](docs/future-improvements.md) for the deferred-but-wanted list.

- Branch comparison, commit ranges, merge-base diffs.
- GitHub / GitLab / Bitbucket integration.
- Language-specific static analysis (Roslyn, tree-sitter, ASTs).
- Canvas or WebGL graph renderers (Cytoscape, Sigma).
- A local HTTP server or localhost port for serving UI assets.
- Manual node dragging and persisted hand layout.
