# Iteration 4 — LLM provider layer

> **Session setup.** Shared context is in [CLAUDE.md](../../CLAUDE.md) and is loaded
> automatically. Read it before this file. Read **Raise before implementing** at the bottom
> of this page *before* writing code, and ask everything in one batch.

| | |
|---|---|
| **Depends on** | [1](iteration-01-foundation.md), [2](iteration-02-shell-settings-repository.md) |
| **Blocks** | 5, 6, 7, 13 |
| **Status** | Not started |

## Goal

One internal contract, five providers, tool calling working on all of them.

## Context

The whole product is one long tool-using LLM conversation. Providers differ in tool-call
format, streaming, token accounting and error semantics; **all of that is absorbed here** so
analysis logic never knows which provider it is talking to (§0.2.4).

## Fixed technical decisions

- Build on **`Microsoft.Extensions.AI`** and `IChatClient`. Do not hand-roll five HTTP
  clients.
- Five providers collapse to three implementations:
  - **OpenAI** — official `OpenAI` SDK.
  - **Grok (xAI)** and **DeepSeek** — OpenAI-compatible; same client, different base URL.
  - **Anthropic** — official `Anthropic` package.
  - **Gemini** — `Google.Cloud.VertexAI.Extensions` or an equivalent `IChatClient` package.
- Expose **"OpenAI-compatible endpoint"** as a user-selectable provider type. This gives
  Mistral, Qwen, Together, OpenRouter and local Ollama for free.
- Wrap `IChatClient` in a thin **`ILlmSession`** owned by this project, carrying the things
  MEAI does not: per-run token and cost accounting, tool-call budgets, hard stops,
  retry/backoff policy. **MEAI types must not leak into `DiffHacker.Core`.**
- **Verify current package status and version at implementation time; this area moves
  quickly.** If a package named here is unavailable or unsuitable, **stop and ask** — do not
  silently substitute (§0.4).

## Requirements

1. `ILlmSession` supporting a multi-turn tool-calling loop and a final structured response.
2. All five providers working through it, including multiple tool calls per turn where
   supported.
3. Token usage reported per request and cumulatively per run, with estimated cost where
   pricing is known.
4. Cancellation: the user can stop a run at any point and the request chain unwinds cleanly.
5. Retry with backoff for rate limits and transient failures. Non-transient errors surface as
   **distinct, actionable** messages: bad key, model not found, context overflow, content
   filter, quota exhausted.
6. Context-window overflow is detected explicitly and reported as a **typed condition the
   analysis layer can react to**, not as an opaque failure. Iteration 7 depends on this.
7. Structured-output support: request JSON conforming to a schema from `/schema`, with
   per-provider handling of how that is expressed.
8. Every request and tool call traceable in `log.txt`, **redacted of secrets**.
9. Tests run against a recorded/fake `IChatClient`. **No test hits a real provider.**

## Out of scope

Prompts, analysis logic, the toolbox. This iteration ships plumbing and a test conversation,
nothing that knows what a diff is.

## Done when

The same tool-calling test conversation succeeds against all five providers, reports usage,
and cancels cleanly mid-flight.

## How to verify

1. One canned conversation — "call this echo tool twice, then answer in this JSON shape" —
   passes against OpenAI, Anthropic, Gemini, Grok and DeepSeek, plus one OpenAI-compatible
   endpoint (Ollama locally is the cheapest proof).
2. Token counts and cost estimates are non-zero, per-request and cumulative, and match the
   provider's own reporting where it exposes it.
3. Cancel mid-turn: the chain unwinds, no unobserved task exceptions, partial usage is still
   reported.
4. Force each error class and confirm each maps to a **distinct** typed error: revoked key,
   nonsense model name, deliberately oversized context, content filter, exhausted quota.
5. Force a 429 and confirm backoff retries, then confirm a non-transient error does *not*
   retry.
6. Grep `log.txt` for the API key after a full run. Zero hits.
7. Confirm `DiffHacker.Core` compiles with no reference to `Microsoft.Extensions.AI` types.
8. Whole test suite runs offline.

## Raise before implementing

Batch these with anything else §0.4 of `CLAUDE.md` covers, and ask once.

- **Package availability and versions — this is explicitly flagged in the plan.** Check each
  of the OpenAI SDK, the Anthropic package, the Gemini `IChatClient` package and the MEAI
  version they all agree on. Report anything unmaintained, prerelease-only, or mutually
  incompatible **before** building on it. The Gemini option is the likeliest to have moved.
- **Cost data.** Requirement 3 says "where pricing is known", but hardcoded prices rot exactly
  the way hardcoded model lists do (§ Iteration 2, requirement 4). Propose where pricing comes
  from and how it stays current, and let the user choose.
- **Structured output on providers that do not support it natively.** Confirm the fallback:
  prompt-and-validate, tool-call-shaped output, or refuse the provider for analysis runs.
- **Streaming.** Requirements mention per-request usage but not streaming. Iteration 13 wants
  a live run view; confirm whether `ILlmSession` needs token-level streaming or whether
  per-turn events plus `report_progress` (Iteration 5) are enough.
- **Default budgets.** Iteration 13 makes max tool calls and max tokens per run configurable.
  Propose the defaults now, since hard stops are user-visible behaviour.
