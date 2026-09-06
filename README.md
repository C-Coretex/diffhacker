# DiffHacker

**Review large Git changes as a graph, not an alphabetical file list.**

DiffHacker is a cross-platform desktop app that points at a local repository, takes the
current uncommitted diff, and asks an LLM to turn it into a **directed review graph**:
clusters of related change, ordered so you start at the thing that matters and walk
downstream through its consequences.

> **Project status: pre-implementation.** The plan is complete and the repository is
> scaffolded, but no feature code exists yet. There is nothing to install. Follow the
> [roadmap](#roadmap) if you want to track progress.

---

## The problem

An AI agent just changed 300 files. Your review tool sorts them alphabetically. So you open
`src/Api/Controllers/AccountController.cs` first — not because it matters, but because `A`
comes first — and spend the next hour reconstructing the shape of the change in your head:
what drove what, which files are the point and which are fallout, what is safe to skim and
what deserves real attention.

The structure of the change exists. It is just invisible from the file list.

## What DiffHacker does

It reconstructs that structure for you, as one diagram:

- **Containers** group related change. Unrelated changes land in different containers.
- **Nodes** are files, or specific places inside files. Every changed file appears — nothing
  is summarised away.
- **Edges** are reading flow: *to understand this, read from here to there.* Solid edges are
  real code dependencies; dashed edges are conceptual relationships the LLM inferred.
- **Ranking** puts the entry point of each container at the top. You read downstream.
- **Hover** a node, edge or container for what changed, why, and how it affects the rest.
  **Risks** live in their own column, never mixed into the prose.
- **Click** a node to open the diff, with the explanation still on screen.
- **Mark nodes reviewed** as you go, so a 300-node review is survivable.

## How it works

The application does not analyse your code. It gives an LLM the tools to analyse it.

```
changed-file list + project profile + your instructions
                    │
                    ▼
          ┌─────────────────────┐
          │  the LLM explores   │◄──── toolbox: grep, glob, read, diff,
          │  the repository     │      tree, metadata, report_progress
          └─────────┬───────────┘
                    │  structured result (JSON Schema)
                    ▼
      validate → persist → lay out → render
```

The initial prompt contains only the changed-file list, project context and instructions.
Everything else the LLM pulls in itself, through tools. File contents and diffs are never
bulk-injected.

The result is validated hard before you ever see it: every changed file has a node, every
node has exactly one container, every edge resolves, every container has exactly one entry
point. On failure the specific problem goes back to the LLM for repair. Nothing is ever
silently dropped or invented to make validation pass.

## Design principles

- **The LLM is the source of truth.** The app renders and persists; it does not second-guess.
- **Language-agnostic.** No Roslyn, no tree-sitter, no ASTs. Language is metadata, nothing more.
- **Provider-agnostic.** Bring your own key. OpenAI, Anthropic, Gemini, Grok, DeepSeek, or any
  OpenAI-compatible endpoint — including local Ollama.
- **Local uncommitted changes only.** Working tree vs `HEAD`. No branch picker, no PR
  integration.
- **Read-only.** The app never commits, stages, checks out or edits your files. The one
  exception is the opt-in documentation generator, which previews every file and asks first.
- **Nothing renders until everything exists.** No half-built graphs, no explanations
  generated at hover time.
- **Any changeset size.** 10 files or 1500.

## Privacy

DiffHacker sends **portions of your source code** to whichever LLM provider you configure —
the changed-file list up front, then whatever the model reads through the toolbox. That is
how it works, and the app states it explicitly before your first analysis.

Your API keys are encrypted with AES-GCM in your application data directory, under a master
key held by your operating system's credential store — DPAPI on Windows, Keychain on macOS,
libsecret on Linux. On systems with no keyring daemon the master key is derived from the
machine and your user account instead, and the app says so rather than claiming a keyring it
does not have. Keys never cross into the WebView. Crash reporting is opt-in only and never
includes repository content. Local logs go to `log.txt` in your application data directory,
with secrets redacted.

## Tech stack

| Concern | Choice |
|---|---|
| Shell | PhotinoX (WebView2 / WKWebView / WebKitGTK) |
| UI | React 19 + TypeScript, Vite |
| Graph | React Flow (`@xyflow/react` v12) |
| Layout | ELK.js `layered`, in a Web Worker |
| Diff viewer | Monaco `DiffEditor`, bundled locally |
| State / styling | Zustand · Tailwind CSS · shadcn/ui |
| Host ↔ UI | JSON-RPC 2.0 over the Photino message channel |
| Contracts | JSON Schema in `/schema` → generated C# + TypeScript |
| Git | `git` CLI behind `IGitClient` |
| LLM | `Microsoft.Extensions.AI` / `IChatClient` |
| Tools | `ModelContextProtocol.Core` C# SDK — one definition, usable in-process **and** over stdio by other agents |
| Storage | SQLite |
| Packaging | Velopack, self-contained per RID |

## Repository layout

```
/schema                  JSON Schema — the contract source of truth
/src
  DiffHacker.slnx
  DiffHacker.Contracts   generated DTOs + value types
  DiffHacker.Core        analysis orchestration, validation, domain
  DiffHacker.Git         IGitClient + git CLI implementation
  DiffHacker.Llm         provider registry, sessions, budgets
  DiffHacker.Tools       the LLM toolbox: the ten tools it explores a repository with
  DiffHacker.Mcp         diffhacker-mcp — the same toolbox over stdio, headless
  DiffHacker.Storage     SQLite, analysis library, settings, secrets
  DiffHacker.Host        Photino, JSON-RPC dispatcher, composition root
  /ui                    Vite + React + TypeScript
/tests
/docs
  /iterations            the implementation plan, one file per iteration
```

## Use the toolbox from your own agent

The tools DiffHacker gives its model are not private to it. `diffhacker-mcp` serves the same ten
tools over stdio to any MCP client:

```
dotnet build src/DiffHacker.slnx
claude mcp add diffhacker -- <repo>/src/DiffHacker.Mcp/bin/Release/net10.0/diffhacker-mcp --repository /path/to/your/repo
```

It is read-only and offline by construction: no write path, no command execution and no network
access exist anywhere in the toolbox — an architecture test asserts the absence rather than the
disuse. It sees only what git sees, so `.git/` and everything `.gitignore` covers are invisible,
and every result is capped and paged so a single call cannot flood a context window.

## Roadmap

Fourteen sequential iterations. Full requirements for each are in
[docs/iterations/](docs/iterations/).

| # | Iteration | Delivers |
|---|---|---|
| [1](docs/iterations/iteration-01-foundation.md) | Foundation | Photino shell, JSON-RPC bridge, schema codegen, CI on 3 OSes |
| [2](docs/iterations/iteration-02-shell-settings-repository.md) | Shell, settings, repository | Repo picker, provider config, secret store |
| [3](docs/iterations/iteration-03-git-layer.md) | Git layer | The changeset: working tree vs `HEAD`, untracked included |
| [4](docs/iterations/iteration-04-llm-provider-layer.md) | LLM providers | One contract, five providers, tool calling, budgets |
| [5](docs/iterations/iteration-05-repository-toolbox.md) | Repository toolbox | The tools the LLM explores with — the heart of the product |
| [6](docs/iterations/iteration-06-repository-knowledge-base.md) | Knowledge base | Project profile, custom instructions, opt-in doc generator |
| [7](docs/iterations/iteration-07-analysis-pipeline.md) | Analysis pipeline | The validated, persisted graph result |
| [8](docs/iterations/iteration-08-graph-rendering.md) | Graph rendering | The diagram |
| [9](docs/iterations/iteration-09-explanations.md) | Explanations | Hover cards, risks column, overview panel — **MVP line** |
| [10](docs/iterations/iteration-10-diff-viewer.md) | Diff viewer | Monaco, graph-following navigation, reviewed tracking |
| [11](docs/iterations/iteration-11-grouping-modes.md) | Grouping modes | Dependency flow vs change clusters |
| [12](docs/iterations/iteration-12-unchanged-intermediate-nodes.md) | Unchanged nodes | Optionally reveal the files in between |
| [13](docs/iterations/iteration-13-cost-transparency-library.md) | Cost & library | Estimates, tool-call inspector, analysis history |
| [14](docs/iterations/iteration-14-packaging-release.md) | Packaging | Signed installers, auto-update, docs, first run |

**Iterations 1–9 are the MVP:** local diff → LLM-built grouped graph → explanations.

Deliberately deferred ideas are recorded in
[docs/future-improvements.md](docs/future-improvements.md) so they are not forgotten and not
accidentally built early.

## Contributing

Not yet open for contributions — the foundation has to exist first. When it is,
`CONTRIBUTING.md` will land alongside Iteration 14.

If you are working on this repository with an AI coding agent, start with
[CLAUDE.md](CLAUDE.md). It carries the full product contract, the invariants, and the rules
about what the agent decides on its own versus what it must ask about.

## Licence

[MIT](LICENSE).

Attribution is appreciated but not legally required. If DiffHacker is useful in your product
or workflow, a credit and a link back are the ask.
