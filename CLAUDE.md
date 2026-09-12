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

Click a node → what changed and why. Click an edge → how the relationship changed. Click a
container's title bar → what the cluster is. Risks live in a separate column from explanations.
Double-click a node → opens the diff. The diff editor's working-tree side is directly editable —
saving there writes the file, same as an external editor would (see §0.2.12).

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
12. **Read-only, with two exceptions.** DiffHacker never commits, stages or checks out anything, and
    the LLM analysing a change never edits a file — `IGitClient` stays read-only and the toolbox
    cannot write to disk at all (`ToolboxSandboxTests`). Two write paths exist, both gated on the
    reviewer's own explicit action and neither reachable from analysis: the opt-in doc generator
    (Iteration 6; explicit confirmation + preview first — generated documentation lives **in
    DiffHacker**, not in the repository, until that confirmed export writes it out); and saving an
    edit made directly in the diff editor's working-tree side (an ordinary text edit to the one file
    already open, gated on the text the editor started from — the write is refused if the file on
    disk no longer matches it, rather than behind a confirmation dialog). `RepositoryWriteTests`
    asserts that exactly these two files are the only write paths in `src/`.
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
| Refresh the user guide's screenshots | `npm run docs:screenshots` in `tests/e2e` (build first, rebuild after) |
| Regenerate `docs/user-guide.md` | `npm run docs:guide` in `src/ui` |

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
  every schema's `$id` (see the 1.0→1.1→1.2→1.7 bumps). A `sed` over `schema/*.schema.json` is the
  intended way to do it; each file carries the segment exactly once.
- **A `$def` must not reference another `$def`** — NJsonSchema's C# record template throws on
  nested references; only root→`$def` is supported. Flatten instead (e.g.
  `schema/changeset-result.schema.json`'s flat per-status counts). Cross-file `$ref` is
  likewise unused — shapes are duplicated per file and reconciled with an agreement test.
- **An inline enum needs a `title` too.** An enum inside an `items` block is named after the property
  otherwise, so two schemas both holding `states` would generate two types called `States` and fail
  to compile. Give it a title, as `analysis-result.schema.json` gives `AnalysisNodeState`.
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
- **The analysis prompt is the product, not boilerplate.** `AnalysisPrompt.SystemPrompt()` is
  organised around what the reviewer does with the answer — start here, read down — and says in its
  own section that **this is not a dependency graph**: a cluster held together by intent alone, with
  no import between its members, is the case the product exists for. `AnalysisPromptTests` pins those
  ideas so the prose can be improved but not quietly dropped. It asserts against a whitespace-collapsed
  copy, so rewrapping a paragraph never fails a test and nobody has to contort the prose to keep one
  green. From Iteration 11 it is assembled from sections rather than written as one literal, because
  two of them come in two versions — and it states the two groupings' *conflict* rather than
  describing grouping twice and hoping for two different answers.
- **The prompt owns the guidance; a schema description owns its field.** Both reach the model on
  *every* request — the schema as the response format — so anything said at length in both is paid
  for twice a turn, up to 300 times a run. Guidance was duplicated into the schema descriptions once
  and cost ~4,000 characters a turn to say what the prompt had already said. A schema description
  states what the field is and the constraint the validator checks; persuasion belongs in the prompt.
  The same rule governs `ProfilePrompt` and `project-profile-document.schema.json`.
- **Anything re-sent per turn is measured, not eyeballed.** The fixed preamble of an analysis request
  is the system prompt + the response schema + the ten tool descriptions: ~7,400 tokens, before a
  single changed-file row — and the schema goes twice, as the response format and again through
  `StructuredOutput.PromptSuffix`, which is why Iteration 11's opt-out strips it rather than
  discarding the answer. `list_changed_files` at 150 rows a page and `ToolText`'s byte caps exist
  for the same reason. Tool descriptions have a 200-character floor (`ToolboxCatalogTests`) — that is
  a floor, not a target, and each still says what its tool will not do and which tool to use instead.
- **Prose is a Markdown subset we own, drawn by one component.** `lib/markdown.ts` parses exactly
  what the prompt's Formatting section names (bold, italic, code, lists, fenced code; `*` only, never
  `_`) into plain objects; `components/analysis/Markdown.tsx` draws them. No package, no HTML, no
  `href`. A risk is `InlineMarkdown` only. A code span or link naming a node's path opens its diff
  through `MarkdownReferences`. Anywhere prose is clamped or used as a hint rather than drawn goes
  through `plainText()`. Widen the grammar in both places or neither. See
  [docs/decisions.md](docs/decisions.md#formatted-prose).
- **The analysis result is the model's, unedited.** `AnalysisValidator` reports; it never corrects.
  Errors go back into the same conversation (`LlmConversation.ResultValidator`, two rounds) and then
  fail the run; warnings — cycles above all — travel with the stored result. Nothing partial is ever
  written: `IAnalysisStore` has no method that could express it. See
  [docs/decisions.md](docs/decisions.md#the-analysis-pipeline).
- **A node id keeps its file path as its whole prefix** — `src/Cache.cs`, or `src/Cache.cs#eviction`
  when one file holds two unrelated changes. §0.6's "stable across re-runs" is enforced by
  `AnalysisNodeId`, not hoped for.
- **Reviewed marks are node ids on the analysis row**, in schema 6's nullable `reviewed_json`;
  schema 7's nullable `grouping_mode` and schema 9's nullable `options_json` (what the run asked for,
  written once with the row) sit beside them. `SetNodesReviewedAsync` and
  `SetGroupingAsync` are the only two methods on `IAnalysisStore` that change a stored analysis, and
  **neither can touch the document** — both write the reviewer's own state, never the model's answer.
  `DeleteOneAsync` removes a run whole, row and all, and cannot touch one either. A re-run writes a
  new row and so starts with nothing marked and no grouping chosen; that is deliberate and pinned by
  `SqliteAnalysisStoreTests`. See [docs/decisions.md](docs/decisions.md#reading-the-code).
- **Which analysis is the caller's to say.** `analysis.get`, `setGrouping` and `setReviewed` take an
  optional `analysisId` — absent means the latest — and `trace`, `checkFreshness` and `delete` take it
  required (`AnalysisRefRequest`). The renderer always sends `view.analysisId`, so an earlier run
  reopened from the library is regrouped and marked as itself. An id is honoured only for its own
  repository; nothing about which run is open is held host-side. The library
  (`ListSummariesAsync`) never reads `document_json`, and the trace is its own call rather than a
  field on `AnalysisView`, which travels on every read. Retention is
  `AnalysisLibraryPolicy.RetentionLimit`. See
  [docs/decisions.md](docs/decisions.md#run-transparency-and-the-analysis-library).
- **Stale means the working tree differs, measured by content.** A run records a SHA-256 of each
  changed file's working-tree bytes (`ChangesetQuery.HashContent` → `ChangedFileFacts.ContentSha256`,
  in `files_json`, so no migration); `AnalysisFreshnessCalculator` compares HEAD, the path set and
  those hashes, and any difference is stale — so a reverted edit is fresh again, which an mtime could
  not say. Hashing is .NET file reading in the git layer, never `git hash-object` (it can write the
  object database). Older analyses fall back to line counts, or HEAD only, and the banner says so.
  The renderer checks **after** an analysis is drawn and on window focus, never before, so reopening
  stays instant.
- **The result holds two groupings of one node set; a view holds one of them.**
  `dependencyContainers`/`dependencyReadingOrder` keep a complete change path intact even across
  concerns; `clusterContainers`/`clusterReadingOrder` regroup the same nodes by theme. Four flat root
  properties rather than a list of groupings, because a `$def` may not reference another one — ask
  for one through `AnalysisResult.For(grouping)`. §0.2.5 applies to **both**: `AnalysisValidator`
  runs every cluster, entry-point and reading-order rule once per grouping, and each message names
  which. `AnalysisWire.ToWire(analysis, grouping, …)` is where a grouping is chosen, and
  `analysis.setGrouping` never touches the runner.
- **Order is a list's order.** A container's `nodeIds` *is* the reading order, entry node first;
  `rank` and the `entry_point` state are gone from the model's answer, because neither could describe
  two groupings, and the wire derives both per grouping so the renderer never noticed. That deleted
  three error codes rather than adding any — see
  [docs/decisions.md](docs/decisions.md#grouping-modes).
- **Every optional part is absent from the request when it is off.** `AnalysisRunOptions` names them:
  the second grouping, implementation groups, risks, node/edge/cluster explanations, plus verbosity
  (brief — the default — medium, detailed). `AnalysisPrompt.SystemPrompt(options)` drops a part's
  guidance and adds one line to a `NOT WANTED ON THIS RUN` section for the parts a model would
  otherwise volunteer. `AnalysisResponseSchema.For(options)` drops its fields and scrubs any
  description that mentions it. This matters twice over, because `StructuredOutput.PromptSuffix`
  sends the schema in the prompt *as well as* as the response format. Verbosity changes the prompt's
  numbers (`AnalysisFieldBudgets.For`) and the validator's length warning, never the schema.
  **Defaults live in Settings** (`AnalysisDefaults`, `analysis.getDefaults`/`saveDefaults`). A run's
  own options travel on `analysis.run` and are never remembered: the renderer forgets them once the
  run succeeds, and the host never stores them as defaults. What a run asked for is stored in schema
  9's nullable `options_json` (`Analysis.Requested`) and reported as `AnalysisView.*Produced`. The
  renderer reads those flags through `AnalysisPartsProvider` and leaves out what nobody asked for; it
  never shows it as empty. With one grouping, an analysis reports that in
  `AnalysisView.availableGroupings`, and the other control is disabled with its reason.
  `LegacyAnalysisDocument` gives a pre-1.10 document the same shape. See
  [docs/decisions.md](docs/decisions.md#analysis-parts).
- **Implementation groups are the model's declaration; merging them is the renderer's.** The app
  cannot tell an interface from any other file (§0.2.3), so `implementationGroups` — an abstraction
  and the nodes implementing it — is part of the model's answer, optional in the request exactly like
  the second grouping (`AnalysisPrompt.SystemPrompt(_, false)`, `AnalysisResponseSchema.For(_, false)`).
  **Null means not asked, empty means none**, and the toolbar says which. `AnalysisWire` projects each
  group onto the active grouping's container; `graph/implementationGroups.ts` turns it into one ELK
  leaf when the reviewer's `localStorage` toggle is on. Every row of a merged box is still its node —
  test id, card, diff, reviewed mark — and a split group is a warning, never a repair round. See
  [docs/decisions.md](docs/decisions.md#implementation-groups).
- **The renderer never composes a command line.** `editor.open` takes an editor, a file and a line;
  whether that becomes `--diff` or `--goto` is decided host-side from which sides of the file exist.
  `HeadBlobExtractor` is the ninth entry on `RepositoryWriteTests.Allowed` and writes only under
  `AppPaths.DiffCacheDirectory` — not one of §0.2.12's two repository write paths.
- **The diff editor's Save writes to the working tree, nowhere else.** `MonacoDiff` leaves the
  committed (`HEAD`) side permanently `originalEditable: false`; only the working-tree side can be
  typed into, and only `RepositoryWorkingTreeWriter` (`src/DiffHacker.Host/Editor/`) ever writes what
  was typed to disk, via `changeset.saveFileContent`. The gate is the exact text the editor started
  from (`SaveFileContentRequest.expectedContent`), decoded the same way `changeset.fileContent`
  decoded it, not a hash: a mismatch against what changeset.fileContent last handed the editor refuses
  the write (`changeset_save_conflict`) rather than overwrite a change made outside DiffHacker.
  `TextDecoding.Encode` mirrors `Decode` so a save round-trips the file's own encoding — BOM included —
  instead of silently rewriting it as UTF-8. Every gesture that would discard an unsaved edit (closing
  the panel, opening another file or a whole cluster, `j`/`k`, `Escape`) is a call into one of three
  guarded store actions (`openDiffFor`/`openContainerDiff`/`closeDiff` in `appStore.ts`); each defers
  into `diffPendingNavigation` when `diffDirty` is set rather than performing the navigation, and
  `DiffPanel` renders the one confirmation dialog that covers all of them. See
  [docs/decisions.md](docs/decisions.md#the-diff-editors-save-is-the-second-write-path).
- **Monaco needed no CSP relaxation, and must not be given one.** `monaco/setup.ts` imports
  `editor.api` plus the `basic-languages` contributions — never the barrel or `editor.main`, which
  drag in four language services and their workers. The one worker left is `editor.worker`, built as
  a classic IIFE like the ELK one. `09-diff-review.spec.ts` asks the engine rather than assuming.
- **A box on the diagram acts through `GraphActionsContext`, never through the store directly.** Its
  buttons — open the diff, hand the file to an editor, mark it read — are rendered three hundred times
  over, so the surface asks the store and the host *once* and passes the answers down; a `useAppStore`
  call inside `FileNode` is twelve hundred subscriptions for a row that is invisible until hovered.
  Every action stops its click and dismisses the pinned card first, or the click Iteration 9 spent on
  keeping that card would drop it over the panel that just opened.
- **A cluster is a unit of reading, not only of layout.** "Open every file" loads the whole container
  into the panel as a queue ordered by the analysis's own reading order, and previous/next then walk
  that queue. The queue ends when the reviewer leaves it or follows an edge out of the cluster —
  `openDiffFor` keeps `diffContainerId` only for a node inside it.
- **Help is the catalogue, drawn twice.** Every word of the Help screen — the fifteen-step guide, the
  diagram reference, shortcuts, FAQ, troubleshooting — is in `en.help`, in the Markdown subset
  `lib/markdown.ts` parses. `help/userGuideMarkdown.ts` renders the same strings into
  `docs/user-guide.md`, which is **generated**: edit `en.ts`, then `npm run docs:guide`; a Vitest file
  snapshot fails when the two differ. The step order and screenshot names are `help/guideSteps.ts`,
  which has no imports so the E2E suite can read it. The screenshots in `help/screenshots/` are
  **generated too**, by `15-user-guide.spec.ts` following the guide against the real app. Change a
  screen a step shows, re-run `npm run docs:screenshots` and look at every picture. The copy makes
  claims about the product — limits, paths, what is sent to a provider — so a change to one of those is
  a change to `en.help` as well. Help asks the host nothing, and `closeHelp` returns to the screen it
  was opened from. See [docs/decisions.md](docs/decisions.md#user-guide-and-help).
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
  bypassing that switch writes test providers/API keys into the developer's real secret store. The
  WebView2 profile has the same trap — PhotinoX finds it through the known-folder API too — so the
  harness sets `WEBVIEW2_USER_DATA_FOLDER`; without it every test shared the developer's
  `localStorage`.

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

**Iteration 6 closed that gap.** `profile.generate` is the first thing in the application that
starts an LLM conversation, so `report_progress` → `ToolProgressNotifier` → `analysis.progress` —
and the tool log on `analysis.toolCall` beside it — now travel the real bridge into the real window
in [05-repository-profile.spec.ts](tests/e2e/specs/05-repository-profile.spec.ts). It drives a
scripted OpenAI-compatible endpoint on localhost (`tests/e2e/src/stubProvider.ts`); no test reaches
a real provider. **Iteration 7 reuses it** in
[06-analysis-pipeline.spec.ts](tests/e2e/specs/06-analysis-pipeline.spec.ts), where
`stubAnalysisResult` builds a valid answer for whichever files the fixture changed — the same
spec drives a three-file change and a five-hundred-file one, and §0.2.5 means the answer has to
name every path either way. **Iteration 8 adds
[07-graph-rendering.spec.ts](tests/e2e/specs/07-graph-rendering.spec.ts)** — the only place the whole
diagram stack is exercised together: a stored analysis, read over the real bridge, laid out by a real
Web Worker in a real WebView, drawn, searched, collapsed and dragged. **Iteration 9 adds
[08-explanations.spec.ts](tests/e2e/specs/08-explanations.spec.ts)**, which is not a nicety either:
jsdom draws no React Flow edges at all and never finishes positioning a Radix popover, so hovering a
line, a card staying beside its node rather than over it, and the clipboard working from a custom
scheme are all things only a real window can show. **Iteration 10 adds
[09-diff-review.spec.ts](tests/e2e/specs/09-diff-review.spec.ts)** for the same reason, more sharply:
Monaco measures a DOM that has no layout, so every unit test mocks it and asserts what it was *asked*
for. Whether a real editor starts — in a WebView, from a custom scheme, with a classic worker, under
a policy granting no `unsafe-eval` — has exactly one place it can be answered, and that spec also
arms a `securitypolicyviolation` collector across six editors to prove the policy was never widened.
**Iteration 11 adds [10-grouping-modes.spec.ts](tests/e2e/specs/10-grouping-modes.spec.ts)** to answer
the one question the design turns on: requirement 2 says switching grouping must not re-run the
analysis, and the only honest check is counting what the provider was asked for — across a switch, a
switch back, and a restart onto the same stored state. `RepoSet.layered()` gives it a change that
genuinely spans database, auth and API, so "one cluster here, three there" is a fact about the
fixture rather than about the stub. **Implementation groups add
[11-implementation-groups.spec.ts](tests/e2e/specs/11-implementation-groups.spec.ts)**: that a click on
a merged box reaches the right row is a question about React Flow's real DOM, and that the toggle
spends nothing is only provable by counting provider requests. `stubAnalysisResult` answers
`implementationGroups: []` by default; a spec that turns the request off sets it to `undefined`.
**The run library adds [12-run-library.spec.ts](tests/e2e/specs/12-run-library.spec.ts)**: the live
strip read during a run that is genuinely in flight (a `hangs()` turn, so the clock is seen to move
through a silence), the inspector's rows checked against the calls the stub scripted, three runs
listed after a restart and an earlier one reopened with the provider's request count unchanged, and
a real file edited on disk — same line counts, so only the content hash can see it — then put back.
Freshness is waited on through the analysis surface's `data-freshness` attribute, because "fresh" is
otherwise the absence of a banner, and an absence cannot be waited for. **Analysis parts add
[14-analysis-parts.spec.ts](tests/e2e/specs/14-analysis-parts.spec.ts)**. Defaults are saved in
Settings; the wire is checked to carry neither the switched-off fields nor their guidance, and to
carry the "not wanted" line instead; the screen is checked to leave out what nobody asked for. A
one-off override is then sent and forgotten, while Settings keeps what it was told. `withoutParts`
strips a stub answer to match, because the stripped schema forbids the fields. **The user guide adds
[15-user-guide.spec.ts](tests/e2e/specs/15-user-guide.spec.ts)**. It follows Help's fifteen steps
for real against `guideFixture.ts`, a small shop repository built to be read, and photographs each
one through `GuideCamera`, which redacts the temp path and port and refuses an image showing the user
name. It also checks that Help is reachable from every screen and that every step's image actually
loaded over `diffhacker://`.

> The stub answers `stream: true` with server-sent events, because `LlmSession` streams every
> request. A stub that only sent one JSON body read as a provider returning an empty message, and
> that had been failing every analysis and profile journey in the suite. See
> [docs/decisions.md](docs/decisions.md#the-stub-provider-speaks-server-sent-events).

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
`Microsoft.Extensions.DependencyInjection.Abstractions` (Iteration 5) — **Iterations 6 and 7 added
none.** Iteration 7's graph walk (cycles, longest chain, fan-in/out) is Tarjan's algorithm in one
file rather than a graph package, and the validator is plain LINQ over the model's answer.
`DiffHacker.Core` gained the already-approved `Microsoft.Extensions.Logging.Abstractions` when the
profile orchestrator landed there; the unified diff behind the export preview is sixty lines rather
than a package, and Monaco stays in Iteration 10.

**Iteration 8** added `@xyflow/react` and `elkjs`, both already named in §0.3, plus
`@radix-ui/react-popover` — within the fixed shadcn/Radix stack, for the legend and the context
breakdown. Nothing else: the palette is CSS variables, the layout worker is twenty lines of
`postMessage`, and the search is `String.includes`.

**Iteration 9 added none.** The hover cards reuse `@radix-ui/react-popover` for anchoring and
collision handling rather than pulling in a hover-card or floating-ui package; the hover timing is a
forty-line hook; the edge hit areas are React Flow's own `interactionWidth`; and copying a path is
`navigator.clipboard` with an `execCommand` fallback.

**Iteration 10** added `monaco-editor`, already named in §0.3, and nothing else. Not
`@monaco-editor/react` — it loads Monaco from a CDN unless reconfigured, which §0.2.13 and the CSP
both forbid, and what survives that configuration is about as much code as `MonacoDiff.tsx`. No
resizable-panel package either: the splitter is one pointer capture, one clamp and one callback. The
diff itself is Monaco's, the language is resolved by Monaco's own extension table, and the graph
navigation is built from `AnalysisView.edges` the renderer already holds.

**Iteration 11 added none.** The grouping picker is `ThemePicker`'s pattern — a `role="group"` of two
`aria-pressed` buttons — rather than `@radix-ui/react-toggle-group`; the schema variant is thirty
lines of `System.Text.Json.Nodes` over the one schema in `/schema`; and the legacy-document upgrade
is the same, rather than a migration framework.

**Analysis parts added none.** The run options reuse `@radix-ui/react-popover`, the verbosity picker is
`GroupingPicker`'s pressed-button pattern, and the schema variants are the same `System.Text.Json.Nodes`
code the second grouping already used.

**Help and the user guide added none.** The section picker is the pressed-button group again, the FAQ
is native `<details>`, the stepper is store state and two buttons, and `docs/user-guide.md` is kept
current by Vitest's own `toMatchFileSnapshot`.

**The run library and staleness added none.** The content hash is `System.Security.Cryptography`,
the library's numbers are SQLite's own `json_extract`, the History list reuses
`@radix-ui/react-popover` and its delete confirmation the existing alert dialog, and the elapsed clock
is a ten-line hook.

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
- **Budgets default to** 500 tool calls, 300 turns, 10,000,000 tokens, 10-minute request
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
