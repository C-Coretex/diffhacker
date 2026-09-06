# CLAUDE.md — DiffHacker

Standing context for every Claude Code session here. Loaded automatically; read before
doing anything else.

§0.1–§0.6 are the **product contract** — fixed, do not re-litigate. Everything genuinely
open is in §0.4 or in [docs/iterations/](docs/iterations/).

---

## §0.1 What the product is

DiffHacker is a cross-platform desktop app for reviewing large local Git changesets.

Point it at a local repository; it diagrams the current uncommitted diff (working tree vs
`HEAD`) as **containers** (clusters of related change), each holding nodes (files, or
specific places inside files) ordered so the most important sits at top and the reviewer
walks downstream through consequences.

Hover a node → what changed and why. Hover an edge → how the relationship changed. Hover a
container → what the cluster is. Risks live in a separate column from explanations. Click a
node → opens the diff.

The graph is built **entirely by the LLM**. The app's job is to give the LLM tools to
explore the repo (search, grep, read, diff, metadata) and to render, persist and navigate
what it produces. Language-agnostic and LLM-provider-agnostic.

**Problem solved:** on a large AI-generated change, the reviewer opens 300 files
alphabetically and reconstructs the change's structure in their head. This app reconstructs
it for them.

**Audience:** developers reviewing large changes, especially AI-agent-produced ones where
100–1000+ files change at once and intent is invisible from the file list. Intended as a
polished open-source product, not a personal script.

---

## §0.2 Product invariants

Non-negotiable, apply to every iteration.

1. **LLM is the source of truth** — graph, containers, edges, ordering, explanations are all
   LLM-decided. The app never computes the dependency graph itself.
2. **The app is a toolbox for the LLM.** It gives read access to the repo/diff via tools; the
   LLM decides what to read.
3. **Language-agnostic.** No language-specific parsing (no Roslyn, no tree-sitter, no ASTs).
   Only per-language behavior: tagging a file with its detected language as metadata.
4. **LLM-provider-agnostic.** Users bring their own API key; adding a provider must not touch
   analysis logic.
5. **Every changed file appears in the graph.** Nothing dropped, hidden, or summarised away —
   validated after every run.
6. **Direct and conceptual relationships both exist.** An edge may be a real code dependency
   or an LLM-inferred conceptual/reading-flow link (explicitly allowed); the two must be
   visually distinguishable.
7. **Edges express reading flow, not call semantics** — "to understand this, read from here
   to there." No taxonomy of verbs like *calls*/*implements*/*uses*.
8. **Analysis completes before display.** Full result (graph, layout, explanations, risks,
   summary) is generated and persisted first, then shown. No progressive reveal, no
   on-demand explanation at hover time.
9. **Token economy matters.** Initial prompt = changed-file list + project context +
   instructions only. Everything else the LLM pulls via tools. Never bulk-inject file
   contents or diffs.
10. **Any changeset size must work** — 10 files or 1500.
11. **Local uncommitted changes only** (working tree vs `HEAD`). No branch/commit picker, no
    commit ranges, no GitHub/GitLab integration anywhere in this plan.
12. **Read-only, one exception.** Never commits/stages/checks out/modifies source files,
    except the opt-in doc generator in Iteration 6 (explicit confirmation + preview first).
13. **The WebView is a pure renderer.** No network/filesystem access; API keys never reach
    it. All I/O in .NET.
14. **Production quality from iteration one** — real error handling, tests, logging,
    separation of concerns.

---

## §0.3 Fixed technology decisions

| Concern | Decision |
|---|---|
| Shell | **Photino.NET** (WebView2 / WKWebView / WebKitGTK) — see [Repository conventions](#the-shell-photinox-not-photinonet) for why the actual dependency is PhotinoX |
| UI | **React 19 + TypeScript**, built with **Vite** |
| Graph | **React Flow** (`@xyflow/react` v12) |
| Layout engine | **ELK.js** (`elkjs`), `layered` algorithm, in a Web Worker |
| Diff viewer | **Monaco Editor** `DiffEditor`, bundled locally |
| UI state | **Zustand** |
| Styling | **Tailwind CSS** + **shadcn/ui** (Radix primitives) |
| Host ↔ UI protocol | **JSON-RPC 2.0** over the Photino message channel |
| Contract source of truth | **JSON Schema** in `/schema` → generated C# records + TS types |
| Git access | **git CLI** behind an `IGitClient` abstraction |
| LLM abstraction | **`Microsoft.Extensions.AI`** / `IChatClient` |
| MCP | **`ModelContextProtocol.Core`** 2.2.0 (official C# SDK; `.Core`, not the main package — see [docs/decisions.md](docs/decisions.md#dependencies-beyond-§03)) |
| Persistence | **SQLite** (`Microsoft.Data.Sqlite`), JSON documents + indexed columns |
| Secrets | `ISecretStore`, per-OS backends + encrypted-file fallback |
| Logging | Local rolling `log.txt` in the app data directory |
| .NET target | Current LTS, verified against Photino support |
| Packaging | **Velopack** (installers + auto-update) |
| Tests | xUnit (.NET), Vitest + RTL (UI), Playwright (E2E) |
| Licence | **MIT** (see §0.7) |

### Solution layout

Indicative; internal structure within each project is Claude Code's call.

```
/schema                        JSON Schema — contract source of truth
/src
  DiffHacker.slnx
  DiffHacker.Contracts         generated DTOs + hand-written value types
  DiffHacker.Core              analysis orchestration, validation, domain
  DiffHacker.Git               IGitClient + git CLI implementation
  DiffHacker.Llm               IChatClient wiring, provider registry, budgets
  DiffHacker.Tools             the toolbox the LLM explores the repository with
  DiffHacker.Mcp               diffhacker-mcp: the toolbox over stdio, headless
  DiffHacker.Storage           SQLite, analysis library, settings, secrets
  DiffHacker.Host              Photino, JSON-RPC dispatcher, composition root
  /ui                          Vite + React + TypeScript
/tests
/docs
```

Hard structural rules:

- `DiffHacker.Core` must not reference `DiffHacker.Host` — enforced by project refs + an
  architecture test.
- `DiffHacker.Tools` must run headlessly, without a window.
- `Microsoft.Extensions.AI` types must not leak into `DiffHacker.Core`.
- Photino types must not appear outside the `IAppShell` implementation.

---

## §0.4 When to ask the user, and when to decide

**Ask first when:**

- A product behaviour is unspecified/ambiguous in the iteration text.
- A decision changes what the user sees or how they interact with the app.
- A §0.3 tech decision turns out wrong/blocked/deprecated/unavailable — report the problem
  and alternatives, don't silently substitute.
- Two requirements conflict, or one conflicts with a §0.2 invariant.
- Implementing as specified would take much longer than expected, or forces a design you
  think is a mistake — say why before proceeding.
- A dependency not listed in §0.3 is needed.

**Decide without asking:** internal project structure/naming, DI wiring, which well-known
utility library to use within the fixed stack, test/fixture design, error-message copy, and
anything the iteration text already lists as open.

Batch questions into one round rather than asking one at a time. When in doubt, ask.

---

## §0.5 Terminology

| Term | Meaning |
|---|---|
| **Analysis** | One complete run: the current diff plus the LLM output produced from it. |
| **Changeset** | Working tree vs `HEAD`, including staged, unstaged and untracked non-ignored files. |
| **Container** | A cluster of interconnected change in the diagram. Unrelated changes → different containers. |
| **Node** | A file, or a specific place inside a file, participating in the change. |
| **Direct edge** | A relationship backed by an actual code-level dependency. |
| **Conceptual edge** | A relationship the LLM inferred from intent, workflow or reading order. |
| **Entry point** | The node a reviewer should start from within a container. |
| **Toolbox** | The repository-exploration tools the app exposes to the LLM. |
| **Project profile** | Stored, reusable repo knowledge, produced in Iteration 6. |

---

## §0.6 Settled product decisions

Recorded so no iteration re-opens them.

- **Node granularity:** one per file; split only for two genuinely unrelated changes in one file.
- **Node identity:** derived from file path (+ disambiguator for multiple nodes), stable across re-runs.
- **Cross-container edges:** drawn, faint styling, excluded from layout influence.
- **Colour channels:** fill = project/module; state = border style + corner badge.
- **Analysis passes:** single pass regardless of size; multi-pass deferred.
- **Partial re-analysis:** doesn't exist — always the whole changeset.
- **Repository profile:** strongly prompted but skippable, with a warning that results are weaker without it.
- **Persistence:** whole analysis written to disk, reopenable after restart.
- **Scope:** one repository per analysis, no workspaces.
- **Localisation:** English only, but no hardcoded UI strings — resource layer throughout.
- **Telemetry:** opt-in crash reports only, never repo content. Local logging always to `log.txt`.
- **Keyboard navigation:** nice-to-have, not a priority.

---

## §0.7 Naming and licence — deviations from the original plan

Decided; do not revisit.

- **Product name is `DiffHacker`**, not `ChangeGraph` — matches the repo, GitHub remote and
  `src/DiffHacker.slnx`. All projects are `DiffHacker.*`.
- **Licence is MIT**, not Apache-2.0. MIT has no `NOTICE` mechanism, so Iteration 14's
  "commercial users should credit the project" is a *request* in the About screen/README,
  not a legal obligation — don't document it as required.

---

## Working agreement

### The iteration model

14 sequential iterations, each self-contained in its own file under
[docs/iterations/](docs/iterations/README.md): goal, context, decisions, numbered
requirements, out-of-scope, done-when bar.

**One session = one iteration** — the user links/pastes one iteration file; this CLAUDE.md
is shared context, the iteration file is the work. Don't start work from a later iteration
because it looks easy or adjacent; if iteration N genuinely needs a piece of N+3, say so and ask.

### Before writing code in an iteration

1. Read the iteration file end to end, including **Raise before implementing**.
2. Batch every question from that section plus anything §0.4 covers, ask once.
3. Only then implement.

### Definition of done

- Every numbered requirement implemented, or explicitly reported not-done with why.
- The **Done when** bar demonstrably met, not assumed.
- Tests exist and pass on the platforms the iteration touches.
- CI green on Windows, macOS and Linux (from Iteration 1 on — see note below, currently deferred).
- No secret ever written to `log.txt`, to SQLite in plaintext, or across the JSON-RPC bridge
  into the WebView.

### Reporting

Report outcomes faithfully: show failing test output, say which requirement was skipped and
why. Never report an iteration complete when it's partially done.

---

## Repository conventions

### Prerequisites

.NET SDK 10 (pinned in `global.json`) + Node.js 24. `dotnet build` invokes `npm`, so Node is
needed even for backend-only work — pass `-p:SkipUiBuild=true` to opt out.

### Commands

Run from the repository root.

| Task | Command |
|---|---|
| Restore + build (codegen + UI bundle) | `dotnet build src/DiffHacker.slnx` |
| Run the app | `dotnet run --project src/DiffHacker.Host` |
| .NET tests | `dotnet test src/DiffHacker.slnx` |
| UI tests | `npm run test:run` in `src/ui` (`npm test` to watch) |
| UI type check | `npm run typecheck` in `src/ui` |
| Renderer inner loop | `npm run watch` in `src/ui`, then reload the window |
| Regenerate contracts | Automatic on build; standalone: `npm run contracts` in `src/ui` |
| E2E tests (drives the real window) | `npm test` in `tests/e2e` (`npm install` once; build the solution first) |
| E2E report/screenshots | `npm run report` in `tests/e2e`; PNGs in `tests/e2e/artifacts/screenshots/` |
| Run against throwaway state | `dotnet run --project src/DiffHacker.Host -- --data-dir <path>` |
| Serve the toolbox over MCP | `dotnet run --project src/DiffHacker.Mcp -- --repository <path>` |

> Never pass `--nologo` to `dotnet test`: under Microsoft.Testing.Platform it's forwarded to
> the test executable, which rejects it ("Zero tests ran").

### How the pieces fit

- **Contracts:** `/schema/*.schema.json` → `tools/DiffHacker.SchemaGen` → C# records
  (`src/DiffHacker.Contracts/Generated/`) + TS (`src/ui/src/contracts/`). Both gitignored,
  regenerated every build. Schema `title` = generated type name; `enum` strings = wire values.
- **Renderer:** Vite builds `src/ui` → `src/ui/dist`. Release embeds it in the host assembly;
  Debug serves from disk. Always served in-process via the `diffhacker://app/` scheme handler
  — never over HTTP.
- **Strings:** host sends error codes/resource keys, never prose. `src/ui/src/i18n/en.ts` is
  the single resource layer, compile-time checked.
- **End-to-end:** `tests/e2e` attaches Playwright to the live WebView2 window over CDP
  (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`, which `PhotinoAppShell` never overrides — no prod
  code knows the suite exists). Windows only: WKWebView/WebKitGTK have no CDP equivalent, so
  the suite skips there rather than claiming a pass it didn't earn. This is the
  whole-application gate; screenshots are evidence only.

### Conventions

- **Contracts are generated, never hand-edited.** Fix the JSON Schema in `/schema` and rebuild.
- **Schema files are versioned**; a persisted analysis records its schema version. Bumping it
  means editing `schema/contract-version.json` **and** the `/<major>.<minor>/` segment in
  every schema's `$id` (see the 1.0→1.1→1.2 bumps).
- **A `$def` must not reference another `$def`** — NJsonSchema's C# record template throws on
  nested references; only root→`$def` is supported. Flatten instead (e.g.
  `schema/changeset-result.schema.json`'s flat per-status counts). Cross-file `$ref` is
  likewise unused — shapes are duplicated per file and reconciled with an agreement test.
- **No hardcoded user-facing strings** — resource layer even though English-only.
- **Never parse human-facing git output.** Machine-readable flags only: `-z` everywhere,
  `--raw` for statuses/modes, `--numstat` for line counts. Hunk counting (the one place a
  patch stream is read) never reads a path out of it.
- **Never invoke a git subcommand that mutates the repo.** Allowlisted in
  `GitProcessRunner.PermittedSubcommands`, granted per top-level subcommand — why `submodule`
  is absent (its status query can't be allowed without also allowing `submodule update`).
  Widening it is a contract change: `grep` was added in Iteration 5 for the toolbox's search,
  and `GitProcessRunnerTests` fails until the addition is made deliberately.
- **A git subprocess never inherits our stdin.** `GitProcessRunner` redirects and closes it.
  In `diffhacker-mcp`, stdin is the live MCP protocol pipe, and a git process holding it blocked
  every call for the full timeout.
- **`git grep -z` puts NUL after *both* fields** — `<path>NUL<line>NUL<text>` — so it carries no
  match-versus-context marker and `-C` is unusable. Match positions come from git, context lines
  are read back from the file. See [docs/decisions.md](docs/decisions.md).
- **The toolbox sees only what git sees**: `ls-files --cached --others --exclude-standard`, no
  `.git/`, no gitignored files, no escape hatch. Ignored entries are counted, never concealed.
- **Text encodings**, decided once in `DiffHacker.Core.Changes.TextDecoding`: NUL in the first
  8000 bytes → binary, then BOM, then strict UTF-8, then Latin-1 (not Windows-1252, since
  `InvariantGlobalization` is on and code pages would need a package); the result records
  which was used.
- **Logging:** structured entries to rolling `log.txt` in the per-user app data dir. Redact
  secrets at the sink, not at call sites.
- **Tests:** xUnit (.NET), Vitest + RTL (UI), Playwright (E2E). Git-layer/toolbox tests run
  against fixture repos built in temp dirs (real commits, renames, untracked files). No test
  hits a real LLM provider.
- **E2E is part of the change, not an afterthought.** Any iteration adding a screen, RPC
  method, user-visible state or new failure mode extends `tests/e2e` and runs the suite before
  reporting done — it's the only thing proving the layers are wired together. See
  [tests/e2e/README.md](tests/e2e/README.md): one test per journey, no fixed sleeps,
  assertions from `en.ts`, screenshots at every meaningful step.
- **E2E runs against throwaway state, never yours.** Host launched with `--data-dir` (.NET's
  per-user data dir comes from the Win32 known-folder API; no env var redirects it). Anything
  bypassing that switch writes test providers/API keys into the developer's real secret store.

### CI is deliberately deferred

No `.github/workflows/` — don't add one back until the user asks. Consequences:

- macOS/Linux are unverified with no automated E2E coverage; everything has only run on
  Windows. WebKitGTK/WKWebView are expected to differ from WebView2 (scheme handler,
  `prefers-color-scheme`). Accepted deliberately (see self-test note below).
- Full local gate: `dotnet test src/DiffHacker.slnx`, `npm run test:run` in `src/ui`, `npm
  test` in `tests/e2e` — only the first two run outside Windows.
- `tools/ci/screenshot.ps1`/`.sh` launch the app, capture the screen, close it. Written for
  CI but work standalone as a visual check on a given platform.

### The renderer self-test was removed — don't rebuild it

Iteration 1's `--self-test` mode and its supporting demo RPC surface (`DemoRpcTarget`,
`DemoPanel.tsx`, `start-demo-*`/`progress-notification` schemas) are gone for good. Its two
non-redundant checks (CSP enforcement, contract handshake) moved to
[04-shell-guarantees.spec.ts](tests/e2e/specs/04-shell-guarantees.spec.ts). Background and
accepted cost (no macOS/Linux E2E coverage; host→renderer notifications only half covered) in
[docs/decisions.md](docs/decisions.md#the-renderer-self-test--why-it-was-removed).

**Iteration 5 built that producer and could not finish the E2E test.** `report_progress` →
`ToolProgressNotifier` → `analysis.progress` → the renderer's subscriber is complete and tested
on both sides separately (`ToolProgressNotifierTests`, `methods.test.ts`), but nothing in the
application starts an analysis, so no notification travels the real bridge into the real window
yet. **Iteration 7 runs the first analysis — add the E2E test there.**

### Dependencies beyond §0.3

§0.4 still applies — ask before adding more. Full rationale in
[docs/decisions.md](docs/decisions.md#dependencies-beyond-§03).

`StreamJsonRpc` · `NJsonSchema.CodeGeneration.{CSharp,TypeScript}` (tool-only) · `Serilog` +
`Serilog.Sinks.File` + `Serilog.Extensions.Logging` · `Microsoft.Extensions.{DependencyInjection,Logging}`
· `Shouldly` (Iteration 1) — `Microsoft.Data.Sqlite` · `Dapper` ·
`Microsoft.Extensions.Logging.Abstractions` · `@radix-ui/react-label`,
`@radix-ui/react-alert-dialog` (Iteration 2, not `@radix-ui/react-select`) —
`Microsoft.Extensions.AI` (+ `.Abstractions`, `.OpenAI`) 10.9.0 · `OpenAI` **2.12.0, not
2.13.0** · `Anthropic` (official `anthropics/anthropic-sdk-csharp` SDK, not community
`Anthropic.SDK`) · `NJsonSchema` now also at runtime (Iteration 4) —
`ModelContextProtocol.Core` **2.2.0, not the main `ModelContextProtocol` package** ·
`Microsoft.Extensions.DependencyInjection.Abstractions` (Iteration 5).

No resilience package (retry is ~60 lines in `RetryPolicy`). No package for the folder picker
or secret store (PhotinoX's `ShowOpenFolder`; `[LibraryImport]` credential bindings — why
`DiffHacker.Storage` alone sets `AllowUnsafeBlocks`).

### The shell: PhotinoX, not Photino.NET

§0.3 names Photino.NET, but it can't serve the renderer through the required in-process
custom scheme handler (`diffhacker://` stays unregistered — photino.NET issue #209,
`wontfix`). Use **[PhotinoX](https://github.com/ivanvoyager/PhotinoX)**, the maintained fork
— same `Photino.NET` namespace, `net10.0`, WebKitGTK 4.1 on Linux. Single-maintainer risk,
mitigated by `IAppShell`: the whole dependency lives in one file. Background:
[docs/decisions.md](docs/decisions.md#the-shell-photinox-not-photinonet).

### The LLM layer: two implementations, not three

Gemini, Grok, DeepSeek and user-supplied endpoints all go through OpenAI-compatible surfaces;
Anthropic is separate. `Google.Cloud.VertexAI.Extensions` (named in Iteration 4) is **not
used** — see [docs/decisions.md](docs/decisions.md#the-llm-layer-two-implementations-not-three)
for why. Two URL normalisations in `ChatClientFactory.ResolveBaseUrl`, applied to defaults and
user overrides alike:

- **Gemini** keeps `/v1beta` for model listing, needs `/v1beta/openai/` for chat.
- **Anthropic** must not be handed a `/v1` — its SDK appends its own.

Other settled decisions:

- **The tool loop is ours**, not MEAI's `FunctionInvokingChatClient` — budgets, Iteration 13's
  ordered trace, per-turn events, and rate-limit-vs-revoked-key retry logic all need to live
  inside the loop.
- **No token streaming.** `ILlmSession` emits per-turn/per-tool-call `LlmRunEvent`s (§0.2.8
  forbids half-built results anyway; Iteration 13 wants progress through turns, not characters).
- **Structured output degrades in tiers:** native `json_schema` → strict `submit_result` tool
  call → `json_object` → prompting. Every tier validates against the schema, one repair round trip allowed.
- **Budgets default to** 500 tool calls, 300 turns, 2,000,000 tokens, 10-minute request
  timeout, 5 retries. No cost ceiling by default — a mid-run kill wastes what's spent;
  Iteration 13's pre-run estimate is where an expensive run gets prevented.
- **Pricing** from bundled `src/DiffHacker.Llm/Pricing/model-prices.json` (stamped `asOf`),
  overridable per-profile. Unrated models report cost as **unknown**, never zero. The table
  is a snapshot and will go stale — refreshing it is routine maintenance.
- **Renderer can cancel a host call** via `$/cancelRequest` (StreamJsonRpc handles it).
  `callAbortable` takes an `AbortSignal`; the timeout path uses the same machinery.

`tests/DiffHacker.Llm.Live.Tests` is opt-in, skipped unless `DIFFHACKER_LIVE_*` is set, so
`dotnet test` stays offline — see its README.

### Permanently out of scope

See [docs/future-improvements.md](docs/future-improvements.md) for the deferred-but-wanted list.

- Branch comparison, commit ranges, merge-base diffs.
- GitHub/GitLab/Bitbucket integration.
- Language-specific static analysis (Roslyn, tree-sitter, ASTs).
- Canvas or WebGL graph renderers (Cytoscape, Sigma).
- A local HTTP server or localhost port for serving UI assets.
- Manual node dragging and persisted hand layout.
