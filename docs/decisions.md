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

### No package needed

- **Resilience/retry**: retry is ~60 lines in `RetryPolicy`, and the thing that had to be
  observable — telling a rate limit from a revoked key — is exactly what a pipeline library
  would have hidden.
- **Native folder picker / secret store**: PhotinoX already exposes `ShowOpenFolder`; the
  three credential backends are `[LibraryImport]` bindings, which is why `DiffHacker.Storage`
  — and only that project — sets `AllowUnsafeBlocks`.
