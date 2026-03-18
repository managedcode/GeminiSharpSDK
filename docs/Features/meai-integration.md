# Feature: Microsoft.Extensions.AI Integration

Links:
Architecture: [docs/Architecture/Overview.md](../Architecture/Overview.md)
Modules: [GeminiChatClient.cs](../../GeminiSharpSDK.Extensions.AI/GeminiChatClient.cs)
ADRs: [003-microsoft-extensions-ai-integration.md](../ADR/003-microsoft-extensions-ai-integration.md)

---

## Purpose

Enable GeminiSharpSDK to participate as a first-class provider in the `Microsoft.Extensions.AI` ecosystem by implementing `IChatClient`, unlocking DI registration, middleware pipelines, and provider-agnostic consumer code.

---

## Scope

### In scope

- `IChatClient` implementation (`GeminiChatClient`) adapting `GeminiClient`/`GeminiThread`
- Input mapping: `ChatMessage[]` → Gemini prompt + images
- Output mapping: `RunResult` → `ChatResponse` with `UsageDetails` and rich content
- Streaming: `ThreadEvent` → `ChatResponseUpdate` mapping
- Custom `AIContent` types for Gemini-specific items (commands, file changes, MCP, web search, collab)
- DI registration via `AddGeminiChatClient()` / `AddKeyedGeminiChatClient()`
- Gemini-specific options via `ChatOptions.AdditionalProperties` with `gemini:*` prefix

### Out of scope

- `IEmbeddingGenerator` (Gemini CLI is not an embedding service)
- `IImageGenerator` (Gemini CLI is not an image generator)
- Consumer-side `AITool` registration (Gemini manages tools internally)
- `Temperature`, `TopP`, `TopK` mapping (Gemini uses `ModelReasoningEffort`)

---

## Business Rules

- `ChatOptions.ModelId` maps to `ThreadOptions.Model`.
- `ChatOptions.ConversationId` triggers thread resume via `ResumeThread(id)`.
- Multiple `ChatMessage` entries are concatenated into a single prompt while preserving original message chronology (Gemini CLI is single-prompt-per-turn).
- `ChatOptions.Tools` is silently ignored; tool results surface as custom `AIContent` types.
- `GetService<ChatClientMetadata>()` returns provider name `"GeminiCLI"` with default model from options.
- Streaming events map item-level, not token-level.
- Turn failures (`TurnFailedEvent`) propagate as `InvalidOperationException`.

---

## User Flows

### Primary flows

1. Basic chat completion
   - Actor: Consumer code using `IChatClient`
   - Trigger: `client.GetResponseAsync([new ChatMessage(ChatRole.User, "prompt")])`
   - Steps: map messages → create thread → RunAsync → map result
   - Result: `ChatResponse` with text, usage, thread ID as ConversationId

2. Streaming
   - Trigger: `client.GetStreamingResponseAsync(messages)`
   - Steps: map messages → create thread → RunStreamedAsync → stream events as ChatResponseUpdate
   - Result: `IAsyncEnumerable<ChatResponseUpdate>` with incremental content

3. Multi-turn resume
   - Trigger: `client.GetResponseAsync(messages, new ChatOptions { ConversationId = "thread-123" })`
   - Steps: resume thread with ID → RunAsync → map result
   - Result: Continuation in existing Gemini conversation

---

## Repository Additions (baseline: `bc11f2f2a7d546f34155d88a4800095be840921a`)

### Projects added to solution

- `GeminiSharpSDK.Extensions.AI/GeminiSharpSDK.Extensions.AI.csproj`
  - `ManagedCode.GeminiSharpSDK.Extensions.AI` package
  - `IChatClient` adapter (`GeminiChatClient`) and DI extensions
- `GeminiSharpSDK.Extensions.AI.Tests/GeminiSharpSDK.Extensions.AI.Tests.csproj`
  - mapper/DI test coverage for M.E.AI integration

### Major artifacts introduced

- Adapter entry points: `GeminiChatClient`, `GeminiChatClientOptions`, `GeminiServiceCollectionExtensions`
- Mapping layer: `ChatMessageMapper`, `ChatOptionsMapper`, `ChatResponseMapper`, `StreamingEventMapper`
- Rich content models: `CommandExecutionContent`, `FileChangeContent`, `McpToolCallContent`, `WebSearchContent`, `CollabToolCallContent`
- Docs: ADR `003` and this feature specification

---

## How to Obtain `IChatClient`

### Option 1: Direct construction

```csharp
using Microsoft.Extensions.AI;
using ManagedCode.GeminiSharpSDK.Extensions.AI;
using ManagedCode.GeminiSharpSDK.Models;

IChatClient client = new GeminiChatClient(new GeminiChatClientOptions
{
    DefaultModel = GeminiModels.Gemini25Pro,
});
```

### Option 2: Standard DI registration

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ManagedCode.GeminiSharpSDK.Extensions.AI.Extensions;
using ManagedCode.GeminiSharpSDK.Models;

var services = new ServiceCollection();
services.AddGeminiChatClient(options =>
{
    options.DefaultModel = GeminiModels.Gemini25Pro;
});

using var provider = services.BuildServiceProvider();
var chatClient = provider.GetRequiredService<IChatClient>();
```

### Option 3: Keyed DI registration

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ManagedCode.GeminiSharpSDK.Extensions.AI.Extensions;
using ManagedCode.GeminiSharpSDK.Models;

var services = new ServiceCollection();
services.AddKeyedGeminiChatClient("gemini-main", options =>
{
    options.DefaultModel = GeminiModels.Gemini25Pro;
});

using var provider = services.BuildServiceProvider();
var keyedClient = provider.GetRequiredKeyedService<IChatClient>("gemini-main");
```

---

## Diagrams

```mermaid
flowchart LR
  Input["IEnumerable<ChatMessage>"]
  MsgMapper["ChatMessageMapper"]
  OptMapper["ChatOptionsMapper"]
  Thread["GeminiThread.RunAsync"]
  RespMapper["ChatResponseMapper"]
  Output["ChatResponse"]

  Input --> MsgMapper
  MsgMapper --> Thread
  OptMapper --> Thread
  Thread --> RespMapper
  RespMapper --> Output
```

---

## Verification

### Test commands

- build: `dotnet build ManagedCode.GeminiSharpSDK.slnx -c Release -warnaserror`
- test: `dotnet test --solution ManagedCode.GeminiSharpSDK.slnx -c Release`
- format: `dotnet format ManagedCode.GeminiSharpSDK.slnx`

### Test mapping

- Mapper tests: `GeminiSharpSDK.Extensions.AI.Tests/ChatMessageMapperTests.cs`, `ChatOptionsMapperTests.cs`, `ChatResponseMapperTests.cs`, `StreamingEventMapperTests.cs`
- DI tests: `GeminiSharpSDK.Extensions.AI.Tests/GeminiServiceCollectionExtensionsTests.cs`

---

## Definition of Done

- `GeminiChatClient` implements `IChatClient` with full mapper coverage.
- DI extensions register client correctly.
- All mapper and DI tests pass.
- ADR and feature docs created.
- Architecture overview updated.
