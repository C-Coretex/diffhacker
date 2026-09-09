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

## The repository knowledge base

Iteration 6 decisions, settled with the user before implementing.

### Documentation is generated into DiffHacker, not into the repository

Requirement 3 and §0.2.12 both describe the documentation generator as writing into the user's
repository behind a preview-and-confirm gate. The user's decision changed the default: the three
documents live in DiffHacker, and writing them out is a **separate, explicitly confirmed export**.

The gate survived that change and got stronger for it. `profile.previewDocumentation` returns a
`previewToken` — a hash over the target and the exact bytes of every file — and
`profile.exportDocumentation` recomputes it from what it is about to write and refuses anything
that does not match. So "nothing is written that was not previewed" holds against a bug in the
interface, a stale renderer, or a direct call to the RPC method; it is not a rule the screen is
trusted to keep. Editing the profile between a preview and a write invalidates the token, which is
the point: the user approved bytes that are no longer these bytes.

The documents are rendered deterministically from the stored profile rather than by a second model
run. A second run would cost another few minutes and a few dollars, would be non-deterministic, and
could disagree with the profile that every analysis actually uses. A template cannot say anything
the profile does not — and the export gate needs the rendering to be stable, because a renderer
whose output varied would refuse every write.

`RepositoryWriteTests` is the audit turned into an assertion: it enumerates every file under `src/`
and fails on a filesystem-write API outside a named allowlist. Verification step 6 asks to audit
this rather than assume it, and an audit is worth something once and then rots.

### Withheld files: listed, never hidden

Requirement 11 asks for sensitive files not to be sent. The toolbox already saw only what git sees,
which is not enough: a committed `.env`, a checked-in private key or an `.npmrc` carrying a registry
token are all things git tracks happily.

Matching files are **flagged in every listing and refused for content**. Hiding them would have been
simpler and wrong: §0.2.5 says every changed file appears in the graph, and a reviewer who cannot see
that `.env` changed is worse off than one who can see it changed but not how. So `list_changed_files`,
`list_directory`, `get_repository_tree` and `get_path_info` all show the path with a marker, and
`read_file`, `get_file_diff` and `search_text` refuse it.

Search is the exception to "filter the results": withheld paths are excluded in git via
`:(exclude,glob)` pathspecs rather than filtered afterwards, so the total the tool reports is a
total of matches it is willing to show. A filtered page would have reported a count including
matches it then refused, and the offsets behind pagination would no longer line up.

The default list names secret-bearing *files*, not content — a rule that has to open a file to decide
whether it may be opened is no rule at all. Public certificates are deliberately absent.

### The size budget, and what happens when a profile misses it

12,000 characters, about 3,000 tokens, per repository and user-settable. The number matters more than
it looks: the profile goes into the prompt of every analysis of that repository, and that prompt is
re-sent on every turn of a tool-using run — so a profile twice as long costs twice as much on every
future analysis rather than once here.

Measured in characters rather than tokens because character counting is exact, free and identical
across providers, where a token count needs a tokeniser per model to be better than a guess. Measured
on the *rendered* document, which is what is actually spent.

An over-budget answer is handed back once to be shortened and then **fails**. Truncating it to fit
would produce a profile that ends mid-sentence and misinforms every future analysis, quietly and
forever. The repair goes through a fresh session with no tools: a session runs once by contract, and
the model is editing its own prose rather than learning anything new.

The user's notes and custom instructions sit outside the budget. They are paid for on the same terms,
and their size is reported for that reason, but capping what the user chose to write is not the
application's call.

### Drift is measured in files, not in commits or days

Regeneration is offered when more than 150 files, or more than a tenth of the tracked files, differ
between the profile's commit and `HEAD` — or when that commit is no longer in the repository at all.

Commits would have been the more natural unit and would have needed `git rev-list` added to
`GitProcessRunner.PermittedSubcommands`. That is a deliberate widening of the read-only contract for
a worse signal: a hundred typo commits drift a repository less than one that moves a module. `diff`
was already on the allowlist. Days are worse still — a repository nobody has touched for a year has
not drifted at all.

An unreachable commit — rebased away, rewritten, never fetched into a shallow clone — counts as
substantial drift by definition. The honest answer is "we cannot tell how stale this is", and offering
a fresh run is the right response to that.

### Manual edits survive regeneration because the shape says so

`IProjectProfileStore` has two save methods and they cannot reach each other's columns:
`SaveGeneratedAsync` names the generated ones, `SaveUserSectionsAsync` names the user's. The RPC
surface mirrors it — `profile.saveDocument` and `profile.saveNotes` — and so does the screen, as two
cards. Requirement 5 is therefore a property of the shape rather than a rule someone has to remember,
and a caller cannot get it wrong by passing the wrong object.

Editing a generated section is allowed, and the form says plainly that a rerun replaces it. What
survives a rerun is what the user wrote in their own sections, byte for byte.

### Two notification channels, not one

`analysis.progress` carries the model's own sentence about what it is doing; `analysis.toolCall`
carries the mechanical traffic underneath it. They are separate methods because they are read
differently — a reviewer reads the first and consults the second — and interleaving them would put a
hundred `read_file` rows between two sentences someone was reading.

Iteration 6 is where both finally have a producer. `docs/decisions.md` recorded that nothing exercised
a real notification travelling the real bridge into the real window, and CLAUDE.md pencilled that test
in for Iteration 7; the profile run is the first thing in the application that starts an LLM
conversation, so the test landed here, in
[05-repository-profile.spec.ts](../tests/e2e/specs/05-repository-profile.spec.ts). It drives a
scripted OpenAI-compatible endpoint on localhost — no test in this repository reaches a real provider.

### Number formatting is ours, not the runtime's

`toLocaleString()` reads its grouping from whatever ICU data the runtime carries, which is how the
same count came to render as `1,200` in the WebView and `1200` under jsdom. DiffHacker ships English
only (§0.6) and the host runs with `InvariantGlobalization`, so `i18n/format.ts` owns one explicit
rule instead of an inherited one.

## The analysis pipeline

### Validation happens inside the run, not after it

The graph rules a JSON Schema cannot express — every changed file covered, every node in exactly one
container, one entry node each, no dangling edge — are checked by a delegate the caller hands to
`LlmConversation`, and `LlmSession` runs it beside the schema check it already did.

Checking afterwards was the obvious shape and it is the wrong one. A result rejected after
`RunAsync` returns can only be repaired by a model that has forgotten everything it read: the session
is single-use by contract, and a fresh one starts with the file list and nothing else. Handed the
failures inside the conversation, the model still has its exploration in context and its tools still
bound, so "no node covers `src/Cache.cs`" is something it can fix by going and looking rather than by
writing a node from the metadata it was given. Fixing it from metadata is close enough to inventing
data that requirement 4 forbids it.

The cost of doing it this way is one additive change to Iteration 4's contract —
`ResultValidator` and `MaxResultRepairs` on `LlmConversation`, `ResultRepairs` on `LlmRunResult`,
`llm_result_rejected` in `LlmFailures`. Every existing caller passes no validator and behaves exactly
as before.

**Two repair rounds, then the run fails loudly.** One round fixes what was reported; a second catches
what the fix knocked over, which happens — moving a node between containers can leave the one it left
without an entry node. A model that has not converged by then usually will not, and every round
re-emits the whole document at the model's output rate, which on a five-hundred-file change is the
expensive half of the run.

### Errors fail a run; warnings travel with it

`AnalysisValidator` produces diagnostics at two severities, and the split is what keeps repair rounds
for real breakage. An uncovered file or a dangling edge makes the result unusable. A cycle does not:
mutual dependencies exist in real code, and rejecting the answer would be asking the model to
misdescribe the repository. Reading direction stays unambiguous regardless, because it comes from
container `displayOrder` and node `rank` — the model's own layout intent — rather than from following
edges.

An incomplete reading order is a warning for the same reason: container order and rank already define
a complete traversal, so there is nothing to ask the model that it has not already said, and
`AnalysisReadingOrder` derives one rather than spending a round.

### Node ids keep the path as their whole prefix

`src/Cache.cs`, or `src/Cache.cs#eviction` when one file genuinely holds two unrelated changes. §0.6
says identity is path-derived and stable across re-runs, and later iterations key reviewed-state off
it; an id the model chose freely would reset every time it phrased something differently. Validation
enforces the form, so for the ordinary one-node-per-file case stability is a property of the shape
rather than a hope about the model.

### The longest chain is measured over the condensation

Accepting cycles means the graph the statistics walk can contain one, and a longest-path walk over a
cyclic graph does not terminate. `AnalysisGraph` measures it over the strongly connected components
instead, so a cycle counts as the one thing it is. Tarjan is written with an explicit stack rather
than recursion because §0.2.10 allows fifteen hundred files and a graph that deep is not something to
hand to the call stack.

### Only completed analyses are stored

There is no method on `IAnalysisStore` for saving a partial one, and that is the design rather than
an omission: an interface that cannot express it cannot accidentally be made to. A cancelled run
rethrows with its usage readable on the session, a failed one comes back as a result carrying what it
spent, and neither reaches SQLite. §0.2.8 is a promise about what the user sees, and it survives a
restart only if nothing partial was written in the first place.

### The generator attaches enum converters to the type, not only to the property

NJsonSchema points each enum *property* at a converter and, for an enum inside an array, emits a TODO
about `ItemConverterType` — which System.Text.Json has no equivalent of. Node states are the first
array of enums in `/schema`, so `unchanged_relevant` would have reached the wire as
`Unchanged_relevant` with nothing failing until someone read a stored analysis back.
`ContractGenerator.AttachEnumConverters` puts `[JsonConverter(typeof(SchemaEnumConverter<T>))]` on the
generated enum itself, which applies wherever it appears, collections included.

The same work found that `StructuredOutput.Validate` threw out of a run when handed something that
was not JSON at all — a real answer for a model to give, and the whole point of the weakest
structured-output tier is asking it not to. It now comes back as a validation failure the run can
repair or fail on.

### The prompt is the product, so it is tested like one

The first draft of `AnalysisPrompt.SystemPrompt()` specified the mechanics well and the purpose
badly, which is backwards for the thing this application exists to do. `rank` was defined only as
"1, 2, 3 with no gaps" — a shape any ordering satisfies, so a model would reach for importance or
path order and produce a pile of files rather than a walk. `readingOrder` appeared once, as a
constraint. The single container example given was a code-dependency chain, which quietly taught
that clusters are held together by imports.

The rewrite is organised around the reader instead: what they do with the answer, how a starting
point is chosen, what orders the nodes after it, and — stated in its own section, before any of the
mechanics — that **this is not a dependency graph**. A program could compute the imports; the model
is here for the connections a program cannot see, and a flag with its migration and its changelog
line is one cluster even though nothing references anything. Conceptual edges are described as
wanted rather than tolerated, because they are the only way a cluster held together by intent can be
expressed at all.

`AnalysisPromptTests` pins the ideas rather than the wording, so the prose can be improved without
rewriting the tests — but not silently dropped. It asserts against a whitespace-collapsed copy of the
prompt: the prompt is a wrapped raw string literal, and a phrase that happens to straddle a line
break is not a different phrase. Without that, rewrapping a paragraph fails a test for no reason and
the cheapest fix is to contort the prose to keep the test green, which is exactly backwards.

### The prompt owns the guidance; a schema description owns its field

That rewrite duplicated the reading-path guidance into the schema descriptions for `entryNodeId`,
`rank`, `displayOrder`, `readingOrder`, `containers`, `edges` and `kind`, on the reasoning that they
travel to the provider as the response format and are read alongside the system prompt. Both halves
of that are true. What it missed is that *both* are re-sent on **every request**, so a paragraph
written twice is paid for twice on every turn of a run that may take three hundred of them.

Measured, the fixed preamble of one analysis request was ~35,000 characters — system prompt 9,563,
response schema 15,671, ten tool descriptions ~10,000 — about 8,800 tokens before a single changed
file is named, against a 300-turn, 2,000,000-token budget. The duplicated guidance alone was ~4,000
characters a turn, and it taught the model nothing the prompt had not already said.

So the division is fixed: the **prompt owns the guidance** — how to cluster, how to choose a starting
point, what rank means, why a conceptual edge is wanted — and a **schema description owns only what
its field is and the constraint the validator will check**. Short factual restatement across the two
is fine and often useful next to the field being emitted; paragraphs of persuasion are not. Trimming
to that line, plus compressing the prompt itself and the tool descriptions, took the preamble to
~28,500 characters (~7,100 tokens) with every idea `AnalysisPromptTests` pins still present. The same
rule governs `ProfilePrompt` and `project-profile-document.schema.json`.

Two things are worth keeping in view when editing any of this. Tool descriptions carry a
200-character floor in `ToolboxCatalogTests`, which is a floor and not a target — each still has to
say what its tool will not do and which tool to reach for instead, and that content is why they were
only trimmed ~11%. And the profile's own size limit is the same argument one level out: a profile is
re-sent on every turn of every *future* review of that repository, which is why `ProfilePrompt` marks
it as a hard limit rather than a target.

### Reachability is a warning, and it is the closest thing to a check on the point

`AnalysisValidator.CheckReachability` walks each container from its entry node along the edges whose
ends both sit inside it. A node nothing leads to is one the reviewer arrives at cold, having to work
out for themselves why it is in front of them — which is the work the product is supposed to have
already done.

It is a warning rather than an error on purpose. The node may genuinely belong there with the
connection merely left unstated, and failing the run would spend a repair round buying an edge the
model might invent rather than find. Cross-container edges do not count as a way in: §0.6 keeps them
out of the layout, so they are not something a reader follows to arrive somewhere.

---

## The diagram

### The LLM decides hierarchy and ranking; ELK decides pixels

Never coordinates from the model — the iteration fixes that, and this is how the model's two ordering
claims reach a layout engine that only understands edges.

**Containers** are React Flow parent nodes, laid out one per sub-graph. The root packs them with
`rectpacking` in `displayOrder`; the layered algorithm would string thirty clusters out in a single
very long row, which is unreadable at any zoom.

**The entry node** carries `elk.layered.layering.layerConstraint: FIRST`. Verification step 2 — the
starting point is at the top of its cluster — then holds by construction rather than by hope. Without
it, an entry node with an incoming edge from one of its own consequences is placed *below* that
consequence, which is exactly backwards from how the answer is meant to be read.

**Rank within a layer** is the order children are emitted in, plus
`considerModelOrder.strategy: NODES_AND_EDGES` and `crossingMinimization.forceNodeModelOrder: true`.
Without those two the ranking reaches ELK and is then discarded in favour of fewer crossings.

**Rank that edges cannot express** gets a synthetic edge from the rank−1 node, tagged with the
`rank:` prefix so `flowGraph` never draws it. A node the model ranked third with nothing pointing at
it would otherwise float into the first layer and read as a second starting point. The edge is a
statement about reading order, not about dependency, so drawing it would be the diagram claiming
something the model did not — and the fidelity gap is made visible rather than hidden: **every box
prints its rank**, so a reader who sees 3 beside 2 knows the ordering is the model's.

### Cross-container edges are absent from the ELK input, not down-weighted

§0.6 says they are excluded from layout influence. Absent is the only version of that which can be
*checked*: `elkGraph.test.ts` asserts the ids are in neither sub-graph, which is what verification
step 6 asks for — "in the ELK input, not by eye". A weight low enough to look right in one graph is
a weight that misbehaves in another, and nobody would notice.

React Flow draws them afterwards with bezier routing, which also makes them look different from the
ELK-routed orthogonal intra-container edges: the two are separable by shape as well as by opacity,
and shape is what survives being zoomed out.

### Ten project colours, handed out interleaved

Fill encodes project (requirement 6) and the number of projects is unbounded, so colour never does
the work alone: every box prints its project name and the legend maps swatch → name → count.

Ten hues about 35° apart, defined as `oklch` tokens in `index.css` like every other colour. Projects
are sorted by node count descending, ties broken by name so the assignment is stable across re-opens
— a reviewer who learned that green is the host should not have to relearn it on a re-run. They are
then handed out in the order `0,4,8,2,6,1,5,9,3,7`: the two largest projects cover most of the
diagram and are the pair most worth telling apart, and slots 1 and 4 are the red/green a protanope
collapses, so they are never the first two given out.

The eleventh project onward shares a neutral colour, and the legend collapses them behind "N other
projects". Past ten hues, another colour is not another *distinguishable* colour, and a legend of
forty swatches is one nobody reads.

### Requirement 11: the numbers, measured

`src/ui/src/graph/layout.bench.test.ts` measures 300 nodes across 12 clusters, with a chain through
each and a cross-container edge out of every one:

| | |
|---|---|
| ELK layout, warm | ~70 ms |
| ELK layout, first call (includes loading the ELK bundle) | ~290 ms |
| `toFlowGraph` over the result | a few ms |
| Collapsing one container (a full relayout) | ~70 ms |

Nothing needed fixing, and `onlyRenderVisibleElements` stays **off** — the iteration's fixed decision
is to profile and fix what is slow rather than reach for it, and nothing here is slow. It is behind
`graphOnlyRenderVisible` in the store so a future profiling session can turn it on to compare.

The end-to-end suite renders a real 500-node analysis in the real WebView. **Frame rate while panning
is not measured** — it needs a real compositor and a hand on the mouse — so it is reported as
unmeasured rather than assumed.

### The layout worker is a seam, and it falls back

`runLayout.ts` has two implementations: a Web Worker for the application, and an in-process one for
tests. Requirement 12's snapshot test uses the second, because the coordinates do not depend on which
thread produced them and a worker in jsdom is a fight with the test environment rather than a test of
the layout.

The worker is a **classic** bundle (`worker.format: 'iife'` in `vite.config.ts`), not a module one.
WebView2 refuses to start a module worker whose script comes from the custom `diffhacker://` scheme:
the constructor succeeds and the worker dies immediately afterwards, which reached the screen as "the
diagram could not be arranged" with nothing in any log to explain it. An IIFE bundle has no import
statements to resolve and starts everywhere.

And when a worker dies for any other reason, everything waiting on it is laid out on the main thread
instead, with a console warning. A diagram that arrives a moment late is worth far more than one that
does not arrive.

### The stub provider speaks server-sent events

Not a decision about the product, but a trap worth recording. `LlmSession` streams every request —
deliberately, so a long answer keeps bytes moving past an idle timeout. `tests/e2e/src/stubProvider.ts`
originally answered every request with one plain JSON body, which the OpenAI SDK read as *no content
at all*: the run then failed schema validation with "the response was empty", and no layer said why.
The stub now emits proper `text/event-stream` chunks. This was breaking every analysis and profile
journey in the end-to-end suite before Iteration 8 touched anything.
