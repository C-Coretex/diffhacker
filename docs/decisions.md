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

## Explanations

Iteration 9's answers to what its **Raise before implementing** section asked, and the two things
that turned out to matter that it did not.

### The overview moved from a rail to a band

Iteration 8 put the summary, the risks, the statistics and the long-form detail in a left rail, and
noted that Iteration 9 would replace its contents. It replaced the rail instead.

Two reasons, both about width. The diagram is the product and at three hundred boxes it wants every
pixel; and Iteration 10 adds a diff panel that can expand to full width, which would have spent the
rest of that session arguing with a rail for the same space. A band above both fights neither.

What is *permanently* on screen is what a reviewer needs permanently: what the change does, and what
it risks — side by side, because §0.2 keeps those two apart from the schema down and the top of the
screen is where that shows first. The heading carries the total risk count, so even folded the band
answers "how much danger is in here".

The summary and the risk column **fold away too**, independently of the overview below them. Prose a
reviewer has already read is the first thing that should give the canvas its height back, and what
survives the fold is the risk count — the part you want without asking for it.

Everything else requirement 5 asks for — reading order, the clusters and their sizes, the statistics,
the register of every risk, the run's provenance, the diagnostics, and Iteration 7's long-form
result — is behind one toggle, capped at 45 % of the window and scrolling inside itself. Laid out as
two rows of three grouped by *shape*: three lists, then three tables. A grid row is as tall as its
tallest column, and mixing a ten-row table into a row of short lists left most of the band empty and
pushed the risk register out of sight, which is the one thing in there that must not be.

The register is counted by walking the document — overall, then each container, each node, each edge —
rather than read from `statistics.riskCount`, so the number beside the heading and the list behind
the toggle cannot disagree. A change-wide risk therefore appears twice when the overview is open,
once in the band's column and once in the register. That is deliberate: "all flagged risks collected
in one place" means the register is complete, not that the column is emptied.

### Hover timing, and clicking to keep a card

**Superseded.** Hovering no longer opens a card at all — every card, node, edge and cluster alike,
opens only on a click, and `useHoverTarget.ts` was simplified to match: no open delay, no close
delay, no separate "pinned" state, because every open card is now the equivalent of what this
section calls "kept". The timing numbers below are history, kept for why click-to-keep was added
in the first place — that part did not change, only the hover-to-glance half it was added beside.
Hovering an edge still brightens it, independently of the card; see "Edges are wider than they
look" below.

250 ms before the first card, no delay at all between adjacent ones, 500 ms of grace on leaving.
The middle number is the one that decides how the diagram feels: re-serving the wait for every
neighbour makes reading across a cluster feel like arguing with the screen.

**Clicking anything on the diagram keeps its card open** — a node, an edge, a cluster — and
**clicking the same thing again closes it**. The gesture that opened something is the one a hand
reaches for to close it, and a control that only goes one way is one people press twice and then go
looking for the exit. Escape and a click on the empty canvas also put it away, and the card carries
a pin button.

Only a *kept* card toggles: clicking what you are merely hovering keeps it, because that is how you
keep it. And the identity compared is the kind as well as the id — a cluster id and a node id come
from different namespaces and nothing stops them matching.

That was not the first answer. The card originally had only the pin button, on the reasoning that
Iteration 10 owns click-on-a-node and this iteration must not spend the gesture. In use that was
wrong twice over: 150 ms of grace was not enough for a hand that does not travel straight to the
card, so the thing you were reaching for vanished on the way; and reaching for a button to keep
something you are already looking at is a step nobody should have to take. Hovering is for glancing.
The moment a reviewer wants to *read* — scroll the card, select a path out of it — clicking is the
gesture they already reached for.

**This changes what Iteration 10 has to do.** Click now keeps a card, so opening the diff needs
either a button on the card (the natural place — the explanation and the risks are already there,
which is that iteration's requirement 4) or a double-click. It is not a free gesture any more.

A pinned card ignores the *pointer* entirely — it is the reviewer's, and having it swap because the
pointer crossed another box on the way to its scrollbar is the worst version of this feature — but
never the reviewer: clicking a second box moves the card there.

One card is mounted, not one per node — a controlled Radix popover anchored to a `position: fixed`
box carrying the hovered element's client rect. Measuring the real element is what keeps the card
correct at every zoom without this code knowing anything about React Flow's viewport transform, and
Radix's collision handling is what keeps it beside its node rather than over it.

**Radix's own outside-click dismissal is turned off** (`onInteractOutside` prevented), and this is
not tidying. Radix dismisses on a *deferred* pointer-down outside the card; clicking a second box is
exactly that, so the click pinned the new card and the deferred dismissal then closed it again — a
kept card could never be moved from one node to the next. The surface already knows what an outside
click means: another node keeps its own card, the background puts the card away. Escape still closes
through `onOpenChange`.

One card is mounted, not one per node — a controlled Radix popover anchored to a `position: fixed`
box carrying the hovered element's client rect. Measuring the real element is what keeps the card
correct at every zoom without this code knowing anything about React Flow's viewport transform, and
Radix's collision handling is what keeps it beside its node rather than over it.

### De-emphasis stops at 70 %, and never at the size

Requirement 6 wants trivial changes visibly quieter; §0.2.5 says every changed file is in the graph.
Both hold because the *only* channels importance uses are opacity and type weight: importance 1–2
fades to 70 % with muted text, 4–5 gets a heavier name and a wider project rail, and everything else
about the box — its size, its border style, its badges, its project colour — is untouched, because
§0.6 already spent those on state and project.

Nothing changes size, so ELK never sees any of this and the layout snapshot is unaffected. And the
fade lifts to full on hover, on a search match and on a jump, so looking at a trivial node is never a
worse experience than looking at an important one. The legend says so too — a reviewer who notices
that some boxes are fainter will otherwise guess, and the likeliest guess is that the faint ones were
filtered out.

### Edges are wider than they look

`interactionWidth`: a transparent 20-pixel stroke over each drawn line, 28 for the faint
cross-container ones, which are deliberately the hardest to see and so the ones that need it. React
Flow renders it itself, so nothing is added to the DOM. Hovering also brightens the real stroke,
because on a dense diagram the reviewer has to see *which* line they caught before they start reading
about it.

`08-explanations.spec.ts` hovers six pixels off the line, measured perpendicular to the path with
`getPointAtLength` and `getScreenCTM` — a miss for a one-and-a-half pixel stroke, a hit only because
of the interaction width. Drop it and that test fails.

### The legend is capped to the room it has

Iteration 8's legend had no height limit, and its project list is the one part with no natural
length: one row per project in the change, and a repository can have as many as it likes. On a
repository with a dozen modules it ran off the bottom of the window and the projects at the end were
unreachable. Iteration 9's emphasis section made it taller still.

Capped to `--radix-popover-content-available-height` — the room Radix measured between the trigger
and the window edge — and scrolled inside that, with a fixed fallback for the frame before the
popover has been positioned. The end-to-end test builds fourteen projects (a `Makefile` is enough to
make a directory one, per `ProjectLocator`) and asserts both halves: that the content really is
longer than its box, and that the box is inside the window anyway. Remove the cap and the first
assertion fails, so the test cannot pass for the wrong reason.

### Cost when the model was not priced

Nothing new: `costUsd` is absent rather than zero, and the screen says **cost unknown**, exactly as
Iteration 4 decided and as the header has said since Iteration 7. An unrated model cost an unknown
amount; reporting nothing spent would be a claim the application cannot make.

### jsdom cannot place a popover, and cannot draw an edge

Two limits found while testing this, both recorded because the next person will hit them.

React Flow renders **no edges at all** in jsdom: a node has to be measured before it is edge-worthy
and jsdom measures everything as zero. So the edge card is tested as a component in
`hoverCards.test.tsx`, and hovering a real line only happens end to end.

Radix keeps a popover invisible and inert until floating-ui has positioned it, which never completes
in that same zero-sized document. Inside the card nothing has an accessible name and everything
inherits `pointer-events: none`, so the component tests query by label and use
`userEvent.setup({ pointerEventsCheck: 0 })`. The card is in the DOM and correct; jsdom simply cannot
place it, and the end-to-end suite hovers, pins, scrolls and copies in a real window where none of it
applies.

### The unhandled error the graph tests could not stop throwing

`vite.config.ts` filters exactly one unhandled error, and it predates this iteration.

A mouse event in a browser always carries a `view`. `@testing-library/user-event` builds its events
by defining `view` as a **non-configurable** own property from an init object that has none, so it is
fixed at `null` before dispatch and cannot be redefined afterwards — patching `UIEvent.prototype` in
`setup.ts` does nothing, because an own property shadows it. React Flow pans with d3-zoom, whose
mousedown handler calls `dragDisable(event.view)` and immediately reads `view.document`. So every
click that reached the pane — collapsing a cluster, expanding one, choosing a search hit — threw a
TypeError inside a DOM listener, asynchronously and outside any assertion. The tests passed and the
run still reported three errors nobody could act on, attributed to whichever test happened to be
running.

`onUnhandledError` matches that one error by type, message and `d3-drag` in the stack. Anything else
still fails the run.

### The clipboard has a fallback because the scheme is not `https:`

`copyText` tries `navigator.clipboard.writeText` and falls back to a hidden textarea and
`document.execCommand('copy')`. Whether `diffhacker://app` counts as a secure context is up to the
host that registered the scheme, and where it does not, `navigator.clipboard` is `undefined` rather
than failing — there is nothing to catch. WebView2, WKWebView and WebKitGTK need not agree, and only
one of the three has been exercised. The end-to-end test reads the path back out of the real
clipboard, so it proves whichever branch actually ran; on WebView2 that is the first one.

### The colour scheme is chosen, not merely detected

The interface followed `prefers-color-scheme` and nothing else, which is not a preference — it is a
guess about the room you are sitting in. There is now a three-way control in the header: follow the
system, light, dark. Three buttons rather than one that cycles, because cycling makes the current
state something you infer and the next state something you guess at, and there are only three.

The choice lives in **`localStorage`**, not in SQLite through the host. Every other setting goes to
the host because it is a secret, a piece of repository state, or something the analysis needs; a
colour scheme is none of those. It has to be applied on the first paint, before any bridge call has
resolved, and putting it behind JSON-RPC would mean a schema, a method, a handler and a table for a
value one word long. §0.2.13's "pure renderer" rule is about network, filesystem and secrets —
browser storage is none of those either. Every read and write is wrapped, because a context with
site data blocked throws on access rather than returning null; following the OS is the fallback.

The media query is still watched while an explicit choice is in force. Switching back to "system"
has to resolve against what the OS is asking for *now* rather than against whatever it said when the
window opened, and `useTheme.test.ts` pins that.

One consequence worth knowing: `localStorage` lives in the WebView's own profile, which
`--data-dir` does not redirect — so unlike everything else the end-to-end suite touches, a theme it
sets is written into the developer's real browser profile. The one test that changes it puts it back
to "follow the system" in a `finally`.

## Reading the code

### Monaco lives inside the existing Content-Security-Policy, unchanged

Iteration 10's largest open question was whether Monaco could be bundled without weakening the
policy Iteration 1 set — the instruction was to stop and ask rather than widen it. It could, and the
reason is *what* is imported rather than any concession made to it.

Not the `monaco-editor` barrel, and not `editor.main`: both pull in the CSS, HTML, JSON and
TypeScript **language services**, each with a worker of its own and a compiler behind it. §0.2.3
makes this product language-agnostic, and it has no business running `tsserver` to colour a diff.
`src/ui/src/components/diff/monaco/setup.ts` imports `editor.api` plus the eighty-one
`basic-languages` contributions instead. Those are Monarch — a tokenizer that runs on the main
thread — and each registers an id, its extensions and a lazy loader, so the grammars become
eighty-one small chunks that load only when a reviewer opens a file in that language.

That leaves exactly one worker, Monaco's own `editor.worker`, which computes the diff. Vite builds
it through `?worker` as a classic IIFE bundle, because `worker.format: 'iife'` is already set for
the ELK layout worker: WebView2 will not start a **module** worker served from the `diffhacker://`
scheme. `worker-src 'self' blob:` and `style-src 'unsafe-inline'` were both granted in Iteration 1
with a comment naming Monaco as the reason, and `font-src 'self'` covers `codicon.ttf`, which
`ContentTypes.cs` already allows. No `getWorkerUrl` blob-bootstrap trick is used; that exists to work
around bundlers that cannot emit a worker chunk, and reaching for it is the one thing that would have
made the policy argue back.

None of that is taken on trust. `09-diff-review.spec.ts` arms a `securitypolicyviolation` collector
before the first editor exists and asserts it is still empty after six have been created and
destroyed in a real window.

**Monaco is a lazy chunk.** A static import would put roughly 3.7 MB in the first script the window
parses, before the welcome screen has drawn. `AnalysisScreen` loads `DiffPanel` through
`React.lazy`, which keeps the initial bundle at about 650 kB and defers the rest to the moment a
reviewer first opens a file.

### Double-clicking no longer zooms the diagram

React Flow's zoom is d3-zoom, and d3-zoom's `dblclick.zoom` listener is a **native** handler on the
pane. React delivers every synthetic event at the document root, which is above the pane — so the
native listener runs first and stops the event dead, and `onNodeDoubleClick` is never called. With
`zoomOnDoubleClick` on, the gesture simply does not exist.

Turning it off costs nothing: scroll, the controls and the Fit button all still zoom, and a
double-click on the diagram now means exactly one thing.

### Unchanged-region folding starts off for a node that names lines

Monaco folds unchanged runs to three lines with a control to open them, which is requirement 2's
"expandable context" and is what makes a diff of a two-thousand-line file readable. It starts
disabled when a node carries a line range, and that is requirement 1 winning a real conflict between
the two.

The lines a node is *about* are usually unchanged — a function whose caller moved, a type whose
shape now matters — so folding is precisely what hides them, and "scroll to and highlight it" cannot
be honoured on a line that was never rendered. Whole-file nodes, which is most of them, start folded.
This was found by the end-to-end test rather than by reading the options.

**And it is a control, not only a default.** The panel's *Whole file* button flips it either way,
because "is this change safe" is often a question about the code the diff did **not** touch, and the
only previous answer to it was opening the file somewhere else — which is the thing this iteration
exists to stop. It follows the file rather than the session: "show me the whole of *this* file" is a
question about this file. `MonacoDiff` applies it in an effect of its own, so pressing the button
re-folds what is already loaded rather than rebuilding two models to change one boolean.

### Reviewed state is a column on the analysis, and does not survive a re-run

`reviewed_json` on `analyses` (schema 6), holding a JSON array of node ids. Additive and nullable,
exactly as `files_json` was in schema 5, so an analysis written before it opens with nothing marked
rather than failing to open.

A column rather than a table of its own, because a mark belongs to one analysis and dies with it:
`DELETE FROM analyses` already takes it, with no foreign key to remember and no orphan row to prune.
The trade is stated plainly rather than hidden: **a re-analysis writes a new row and therefore starts
with nothing marked.** That is the honest reading of "persisted with the analysis"; carrying marks
forward across runs would be a behaviour nobody asked for, and `SqliteAnalysisStoreTests` pins the
current one so it is a decision rather than a discovery.

Ids, not indices. §0.6 makes node identity path-derived and stable across runs, so a mark keeps its
meaning when the model rephrases a title — and Iteration 11 can switch grouping mode underneath it
without touching the column.

`IAnalysisStore.SetNodesReviewedAsync` is the first method on that interface that changes a stored
analysis, and it is not an exception to "the analysis result is the model's, unedited": it writes the
reviewer's own marks, which live beside the document rather than inside it. Nothing on the interface
can edit the document.

### The external editor is handed two paths, and the host decides which two

An external diff tool takes two file paths, and the committed side of a change is a git object.
`HeadBlobExtractor` materialises it under `AppPaths.DiffCacheDirectory` — the application's own data
directory, never the repository — which is why it is the eighth entry on `RepositoryWriteTests.Allowed`
and why that entry says what it writes to. §0.2.12 still holds: the documentation export remains the
only thing that writes into a repository.

The renderer names an editor, a file and a line; it never composes a command. The host decides
whether that becomes `--diff` or `--goto` from **which sides actually exist**, so an added file (no
committed side) opens rather than being compared against emptiness, and a deleted one opens its
extracted committed copy. A renderer that chose would be able to ask for a comparison against
nothing, and the answer to that is an editor showing an empty pane instead of a message.

Every launch is `UseShellExecute = false` with an argument list. No shell is involved, so a path
containing a space or a semicolon is one argument and cannot become two, and nothing in a
user-configured command can turn into a second command.

VS Code and Visual Studio are discovered rather than configured — `code`/`code.cmd` on `PATH` plus
the usual install locations, and `vswhere.exe` for Visual Studio on Windows — so the interface offers
a button per editor that is actually installed and nothing for one that is not. What is left to
configure is everything else, which is what "the external editor command is user-configurable" has to
mean once the common cases need no configuring. There is deliberately no "preferred editor" setting:
picking a default from a list of one is a worse interface than a button that is simply there.

One honest limitation, recorded in the extractor's own doc comment: `GetFileContentAsync` returns
decoded text, so what lands in the cache is UTF-8 whatever the committed bytes were. For a Latin-1
file the external editor sees the same characters written a different way. The diff is identical.

### "Expand to full width" stops short of full width

The iteration's fixed decisions say the diff panel can expand to full width; its verification step 10
asks that the reviewer's position stay visible **in the graph** at all times, "including while the
diff panel is expanded to full width". Both cannot be literally true.

The splitter clamps at `container − DIFF_PANEL_MIN_GRAPH`, leaving the diagram a 260-pixel rail, and
the rail stays centred on whatever the panel is showing — `diffPanelWidth` is a dependency of the
centring effect, without animation while the divider is moving, because sixty queued four-hundred-
millisecond pans is a canvas that keeps drifting after the pointer has stopped. The sentence that
survives is the one with a reason behind it.

**Full screen is the separate mode beside that, and it is deliberately separate.** The clamp is about
*dragging*: a reviewer moving a divider has not asked to lose sight of where they are, and a rail that
silently vanished at some width would be a rule nobody could see. Asking for the whole window by name
is a different act — one button in, the same button out, `Escape` as well — so the diagram is hidden
rather than shrunk. It is hidden, not unmounted: the diagram owns an ELK worker and a laid-out canvas
that cost real time to build, and tearing them down would make leaving full screen slower than
entering it.

### The boxes on the diagram carry their own controls

Iteration 10 first put "open the diff" only on the hover card, because Iteration 9 had spent the
single click on pinning that card. That was one gesture too many: a reviewer who already knows which
file they want had to summon a card to reach a button. The card is still where the *explanation*
lives; the three things a reviewer does *to* a file — read its diff, hand it to an external editor,
mark it read — are now on the box, drawn over the footer when the pointer is on it.

Every one of those buttons stops its click and dismisses the pinned card first. The surface turns a
click on a box into a kept card, so a button that let its click through would open a diff and drop a
card over it in the same gesture; and pressing a button *on* a card's box says the reading is over.

They reach the store through `GraphActionsContext` rather than by calling `useAppStore` themselves,
and that is arithmetic rather than taste. Three hundred boxes reading four store slices each would be
twelve hundred subscriptions, plus three hundred copies of the "which editors exist" question, to
render a row that is invisible until a pointer arrives. The surface asks once and passes the answers
down.

### A cluster opens as a queue of its files

A container is the unit a reviewer actually reads — it is the thing the model decided belongs
together — so its title bar has *Open every file*, and a double-click on the region does the same.
The panel then carries a strip listing every file in the cluster, with the reviewed ticks on it, and
previous/next walk that list instead of the whole change.

The order is the analysis's own reading order restricted to the cluster, which is the only ordering
that makes opening one worth anything: a queue in declaration order would be the alphabetical file
list with a smaller N. A member the reading order never mentions is still listed, after the ones it
did — §0.2.5 puts every changed file on the diagram and a list of them cannot quietly drop one.

The queue is put down by its own button, and by following an edge out of the cluster: `openDiffFor`
keeps `diffContainerId` only while the node being opened is in that container. A queue that kept
pointing at a cluster the reviewer had left would be a queue they no longer chose.

## Grouping modes

### One pass produces both groupings

Iteration 11 named the choice and left it open: extend the result so one pass produces both
groupings, or run a second pass lazily on the first switch. It was decided as the first, and the
reason is where the tokens actually are.

Almost everything an analysis costs is exploration and node prose. The nodes carry `whatChanged`,
`whyItChanged`, `howItAffectsOthers`, `implementationNotes` and their risks — on a three-hundred-file
change that is the overwhelming majority of the output, and **both groupings share every word of
it**. A second grouping is a second container list and a second reading order: perhaps ten to twenty
containers of a couple of hundred characters each, plus three hundred node ids. Single-digit percent.

A lazy second pass, by contrast, is a second conversation. Either it explores the repository again —
which is the expensive half, and would cost roughly what the first run cost — or it is handed the
first result back as input, which on a large change is tens of thousands of input tokens and a
grouping decided without ever having looked at the code. And it would arrive as a charge from
clicking a toggle, needing a cost estimate, a confirmation, progress reporting and a failure path,
for a view the iteration text describes as "a genuinely good complementary view, not a consolation
prize".

So everybody pays a little and nobody pays a lot — with one exception, below.

### The second grouping can be turned off, and then it is not in the request at all

[docs/iterations/iteration-14-follow-ups.txt](iterations/iteration-14-follow-ups.txt) asks for an
option to disable parts of an analysis, and this is the first part to get one: a remembered toggle
beside the Analyse button, defaulting to on.

What makes it worth having is that turning it off takes the work out of the *request* rather than
discarding the answer. `AnalysisPrompt.SystemPrompt(changeClusters: false)` never mentions the second
grouping, and `AnalysisResponseSchema.For(false)` removes `clusterContainers` and
`clusterReadingOrder` from the schema the model answers in. That matters more than it sounds:
`StructuredOutput.PromptSuffix` appends the schema to the system prompt *as well as* sending it as
the provider's response format, so every character of the schema is paid for twice on each of up to
three hundred turns.

The variant is derived from the one schema in `/schema` at runtime rather than kept as a second file.
Two files would be two things to bump and a way for the variant to stop matching the contract the
validator enforces.

An analysis produced that way holds one grouping, says so in `AnalysisView.availableGroupings`, and
the control offering the other is disabled with its reason rather than hidden — the view is unbought,
not missing.

The "remembered toggle" is no longer how it is set: since 1.15 it is one of the
[analysis parts](#analysis-parts), defaulted in Settings and overridable for one run.

### Order is a list's order, not a number on the node

`rank` was an integer on the node and `entry_point` was one of its states. Neither can describe two
groupings: a node sits at one position per grouping, and may start a cluster in one while sitting in
the middle of another. So the model no longer states either. A container's `nodeIds` **is** the
reading order, entry node first, and `entryNodeId` is the single declaration of where to start.

This turned out to be a reduction rather than a trade. It deleted `rank_not_dense` — a dense 1..n
across three hundred nodes was a repair round models spent regularly — along with `many_entry_nodes`
and `entry_state_disagrees`, which existed only because one fact was stated in two places that could
disagree. Five error codes became two.

The wire kept both. `AnalysisNodeInfo.rank` is the index in the active grouping's container plus one,
and `entry_point` is added to the entry node's states by `AnalysisWire`. So the renderer, the boxes,
the legend and the end-to-end suite were untouched by any of it — which is also why
`AnalysisAgreementTests` now pins a deliberate divergence between the two node-state enums instead of
their equality.

### The projection chooses a grouping; the renderer never sees two

`AnalysisWire.ToWire` already derived everything that differs between groupings — container
membership onto each node, `crossesContainers`, the container-then-rank sort, the resolved reading
order. Giving it a grouping parameter was the whole of the renderer-facing change: `AnalysisView`
keeps its shape and only its content becomes per-grouping, so `buildElkGraph`, `toFlowGraph`,
`containerQueue`, `neighboursOf`, `collectRisks` and `searchGraph` did not change at all.

Sending both groupings over the bridge and switching in TypeScript was the alternative. It would have
duplicated the projection, made the statistics that depend on grouping the renderer's problem, and
doubled the container payload on every read — to save a call that takes milliseconds against an ELK
re-layout that has to happen either way. `analysis.setGrouping` returns a whole view, the same
arrangement `analysis.get` and `analysis.run` already use.

It also spends nothing, and there is no path from it to the runner. That is requirement 2, and
[10-grouping-modes.spec.ts](../tests/e2e/specs/10-grouping-modes.spec.ts) proves it the only way it
can be proved: by counting what the provider was asked for, across a switch, a switch back, and a
restart.

### Four statistics move with the grouping, and are recomputed rather than stored twice

`containerCount`, the two container sizes and `riskCount`'s container term depend on how the change
was grouped. `Analysis.StatisticsFor` recomputes those from the stored document, following the
precedent `Analysis.ReadingOrder` already set: everything needed is in the document, and a second
stored copy of twenty numbers is a second thing that can be stale.

### Diagnostics are filtered to the picture on screen

Cluster, entry-point and reading-order rules run once per grouping, so their findings belong to one
grouping. `AnalysisDiagnostic` records which, each message names it in prose so a repair round stays
actionable, and the projection shows the reviewer the observations about the change as a whole plus
the ones about the grouping they are looking at. A warning that nothing leads to a node in a cluster
that does not exist in the current view is not something a reviewer can act on.

### An old analysis opens in dependency flow rather than failing

The contract renamed the fields that carry a grouping, so a document written before Iteration 11 has
none of the new names and would deserialise into a result with no clusters at all — an analysis
someone paid for, opening as an empty diagram.

`LegacyAnalysisDocument` upgrades it on read instead: the old `containers` become the dependency-flow
grouping with each container's members sorted by the rank they carried, `readingOrder` becomes
`dependencyReadingOrder`, `entry_point` is dropped now that the container declares the entry node,
and no change-clusters grouping is invented. It is detected by shape rather than by the row's schema
version — a document is upgradeable exactly when it has the old field and not the new one, and
reading that off the JSON cannot disagree with itself. The precedent is `Analysis.ChangedFiles`,
where an analysis older than schema 1.8 opens with no line counts rather than not at all.

### The chosen grouping is the second thing a reviewer writes into a stored analysis

Database schema 7 adds a nullable `grouping_mode` column beside `reviewed_json`, and
`IAnalysisStore.SetGroupingAsync` is the second method that changes a stored analysis. The invariant
worth keeping is the one that still holds: **neither can touch the document.** Which of two groupings
someone is reading is not part of the model's answer.

Null means "never chose one", and then the application-wide default applies — the grouping last
chosen anywhere, which is what a new analysis opens in. A re-run writes a new row and so opens at
that default again, exactly as it starts with nothing marked reviewed.

The renderer's own view state divides the same way. `graphCollapsed` and `diffContainerId` hold
*container* ids, and the other grouping's containers are different clusters, so `setAnalysis` resets
them on a grouping change. The file open in the diff panel, the reviewed marks, the panel width and
the band folds are node-keyed or presentational and survive — a reviewer who switches grouping while
reading a file is still reading that file.

## Formatted prose

### The model writes a Markdown subset, and the renderer draws exactly that subset

Every prose field — the overall summary, container summaries and explanations, the four node fields,
edge explanations and risks — is drawn as Markdown by `components/analysis/Markdown.tsx`, over a
parser of our own in `lib/markdown.ts`. It understands bold, italic, inline code, bullet and numbered
lists and fenced code blocks; a heading keeps its words as a bold line, and a table, a link or HTML
stays text. The prompt's **Formatting** section names that list, so the grammar the parser has to be
complete for is one we chose.

No package. `react-markdown` with `remark-gfm` is about forty transitive packages to render more than
we ask for, and every piece of it still has to be turned off again: raw HTML, links that would move
the WebView off `diffhacker://app`, images the CSP blocks anyway. `markdown-to-jsx` is small, but
turning HTML off in it is an option somebody has to remember to keep set. A parser that returns plain
objects, drawn by React, cannot produce markup at all, so it needs no sanitiser.

Three choices in the grammar are deliberate:

- **Only `*` makes emphasis, never `_`.** What a model writes with underscores in it is `snake_case`,
  `__init__.py` and `_private`, far more often than italics. CommonMark would render `__init__` bold.
- **Every newline is a line break**, as in a GitHub comment. Analyses stored before this were
  plain text shown with `white-space: pre-wrap`, and they still render as they did.
- **A risk gets inline formatting only.** A list or a code block inside one entry would put a
  paragraph back into the risk column that §0.1 keeps separate.

### A file the prose names opens from the text

A code span or a link whose text is a node's id or file path is drawn as a button that opens that
file's diff: `openDiffFor`, the same navigation as the card's own button. A trailing `:42`,
`:12-40` or `#L12` is ignored, and so are a leading `./` and backslashes. A basename alone never
matches, because `index.ts` names a dozen files and a reference that opened the wrong one would be
worse than none. `MarkdownReferences` builds one index per screen, and without it every code span
is text. That is why `plainText()` exists for the places prose is clamped or used as a hint
rather than drawn: the navigator's edge preview and the search results.

The Formatting section is ~1,150 characters, about 290 tokens a turn. It pays for itself only if
the model actually cites paths the way the changed-file list spells them, which is why it gives an
example and says what the citation does.

## Implementation groups

### The model declares them, because nothing else here can

An interface and the classes implementing it are one idea spread over several files, and a reviewer
reads them that way whether or not the diagram does. Drawing them as one box needs to know which
files those are — and §0.2.3 means the application cannot tell: it parses no language, so an
`interface`, an abstract base class, a Rust trait, a Swift protocol and a C header all look like
text. §0.2.1 settles who decides instead. The model already reads the code; `implementationGroups`
is where it says "this declares a contract, those fulfil it", in whatever language the repository
is written in.

It is a separate list rather than a kind of edge. The prompt tells the model never to classify an
edge as *calls* or *implements* (§0.2.7), and that stays true: the edges between an interface and
its implementations are still written as reading flow, in prose. The group is a second, narrower
statement beside them, and the prompt says both — including that most changes have none and the
definition is not to be stretched to fill the list.

### Null and empty are different answers

`AnalysisResult.ImplementationGroups` is the one list on the document that is nullable. Empty means
the model was asked and this change has no abstraction beside its implementations; null means
nobody asked — a run with the switch off, or any document written before 1.13, which reads as it
stands with no `LegacyAnalysisDocument` step. The toolbar's toggle is disabled in both cases, and
says which, because they need different things from the reviewer: one is fixed by analysing again,
the other is simply the change.

### Optional in the request, like the second grouping

The switch beside Analyse is the change-clusters opt-out's twin, for the same reason: turning it off
removes the prompt section, the schema property *and* its `$def` from the request, rather than asking
and discarding. `AnalysisResponseSchema.For(clusters, groups)` derives all four variants from the one
schema in `/schema`. It is remembered application-wide under `analysis.implementationGroups.produce`
and defaults to on; the cost is a short list of node ids.

### A group lives in one container, and a split is a warning

A merged box is drawn inside a container, so the wire projects each group onto the grouping on
screen: it sits in the abstraction's container and keeps the implementations that share it. The
model is told to keep a group together in every grouping, but an implementation left elsewhere is
`implementation_group_split` — a warning, never a repair round. It costs a smaller picture (that
file is drawn as its own box there), a repair round costs money, and a change-clusters grouping may
have a real reason to put the two apart.

The errors are only the things that would make the box undrawable: an id that is not a node, a
group with no implementations, and a node claimed twice — by two groups, twice by one, or as its own
implementation. A node drawn in two boxes, or in none, would break §0.2.5 on screen.

### Merging is the renderer's, and it is presentation

Whether the groups are drawn merged is a toggle in the toolbar that re-lays the diagram out and
never reaches the host, the same kind of decision as collapsing a container. It is remembered in
`localStorage` for the reasons `useTheme.ts` gives for the colour scheme, not in `app_settings`:
it is how someone reads, costs nothing and describes no analysis.

A merged box is one ELK leaf placed where its earliest member would have been, pinned first when any
member is the entry node. Every row is still its node — its own test id, state border, importance,
reviewed tick, controls, card and diff — and the surface resolves a click on the box to the row it
landed on. Edges attach to the box: one inside it is dropped (the box already says the two belong
together), and several landing on the same neighbour become one line labelled with a count whose
card lists each. A collapsed container still wins over a merged box inside it.

## Run transparency and the analysis library

Four follow-ups from the original Iteration 13 — the live run view, the tool-call inspector, the
library of earlier runs, and stale detection — landed together, because three of them rest on one
change: every analysis call can name *which* stored run it means.

### Which analysis is the caller's to say

Until now every `analysis.*` read and write went through `GetLatestAsync`, which was right while
"the analysis" meant the latest one. Reopening an earlier run broke that: a reviewer who regrouped or
marked a file in last week's run would have written it onto today's. So `analysis.get`,
`analysis.setGrouping` and `analysis.setReviewed` take an optional `analysisId` — absent means the
latest, so an older renderer changes nothing — and the three new calls that are always about one run
(`trace`, `checkFreshness`, `delete`) take it required, in `AnalysisRefRequest`. The renderer always
sends `view.analysisId`. An id is honoured only for the repository it belongs to; one from another
repository is `analysis_not_found`, not a cross-repository read. Nothing about which run is open is
held host-side, for the reason the repository path travels on every request.

`AnalysisView.isLatest` is what lets the screen say "you are looking at an earlier run". It is
computed against the library rather than stored, so deleting or pruning changes it without a write.

### The live strip counts on the host, not in the renderer

The tool log keeps its most recent 200 rows, so counting rows would undercount any long run. The
count rides on every `analysis.toolCall` event as `toolCallCount`, like the token totals: `LlmSession`
increments it with `Interlocked` as each call *finishes* — calls in one turn run concurrently, and
`_toolCalls` is only filled once the whole turn is done, which would have made the figure sit still
through a turn of six reads and then jump. Elapsed time is the renderer's own clock from the moment
it asked for the run, ticking once a second, because the moment a reviewer most wants to see it move
is a long silence in which no event arrives.

### The trace is its own call

`LlmToolCallRecord`s were already stored in `trace_json`; what was missing was a way to read them.
They are not on `AnalysisView`: five hundred calls at a few hundred characters each would travel with
every read and every grouping switch. `analysis.trace` returns them sorted by ordinal — sorted there
rather than trusted to the array's order, because "in order" is the requirement — and the inspector
fetches them the first time it is unfolded. Arguments and results stay previews (200 and 500
characters, the limits `LlmToolCallRecord` already had); the size is of the whole answer.

### The library reads no documents

`IAnalysisStore.ListSummariesAsync` selects the scalar columns and lifts the numbers out of the JSON
in SQL — `json_extract(statistics_json, …)` for files, lines, nodes and containers,
`json_array_length(trace_json, '$.toolCalls')` for calls — and never selects `document_json`. Twenty
five-hundred-node graphs parsed to show twenty dates would make the list anything but instant. A
row whose statistics predate a field lists with a zero rather than failing, and
`SqliteAnalysisStoreTests` proves the document is never read by corrupting it.

Retention stays at 20 per repository, pruned on save as before, now named
`AnalysisLibraryPolicy.RetentionLimit` so the store that prunes and the library that tells the
reviewer it does share one number. Deleting a run is `DeleteOneAsync`, behind a confirmation in the
UI. It is the third method on `IAnalysisStore` that changes what is stored, and like the other two it
cannot edit a document — it removes a row whole, and the reviewed marks and chosen grouping, being
columns on it, go with it.

### Stale means the working tree differs, measured by content

What an analysis describes is one exact changeset, so there is no threshold: any difference is
stale. The comparison is per path:

- **HEAD moved** is always stale, because the committed side of every diff moved with it.
- **A file added to or removed from the changeset** is stale.
- **A file in both** is modified when its status or previous path changed, or when its content
  changed. Content means a **SHA-256 of the working-tree bytes**, recorded when the run started, in
  `ChangedFileFacts.ContentSha256`.

Modification times were the obvious alternative and cannot do the one thing verification step 6
asks: an edit reverted byte for byte bumps the mtime twice and is the same file. Line counts alone
miss an edit that keeps them. A hash does neither.

The hash is taken by the git layer (`ChangesetQuery.HashContent`) rather than asked of git:
`git hash-object` can write to the object database, which is why it is not on the allowlist, and the
working-tree side of an unstaged file is all zeros in `--raw`. It reads only the files the changeset
already names, under the root. A symlink is hashed by where it points and never followed, a
submodule by the commit it is on, and a deleted file has no working-tree side and so no hash. A file
that cannot be read gets null, and compares by line counts instead of failing the run.

It lives in `files_json`, so there was no database migration. An analysis from before 1.14 has no
hashes and is compared by line counts; one from before 1.8 has no per-file record at all and can only
compare HEAD. The report says which (`basis`), and the banner says the check was weaker when it was.

### Checked after the analysis is drawn, never before

Reading and hashing a large changeset takes a moment, and requirement 8 wants reopening to be
instant. So the renderer asks `analysis.checkFreshness` once the analysis is on screen — when it is
opened, reopened or produced — and again when the window regains focus, the moment a reviewer comes
back from their editor. At most one check is in flight per analysis, focus checks are at least two
seconds apart, and a verdict that arrives for an analysis no longer on screen is dropped. A check
that fails is logged and shows nothing: it must never take the analysis away or call it stale when
nobody knows. The banner sits above the analysis rather than replacing it, because what is on screen
is still a faithful account of the change as it was. Dismissing it lasts for the session and only for
the difference it described, so a later check that finds something else shows it again.

## Configurable limits and the budget prompt

Follow-up 10 asked for two things: `LlmBudget`'s tool-call and token ceilings become user-configurable,
and hitting one no longer fails a run outright — it pauses and asks whether to continue. Both an
analysis run and a repository-profiling run go through the same `LlmSession` loop, so one mechanism
serves both.

### The pause is a callback, not a new outcome

`ILlmSession.RunAsync` gained one optional parameter, `BudgetDecisionCallback? onBudgetExceeded`,
defaulting to null. Every limit check (`LlmSession.ExceededLimit`) is unchanged in what it detects;
what changed is what happens next. With no callback, a reached limit hard-stops exactly as before —
every caller written before this feature, including every existing test, is untouched by construction
rather than by an update. With one, the session calls it, and:

- **Continue** raises the one limit that was hit by the amount it started at (`LlmSession.Extend`) and
  loops the check again — deliberately linear (500, 1000, 1500 tool calls, …) rather than doubling, so
  repeated continues cost predictable rather than runaway room. Turns and the cost ceiling go through
  the same callback for consistency, though neither is user-configurable the way tool calls and tokens
  are (§0.6 leaves multi-pass and cost estimation to later work).
- **Stop** produces the same `LlmRunResult` a hard stop always did — `LlmRunOutcome.BudgetExceeded`,
  the same explanation — so nothing downstream needed to change: a stopped run is a failed run, and
  §0.2.8 already forbids showing anything partial for one of those.

`MaxConsecutiveToolFailures` — a stuck-loop detector, not a spend limit — still hard-stops
unconditionally. Asking the reviewer whether to continue when the model has demonstrably stopped
making progress would not serve them; that stop stays exactly as it was.

### Asking the renderer needed no new RPC direction

The host already pushes notifications freely (`IRpcNotifier`) and the renderer already calls the host
freely; nothing here needed the host to *call into* the renderer and wait for a return value.
`BudgetDecisionNotifier` sends `run.budgetLimitReached` with a fresh `promptId` and then awaits a
`TaskCompletionSource` it holds keyed by that id — not on a reply to the notification, which JSON-RPC
notifications do not have, but on the renderer's own follow-up call, `run.answerBudgetPrompt`, landing
on the small `BudgetPromptRpcTarget` and resolving the matching wait. The class implements both
`IBudgetDecisionPrompt` (what `AnalysisRunner`/`ProfileBuilder` ask through) and `IBudgetPromptResolver`
(what the RPC target resolves through), registered under both interfaces from one singleton.

`run.budgetLimitReached` and `run.answerBudgetPrompt` are named for the run rather than for analysis or
profile specifically, the same reasoning `analysis.toolCall` already stretches to profiling: one
mechanism, one pair of methods, regardless of which kind of run hit the limit.

### The character budget stopped being a runaway guard

The unrelated half of the same follow-up: `ProjectProfile.EffectiveCharacterBudget` is the user's own
dial (§0.6), not a spend limit, and `ProfileBuilder` used to fail the whole run when a document was
still over it after one shorten attempt (`ProfileFailures.OverBudget`, now deleted along with its
`en.ts` string — nothing produces it any more). It now keeps the shortened document only if shortening
actually helped, and otherwise saves the original — complete and valid, merely long — rather than
discarding it. `ProfileProvenance.DocumentCharacters` against `EffectiveCharacterBudget` is already
enough for the screen to say a stored profile is oversized; no new field was needed.

### A migration was the easy part to miss

`LlmProviderProfile.MaxToolCallsOverride`/`MaxTotalTokensOverride` round-trip through
`SqliteProviderProfileStore` as their own INTEGER columns, the same shape `ContextWindowTokens`
already used — provider profiles are explicit columns, not a JSON document, so a new domain property
needs a schema migration (`AppDatabase.CurrentSchemaVersion` 7 → 8) and explicit mapping in
`ProviderProfileRow`, or it never reaches the database at all. The first pass of this feature added the
domain property, the RPC mapping and the settings-form fields but skipped the storage layer entirely;
nothing failed to compile or to unit-test, because every layer above the missing column faithfully
passed a value on and every layer below it faithfully returned null. Only
`13-budget-limits.spec.ts` — a real save, a real restart-free re-read, a real run against the
now-configured limit — caught it, which is the whole reason that suite exists rather than a unit test
standing in for it.

## Analysis parts

A reviewer can switch off parts of an analysis to make runs cheaper and faster: the second grouping
and implementation groups (both optional already), risks, and three kinds of prose — node, edge and
cluster explanations — plus how much the prose asks for (brief, medium, detailed). The defaults are
set in Settings; the run options beside the Analyse button change them for one run.

### Taken out of the request, and the model is told not to do the work

As with the second grouping, a part that is off is removed from the request rather than asked for and
thrown away. `AnalysisPrompt.SystemPrompt(options)` drops that part's guidance, and
`AnalysisResponseSchema.For(options)` removes its fields. `additionalProperties: false` then makes an
answer that writes them anyway a schema failure.

Removing the field stops the model writing it *there*, not doing the work. A model that has always
been asked for risks notices a dangerous migration and says so in a summary instead. So the prompt gains
one short `## !! NOT WANTED ON THIS RUN !!` section with a line per disabled part that a model would
otherwise volunteer, placed near the top so it shapes exploration and not only the answer.
Implementation groups are left out of it: nothing would make a model declare them unprompted, and naming
them only to forbid them would cost their tokens.

The schema variant also rewrites the few descriptions that mention a removed part ("No risks here.",
the `risky` state, the list of node prose fields). `AnalysisResponseSchemaTests` checks that the
risk-free schema contains no "risk" in any case, so a description added later that talks about risk
fails a test rather than quietly inviting the work back. `risky` goes from the node-state enum with
the risk fields: it is a risk judgement under another name.

That line or two of "not wanted" can make the *prompt* longer than the one it replaced. The honest
measure is what a turn re-sends — the prompt plus the schema twice — and `AnalysisPromptTests` checks
that every opt-out makes that smaller. The larger saving is in output tokens, which no preamble
measure sees: edge explanations alone are one sentence per edge, and there can be hundreds.

### A re-serialised variant must not be bigger than what it was cut from

The first version of the edge-explanations test failed: the variant came out *larger* than the full
schema file. `JsonNode.ToJsonString` used the default encoder, which writes every apostrophe and dash
in the descriptions as a six-character `\uXXXX` escape, and the platform newline, which is `\r\n` on
Windows. Together those outweighed the one field removed. Variants are now written with
`UnsafeRelaxedJsonEscaping` (the text is never embedded in HTML) and `NewLine = "\n"`. This also
shrank the change-clusters variant, which had carried the same overhead since Iteration 11, and made
the request the same on every OS.

### Defaults in Settings, overrides forgotten

Before 1.15, the last run's checkbox became the next run's default. A reviewer who trimmed one expensive
re-run had silently trimmed every run after it. Now `AnalysisDefaults` holds the defaults, and
`analysis.saveDefaults` — called only from Settings — is the only thing that writes them. An
`analysis.run` request names its parts. Any it leaves out come from the defaults, so a renderer that
does not know about a part cannot change what a run costs, and nothing about the request is remembered.
The renderer drops its override once a run succeeds and keeps it after a failure, so a retry asks for
the same thing. The two settings keys that existed before are still the keys, so an upgrade keeps a
reviewer's choice.

### Brief is the default verbosity

Most prose is read in a hover card. Medium — the lengths every analysis before 1.15 was written
against — often ran past what a card shows, and output tokens are the dearest part of a run. Brief
halves the prose budgets; Detailed roughly doubles them. Titles stay the same at every level, because
the box clamps them anyway. Verbosity changes the prompt's numbers and the validator's "too long"
warning, never the schema: lengths stay asked for rather than enforced, for the reason
`AnalysisFieldBudgets` gives.

### What was asked for is stored beside the answer

An empty risk list means two different things: nothing was found, or nobody asked. Only the second
justifies hiding the risk column; showing "No risks were reported" for it would be a reassurance nobody
earned. So `Analysis.Requested` is stored in schema 9's nullable `options_json`, beside the document for
the reason `grouping_mode` is. The view reports it as `risksProduced` and three `*ExplanationsProduced`
flags. An older row infers what it can from its document (the two groupings, implementation groups)
and reports everything else as produced, which it was. Its verbosity stays absent rather than being
guessed.

The renderer reads those flags through one `AnalysisPartsProvider` over the analysis surface, following
`GraphActionsContext`'s rule, rather than each card subscribing to the store. A part that was not
asked for is left out — no risk column, no risk register, no risk statistics — or, where a card would
otherwise be empty, replaced by one line saying the explanations were not asked for. The provenance line
lists what was skipped.

### The document's prose fields are no longer `required`

System.Text.Json enforces C#'s `required` keyword, so an answer to a schema without `whatChanged` would
not deserialise at all. The prose fields on `AnalysisNode`, `AnalysisContainer` and `AnalysisEdge`
default to empty instead. `AnalysisValidator` insists on `whatChanged` and `whyItChanged` whenever node
explanations were asked for, and on the title always. Only the root `summary` stays required: the
overall summary is never optional.

## User guide and Help

A **Help** button in the header, on every screen, opens a screen of its own. It holds a fifteen-step
guide with a screenshot per step, what the product does, how to read the diagram, the keyboard
shortcuts, an FAQ and troubleshooting. The same content is in [docs/user-guide.md](user-guide.md) for
anyone reading the repository before installing. The start screen offers the guide on a card that
stays gone once dismissed.

### Screenshots, not an interactive tour

A guided tour over the real analysis screen with mock data was designed first. It would have run the
unmodified `AnalysisScreen` inside a nested `RpcProvider` with an in-memory transport answering
`analysis.*` and `changeset.fileContent`. It was dropped at the user's request in favour of
step-by-step instructions with pictures. The pictures cover setup — provider, repository, profile —
which a tour confined to the analysis screen could not, and a guide has no store state to snapshot
and restore.

### The screenshots are generated, by the suite that proves the steps work

`tests/e2e/specs/15-user-guide.spec.ts` follows the guide against the real application and captures
each step as it goes. It uses a fixture repository built to be read (`guideFixture.ts`) and the stub
provider answering the way a good model would. So a step that stops being possible fails a test
before it misleads a reader, and refreshing every picture after a UI change is one command:
`npm run docs:screenshots` in `tests/e2e`, which is the spec with `--update-snapshots`. A normal run
writes a guide screenshot into the source tree only when it is missing, so the first set bootstraps
itself and no ordinary run dirties a tracked file. Captures are at 1440×900 CSS pixels, light theme
only. One set keeps the bundle small, and the images are captured at one pixel per CSS pixel, so the
stepper can show them at no more than natural size.

No personal detail reaches an image. The fixture lives under the temp directory, whose path carries
the operating-system user name, and the stub listens on a random port. `GuideCamera` rewrites both in
the page's text and inputs before each capture, then fails the capture if the user name still
appears inside a path. That rewriting is test code; nothing in the product knows about it.

### One set of words, drawn twice

Every word is in `en.help` (§0.6's one resource layer), written in the Markdown subset
`lib/markdown.ts` parses, which GitHub renders the same way. The Help screen draws it with the same
`Markdown` component the analysis uses. `help/userGuideMarkdown.ts` renders the same strings, in the
same order, into `docs/user-guide.md`, and `userGuideMarkdown.test.ts` compares that with a Vitest
file snapshot, so the committed document cannot drift. `npm run docs:guide` in `src/ui` rewrites it.
The document links the screenshots where the application bundles them rather than keeping copies.

The step order lives in `help/guideSteps.ts` because the steps carry screenshots. It has no imports,
so the end-to-end suite can import it as it imports `en.ts`. `helpContent.ts` makes a step without
copy, or copy without a step, a compile error, and `guideImages.test.ts` fails when a step's picture
is missing. The screenshots are loaded through `import.meta.glob`, so a missing one is a placeholder
rather than a broken build.

### Help asks the host nothing

It is the catalogue and a folder of images, so it works before a provider, a repository or even git
is set up. `openHelp` records the screen it was opened from and `closeHelp` returns there, which
matters most on the analysis screen, where Back would otherwise mean the repository screen.

### The end-to-end suite shared the developer's browser profile

Writing spec 15 found an older bug in the harness. Redirecting `LOCALAPPDATA` never moved WebView2's
profile, because PhotinoX finds its folder through the known-folder API, as `AppPaths` does. Every
launch therefore shared `%LOCALAPPDATA%\PhotinoX\EBWebView` and its `localStorage` with the
developer's own window. The guide offer dismissed in one run was still dismissed in the next. The
harness now also sets `WEBVIEW2_USER_DATA_FOLDER`, which WebView2 honours over the host's choice.

No dependency was added: the stepper, the section picker (the pressed-button group again) and the FAQ
(native `<details>`) are all built from what was already here.
