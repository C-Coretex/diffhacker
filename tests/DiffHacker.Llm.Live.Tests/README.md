# Live provider conformance

Iteration 4 has two rules that pull against each other:

- **Done when** — the same tool-calling conversation succeeds against all five providers,
  reports usage, and cancels cleanly mid-flight.
- **Requirement 9** — tests run against a fake `IChatClient`. **No test hits a real provider.**

This project is how both stay true. Every test here skips unless the environment variable for
its provider is set, so `dotnet test src/DiffHacker.slnx` is offline and green on a machine with
no keys, and the live pass is something a person chooses to run with credentials they own.

The offline suite in [`../DiffHacker.Llm.Tests`](../DiffHacker.Llm.Tests) is where behaviour is
actually pinned — the loop, the budgets, the retry curve, the error taxonomy. This project
proves one thing the fakes cannot: that six real providers accept what
`ChatClientFactory` builds, and answer in a shape `LlmSession` can read.

## Running it

Set whichever providers you have, then run the project on its own:

```powershell
$env:DIFFHACKER_LIVE_OPENAI_KEY    = "sk-..."
$env:DIFFHACKER_LIVE_ANTHROPIC_KEY = "sk-ant-..."
$env:DIFFHACKER_LIVE_GEMINI_KEY    = "AIza..."
$env:DIFFHACKER_LIVE_GROK_KEY      = "xai-..."
$env:DIFFHACKER_LIVE_DEEPSEEK_KEY  = "sk-..."

dotnet test tests/DiffHacker.Llm.Live.Tests
```

A skipped test names the variable that would switch it on, so the output is also the
documentation.

### The free one

The generic OpenAI-compatible path costs nothing to prove. Point it at a local
[Ollama](https://ollama.com):

```powershell
$env:DIFFHACKER_LIVE_COMPATIBLE_BASEURL = "http://127.0.0.1:11434/v1"
$env:DIFFHACKER_LIVE_COMPATIBLE_MODEL   = "llama3.1"
```

No key is needed — the OpenAI client requires a credential, so a placeholder is supplied.

Note that a small local model may genuinely fail the conformance run: it has to call a tool
twice **and** answer in a schema. That is a finding about the model, not about DiffHacker, and
the failure message says which provider and model said what.

## Choosing the model

Each provider has a cheap default, and every default will eventually be retired. Override it
rather than editing the source:

```
DIFFHACKER_LIVE_OPENAI_MODEL      default gpt-4o-mini
DIFFHACKER_LIVE_ANTHROPIC_MODEL   default claude-haiku-4-5
DIFFHACKER_LIVE_GEMINI_MODEL      default gemini-2.5-flash
DIFFHACKER_LIVE_GROK_MODEL        default grok-3-mini
DIFFHACKER_LIVE_DEEPSEEK_MODEL    default deepseek-chat
DIFFHACKER_LIVE_COMPATIBLE_MODEL  default llama3.1
```

## What it costs

A handful of cents at most. Each conformance test is one short conversation of three or four
turns on a small model.

## What it does not cover

Four of requirement 5's error classes — a revoked key, an exhausted quota, a deliberately
oversized context and a content filter — cannot be arranged from a test without deliberately
breaking an account. Their mapping is pinned offline in `ProviderErrorMapperTests`, against the
exception shapes each SDK actually throws. Only "model not found" is forced for real here,
because a nonsense model name is free to ask for.
