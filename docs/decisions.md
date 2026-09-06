# Decisions — background and rationale

This file holds the *why* behind facts that CLAUDE.md states without justification. CLAUDE.md
is loaded every session and stays lean; this file is for when you need the backstory behind
a decision — a deviation from §0.3, a dependency choice, a removed feature.

Nothing here overrides CLAUDE.md. If the two conflict, CLAUDE.md wins and this file is stale.

---

## The shell: PhotinoX, not Photino.NET

§0.3 names Photino.NET, but it can't satisfy the hard constraint of serving the renderer
through an in-process custom scheme handler: `Photino.Native` registers a WebView2
`add_WebResourceRequested` filter but never calls
`ICoreWebView2EnvironmentOptions4::SetCustomSchemeRegistrations`, so WebView2 treats
`diffhacker://` as an unknown protocol, the handler is never invoked, and the window stays
blank. That's photino.NET issue #209, closed `wontfix`; upstream has had no code commit since
2025-01-23.

[PhotinoX](https://github.com/ivanvoyager/PhotinoX) is the maintained fork and registers the
scheme correctly — same `Photino.NET` namespace, targets `net10.0`, WebKitGTK 4.1 on Linux.
It's a single-maintainer project, which is the risk accepted here, mitigated by `IAppShell`:
the entire dependency lives behind that one file.

## The LLM layer: two implementations, not three

Iteration 4 said five providers collapse to three implementations and named
`Google.Cloud.VertexAI.Extensions` for Gemini. They collapse to two, and that package is not
used: it's prerelease-only (`1.0.0-beta08`) and targets Vertex AI, which needs a GCP project
and application-default credentials — not the plain Gemini API key the provider form collects.

Gemini instead goes through Google's own OpenAI-compatible surface at
`https://generativelanguage.googleapis.com/v1beta/openai/`, sharing the OpenAI SDK path with
Grok, DeepSeek and any user-supplied endpoint. It supports tool calling, `json_schema` and
usage reporting; Google labels it beta, and a few Gemini-only controls are unreachable through
it. The alternative was a third-party 0.x package on the product's critical path.

Two URL normalisations follow from this, both in `ChatClientFactory.ResolveBaseUrl`, both
applied to a user-supplied override as well as to the default:

- **Gemini** keeps `/v1beta` for the model listing but needs `/v1beta/openai/` for chat.
- **Anthropic** must not be handed a `/v1`; its SDK appends its own.

## The renderer self-test — why it was removed

Iteration 1 built a `--self-test` mode: the host launched, the renderer verified the bridge
from inside the page, reported a verdict through `host.reportSelfTest`, and the process
exited 0 or 1. It's gone, deliberately, along with the demo RPC surface that existed to feed
it — `DemoRpcTarget`, `DemoPanel.tsx`, and the `start-demo-*` and `progress-notification`
schemas.

Why: `tests/e2e` covers everything it covered except two checks, and both moved to
[04-shell-guarantees.spec.ts](../tests/e2e/specs/04-shell-guarantees.spec.ts) —
Content-Security-Policy enforcement and the contract handshake. Driving them from a test
rather than from app code is strictly better, because the self-test was test code compiled
into the shipped renderer bundle, plus an RPC surface and a CLI mode that existed only for it.

The cost the user accepted:

- macOS and Linux have no automated end-to-end coverage. The self-test was the only thing
  that could ever have run there, since Playwright needs CDP and so needs WebView2. Its
  "redundant" checks were the ones that would have caught a broken Keychain or libsecret
  backend. The user does not want cross-platform verification, so this is settled.
- Host→renderer notifications are only half covered. The host side stays proven by
  `RpcBridgeTests.A_target_can_push_notifications_back_to_the_renderer`, which uses a target
  local to the test; the renderer side is proven only by `client.test.ts` against a fake
  transport. Nothing exercises a real notification travelling the real bridge into the real
  window, because after the demo target's removal nothing sends one. Iteration 5's
  `report_progress` is the first real producer — add an end-to-end test for it there.

## The toolbox: what the LLM can and cannot see

Iteration 5 decisions that shape every later prompt, settled with the user before implementing.

### `git grep`, and the allowlist widening it cost

Requirement 4 forbids command execution, and a repository-wide regex search needs an engine. The
three candidates were an external tool such as ripgrep (an unlisted dependency, and exactly the
command execution the toolbox is forbidden), .NET's own `Regex` over a streamed file list
(correct, but reimplementing the `.gitignore` traversal git already does), and `git grep`.

`git grep` won, which meant adding `grep` to `GitProcessRunner.PermittedSubcommands`. CLAUDE.md
calls that "a change to the product's contract, not a detail", so: `grep` has no mutating form at
all — unlike `submodule`, there is no sibling command the grant drags along — and it starts no
external program provided `--no-textconv` is passed, which `GitClient.CommonGrepOptions` does.
`GitProcessRunnerTests.The_allowlist_contains_only_read_only_subcommands` failed when it was
added, which is the guard working.

One consequence is worth knowing before touching the search code. `git grep -z` replaces **both**
field separators with NUL, not just the one after the path, so the record is
`<path>NUL<line number>NUL<line>`. That erases the `:`-versus-`-` marker distinguishing a matching
line from a context line, which means `-C` returns lines with no way to tell which of them
matched. Dropping `-z` to recover the marker would mean parsing quoted, escaped,
`core.quotePath`-dependent paths, which CLAUDE.md forbids outright. So git supplies match
positions — which only it can determine — and `SearchTools` reads the lines around them back from
the file. `GitClientSearchTests` pins the byte format.

Regex dialects are exposed as `fixed`, `extended` and `perl`, because a model writes `\d` by
habit and POSIX extended has no such thing. `perl` needs a git built with PCRE; where there is
none, the search runs as `extended` and the result header says so rather than failing.

### The visible set: git-tracked only

Every tool's field of view is one call — `git ls-files --cached --others --exclude-standard -z` —
taken once per session. Tracked files plus untracked files `.gitignore` does not cover, and
nothing else. `.git/` is refused by name at any depth.

This is a token-economy decision before it is a security one: on a typical JavaScript repository
`node_modules` alone would make `find_files` and `get_repository_tree` useless and could burn
several hundred thousand tokens on a single call. There is deliberately **no** `include_ignored`
escape hatch, because that is the cheapest possible way to do exactly that by accident.

The cost is that a model can be misled into thinking a directory is empty when it is merely
expensive. So ignored entries are counted, not concealed: `list_directory` and
`get_repository_tree` append "N more entries are present but ignored by git", and
`get_path_info` answers "exists as a file but is ignored by git" rather than "not found" — the
only tool that draws that distinction, and the reason it exists.

Deriving directories from the flat path list rather than from the filesystem keeps one definition
of what is visible. Its one visible consequence is that an empty directory never appears, which
costs nothing: git does not track those either.

### Result caps

`LlmBudget` defaults to 500 tool calls and 2,000,000 tokens for a whole run, so the *average*
tool result has to land near 4,000 tokens for a long analysis to fit. The numbers in
`ToolboxLimits` put a typical page at 5–25 KB (roughly 1–6k tokens) and set a hard ceiling of
48 KiB — about 12k tokens — that no result may cross whatever its own page size says. The ceiling
is meant to be a rare event rather than a routine spend.

Results are plain text, not JSON: the same information costs roughly twice the tokens once every
key is quoted and repeated per row, and §0.2.9 makes token economy an invariant. `ToolText` is
the only thing that enforces a cap or writes a truncation marker, so the marker has one spelling
and cannot drift between nine tools.

Continuation tokens are opaque and carry a fingerprint of the query that produced them. A cursor
that read as `offset=40` is a cursor a model will edit, and an edited offset against a different
query returns the wrong rows silently; pairing one with a different search is refused instead.

### One definition, two consumers

Each tool is one `[McpServerTool]`-attributed method. `ToolboxCatalog` scans those methods once
and produces both `McpTools`, which the stdio server serves, and `LlmTools`, the provider-agnostic
`LlmToolDefinition` the analysis pipeline will run.

The two go through different SDK factories, and that is deliberate rather than incidental:
`McpServerTool.Create` knows a method returning `string` is text and emits it as text, where
routing the MCP path through an `AIFunction` first JSON-encodes it and hands the model a quoted,
backslash-escaped wall. That nearly shipped;
`StdioServerTests.Tool_results_arrive_as_plain_text_not_as_encoded_json` exists because of it.
What keeps the pair one tool rather than two is the shared `MethodInfo` plus
`ToolboxCatalogTests`, which asserts they agree on name, description and argument schema for
every tool.

### The standalone server is its own executable

`src/DiffHacker.Mcp` builds `diffhacker-mcp`, rather than the toolbox being a `--mcp-stdio` mode
of the host. The host becomes a windowed application in Iteration 14, and a windowed subsystem
has no usable stdout — which is the entire transport. Keeping them apart also means an external
agent running the toolbox never loads PhotinoX or a native WebView, and
`LayeringTests.No_domain_assembly_references_Photino` asserts it.

It logs to stderr, which is where MCP reserves server diagnostics. MCP's own logging channel,
`notifications/message`, was deprecated in specification version 2026-07-28 (SEP-2577) and the
SDK errors on it, so `report_progress` over stdio goes to stderr rather than to a deprecated
notification.

### A subprocess must not inherit the parent's stdin

Found by the stdio server and fixed in the git layer: `GitProcessRunner` now redirects the child's
stdin and closes it immediately. Without that, git was handed whatever stdin the host process had.
In the desktop application that is harmless; in the MCP server, stdin is the live protocol pipe,
and every git invocation blocked for the full 30-second timeout — a `read_file` call took 30s
instead of 25ms. A read-only subprocess has no business holding its parent's input channel in
either process, and in the server it could in principle have consumed protocol bytes.

## Dependencies beyond §0.3

Full rationale for each package added outside the fixed stack. CLAUDE.md keeps a short
package → reason table; this is the expanded version.

### Iteration 1

- **`StreamJsonRpc`** — JSON-RPC 2.0 over the shell's message channel, via a custom
  `MessageHandlerBase`.
- **`NJsonSchema.CodeGeneration.{CSharp,TypeScript}`** — contract codegen. One parser for both
  languages, so they cannot drift. Tool-only.
- **`Serilog` + `Serilog.Sinks.File` + `Serilog.Extensions.Logging`** — rolling `log.txt`
  behind `ILogger<T>`. No Serilog type escapes the composition root.
- **`Microsoft.Extensions.{DependencyInjection,Logging}`** — composition root.
- **`Shouldly`** — test assertions.

### Iteration 2

- **`Microsoft.Data.Sqlite`** — §0.3's persistence choice, now actually used. Version-matched
  to the `Microsoft.Extensions` 10.0.11 line.
- **`Dapper`** — requested by the user: parameter binding and row mapping instead of
  hand-written `DbDataReader` loops. A micro-ORM — no schema management, no change tracking,
  no query translation — so §0.3's "SQLite, JSON documents + indexed columns" is untouched.
- **`Microsoft.Extensions.Logging.Abstractions`** — the abstractions half of the
  already-approved logging package, so domain projects can take `ILogger<T>` without dragging
  the DI container in.
- **`@radix-ui/react-label`, `@radix-ui/react-alert-dialog`** — shadcn/ui primitives (§0.3).
  The provider picker uses a styled native `<select>`, so `@radix-ui/react-select` was
  deliberately not added.

### Iteration 4

- **`Microsoft.Extensions.AI` (+ `.Abstractions`, `.OpenAI`)** — §0.3's LLM abstraction,
  10.9.0. Versioned independently of the `Microsoft.Extensions` 10.0.11 line, which is why the
  numbers differ. `.Abstractions` is declared explicitly because three packages depend on it
  at three versions and transitive pinning needs one to settle on.
- **`OpenAI` — 2.12.0, not 2.13.0** — `Microsoft.Extensions.AI.OpenAI 10.9.0` declares
  `[2.12.0, 2.13.0)`. Raise both together or neither. Serves five of the six provider types.
- **`Anthropic`** — the official Anthropic .NET SDK (`anthropics/anthropic-sdk-csharp`, MIT),
  not the community `Anthropic.SDK`. Already depends on `Microsoft.Extensions.AI.Abstractions`
  and ships `AsIChatClient(model)` with tool calling, structured output and usage, so it needs
  no adapter.
- **`NJsonSchema`** — not new, but newly runtime: `DiffHacker.Llm` validates the model's
  structured answer against the schema it was asked for. The two `CodeGeneration` packages
  remain tool-only.

### Iteration 5

- **`ModelContextProtocol.Core`** — §0.3 names the main `ModelContextProtocol` package; this is
  the same official SDK one layer down. Everything the toolbox and the stdio server need is in
  it: `McpServerToolAttribute`, `McpServerTool.Create`, `StdioServerTransport`,
  `McpServer.Create`, and the client transport the round-trip test drives. The main package adds
  `AddMcpServer().WithStdioServerTransport()` builders over exactly those APIs and brings
  `Microsoft.Extensions.Hosting.Abstractions` and `Caching.Abstractions` with them; both
  composition roots here use a bare `ServiceCollection` and no generic host, so that would be
  sugar over a call we make directly, paid for in two more pinned packages. Emphatically not
  `.AspNetCore`, which `NoLocalServerTests` forbids by test. 2.2.0, `net10.0`; its floors sit
  under our existing pins, so nothing else moved. Raised with the user rather than substituted.
- **`Microsoft.Extensions.DependencyInjection.Abstractions`** — declared for the same reason as
  the logging abstractions already were, then not needed: `Toolbox.OpenAsync` takes its five
  dependencies as a record instead of a container. The entry stays in
  `Directory.Packages.props` because transitive pinning wants one version to settle on.

### No package needed

- **Resilience/retry**: retry is ~60 lines in `RetryPolicy`, and the thing that had to be
  observable — telling a rate limit from a revoked key — is exactly what a pipeline library
  would have hidden.
- **Native folder picker / secret store**: PhotinoX already exposes `ShowOpenFolder`; the
  three credential backends are `[LibraryImport]` bindings, which is why `DiffHacker.Storage`
  — and only that project — sets `AllowUnsafeBlocks`.
