# ADR 003: Microsoft.Extensions.AI Integration

- Status: Accepted
- Date: 2026-03-05

## Context

GeminiSharpSDK wraps the Gemini CLI with a bespoke API surface (`GeminiClient`/`GeminiThread`/`RunResult`). The .NET ecosystem has standardized on `Microsoft.Extensions.AI` abstractions (`IChatClient`) for provider-agnostic AI integration with composable middleware pipelines.

## Decision

Implement `IChatClient` from `Microsoft.Extensions.AI.Abstractions` in a **separate NuGet package** (`ManagedCode.GeminiSharpSDK.Extensions.AI`) that adapts the existing SDK types without modifying the core SDK.

### Key design choices

1. **Separate package** — Core SDK remains M.E.AI-free. The adapter is opt-in, following the pattern of `Microsoft.Extensions.AI.OpenAI` being separate from `OpenAI`.

2. **Custom AIContent types** — Rich Gemini items (command execution, file changes, MCP tool calls, web searches, multi-agent collaboration) are surfaced as typed `AIContent` subclasses rather than being flattened to text. This preserves full fidelity of Gemini output.

3. **Gemini-specific options via AdditionalProperties** — Standard `ChatOptions` properties (`ModelId`, `ConversationId`) map directly. Gemini-unique features use `gemini:*` prefixed keys in `ChatOptions.AdditionalProperties` (for example `gemini:sandbox_mode`). Options not supported by the current headless CLI contract fail fast instead of silently degrading.

4. **Thread-per-call with ConversationId resume** — Each `GetResponseAsync` call creates or resumes a `GeminiThread`. Thread ID flows via `ChatResponse.ConversationId` for multi-turn continuity.

5. **No AITool support** — Gemini CLI manages tools internally (commands, file changes, MCP). Consumer-registered `ChatOptions.Tools` are ignored; tool results surface as custom `AIContent` types instead.

## Diagram

```mermaid
flowchart LR
  Consumer["Consumer code\n(IChatClient)"]
  Adapter["GeminiChatClient\n(Extensions.AI)"]
  Core["GeminiClient\n(Core SDK)"]
  CLI["gemini --prompt ... --output-format stream-json"]

  Consumer --> Adapter
  Adapter --> Core
  Core --> CLI

  subgraph "M.E.AI Middleware (free)"
    Logging["UseLogging()"]
    Cache["UseDistributedCache()"]
    Telemetry["UseOpenTelemetry()"]
  end

  Consumer -.-> Logging
  Logging -.-> Cache
  Cache -.-> Telemetry
  Telemetry -.-> Adapter
```

## Consequences

### Positive

- SDK participates in .NET AI ecosystem: DI registration, middleware pipelines, provider swapping.
- Consumers get logging, caching, and telemetry for free via M.E.AI middleware.
- Rich Gemini items preserved as typed content, not lost.
- Adapter behavior stays aligned with the real installed CLI contract instead of a guessed chat abstraction.

### Negative

- Impedance mismatch: Gemini is an agentic coding tool, not a simple chat API. Multi-turn via message history doesn't map cleanly (uses thread resume instead).
- Current headless CLI does not expose generic chat-tuning knobs such as temperature/topP/topK, and unsupported options are rejected explicitly.
- Streaming is event-level (`init`/`message`/`tool_use`/`tool_result`/`result`), not token-level.

### Neutral

- Additional NuGet package to maintain.
- `ChatOptions.Tools` is a no-op; documented as limitation.

## Alternatives considered

- Implement `IChatClient` directly in core SDK: rejected to avoid mandatory M.E.AI dependency.
- Flatten all Gemini items to `TextContent`: rejected to preserve rich output fidelity.
- Map Gemini commands/file changes as `FunctionCallContent`: rejected because tools are internal to CLI, not consumer-invocable.
