# ManagedCode.GeminiSharpSDK

[![CI](https://github.com/managedcode/GeminiSharpSDK/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/managedcode/GeminiSharpSDK/actions/workflows/ci.yml)
[![Release](https://github.com/managedcode/GeminiSharpSDK/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/managedcode/GeminiSharpSDK/actions/workflows/release.yml)
[![CodeQL](https://github.com/managedcode/GeminiSharpSDK/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/managedcode/GeminiSharpSDK/actions/workflows/codeql.yml)
[![NuGet](https://img.shields.io/nuget/v/ManagedCode.GeminiSharpSDK.svg)](https://www.nuget.org/packages/ManagedCode.GeminiSharpSDK)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/managedcode/GeminiSharpSDK/blob/main/LICENSE)

`ManagedCode.GeminiSharpSDK` is an open-source .NET SDK for driving the Gemini CLI from C#.

It is a CLI-first `.NET 10 / C# 14` SDK aligned with real `gemini` runtime behavior, with:
- thread-based API (`start` / `resume`)
- streamed JSONL events
- structured output schema support
- image attachments
- `--config` flattening to TOML
- NativeAOT-friendly implementation and tests on TUnit

All consumer usage examples are documented in this README; this repository intentionally does not keep standalone sample projects.

## Installation

```bash
dotnet add package ManagedCode.GeminiSharpSDK
```

## Prerequisites

Before using this SDK, you must have:
- `gemini` CLI installed and available in `PATH`
- an already authenticated Gemini session (`gemini login`)

Quick check:

```bash
gemini --version
gemini login
```

## Quickstart

```csharp
using ManagedCode.GeminiSharpSDK;

using var client = new GeminiClient();

var thread = client.StartThread(new ThreadOptions
{
    Model = GeminiModels.Gemini25Pro,
});

var turn = await thread.RunAsync("Diagnose failing tests and propose a fix");

Console.WriteLine(turn.FinalResponse);
Console.WriteLine($"Items: {turn.Items.Count}");
```

`AutoStart` is enabled by default, so `StartThread()` works immediately.

## Advanced Configuration (Optional)

```csharp
using var client = new GeminiClient(new GeminiClientOptions
{
    GeminiOptions = new GeminiOptions
    {
        // Override only when `gemini` is not discoverable via npm/PATH.
        GeminiExecutablePath = "/custom/path/to/gemini",
    },
});

var thread = client.StartThread(new ThreadOptions
{
    Model = GeminiModels.Gemini25Pro,
    ApprovalPolicy = ApprovalMode.Default,
    AdditionalDirectories = ["/workspace/shared"],
});
```

## Extended CLI Options

`ThreadOptions` maps to the current headless Gemini CLI surface (`--prompt`, `--output-format stream-json`, `--resume`, `--approval-mode`, `--include-directories`, and sandbox toggle). Unsupported legacy flags fail fast.

```csharp
var thread = client.StartThread(new ThreadOptions
{
    Model = GeminiModels.AutoGemini3,
    ApprovalPolicy = ApprovalMode.Default,
    AdditionalDirectories = ["/workspace/shared"],
    SandboxMode = SandboxMode.WorkspaceWrite,
    AdditionalCliArguments = ["--some-future-flag", "custom-value"],
});
```

## Gemini CLI Metadata

```csharp
using var client = new GeminiClient();

var metadata = client.GetCliMetadata();
Console.WriteLine($"Installed gemini-cli: {metadata.InstalledVersion}");
Console.WriteLine($"Default model: {metadata.DefaultModel ?? "(not set)"}");

foreach (var model in metadata.Models.Where(model => model.IsListed))
{
    Console.WriteLine(model.Slug);
}
```

`GetCliMetadata()` reads:
- installed CLI version from `gemini --version`
- default model from `~/.gemini/config.toml`
- model catalog from `~/.gemini/models_cache.json`

```csharp
var update = client.GetCliUpdateStatus();
if (update.IsUpdateAvailable)
{
    Console.WriteLine(update.UpdateMessage);
    Console.WriteLine(update.UpdateCommand);
}
```

`GetCliUpdateStatus()` compares installed CLI version with latest published `/gemini-cli` npm version and returns an update command matched to your install context (`bun` or `npm`).

When thread-level web search options are omitted, SDK does not emit a `web_search` override and leaves your existing CLI/config value as-is.

## Client Lifecycle and Thread Safety

- `GeminiClient` is safe for concurrent use from multiple threads.
- `StartAsync()` is idempotent and guarded.
- `StopAsync()` cleanly disconnects client state.
- `Dispose()` transitions client to `Disposed`.
- A single `GeminiThread` instance serializes turns (`RunAsync` and `RunStreamedAsync`) to prevent race conditions in shared conversation state.

## Streaming

```csharp
var streamed = await thread.RunStreamedAsync("Implement the fix");

await foreach (var evt in streamed.Events)
{
    switch (evt)
    {
        case InitEvent init:
            Console.WriteLine($"Session: {init.SessionId}");
            break;
        case MessageEvent { Role: "assistant" } message:
            Console.Write(message.Content);
            break;
        case ResultEvent { Usage: not null } result:
            Console.WriteLine($"Output tokens: {result.Usage.OutputTokens}");
            break;
    }
}
```

## Structured Output

```csharp
using System.Text.Json.Serialization;

public sealed record RepositorySummary(string Summary, string Status);

[JsonSerializable(typeof(RepositorySummary))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

var schema = StructuredOutputSchema.Map<RepositorySummary>(
    additionalProperties: false,
    (response => response.Summary, StructuredOutputSchema.PlainText()),
    (response => response.Status, StructuredOutputSchema.PlainText()));

var result = await thread.RunAsync<RepositorySummary>(
    "Summarize repository status",
    schema,
    AppJsonContext.Default.RepositorySummary);
Console.WriteLine(result.TypedResponse.Status);
Console.WriteLine(result.TypedResponse.Summary);
```

For advanced options (for example cancellation), use the `TurnOptions` overload:

```csharp
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

var result = await thread.RunAsync<RepositorySummary>(
    "Summarize repository status",
    AppJsonContext.Default.RepositorySummary,
    new TurnOptions
    {
        OutputSchema = schema,
        CancellationToken = cancellation.Token,
    });
```

`RunAsync<TResponse>` always requires `OutputSchema` (direct parameter or `TurnOptions.OutputSchema`).
For AOT/trimming-safe typed deserialization, pass `JsonTypeInfo<TResponse>` from a source-generated context.
Overloads without `JsonTypeInfo<TResponse>` are explicitly marked with `RequiresDynamicCode` and `RequiresUnreferencedCode`.

## Diagnostics Logging (Optional)

```csharp
using Microsoft.Extensions.Logging;

public sealed class ConsoleGeminiLogger : ILogger
{
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Console.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        if (exception is not null)
        {
            Console.WriteLine(exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}

using var client = new GeminiClient(new GeminiOptions
{
    Logger = new ConsoleGeminiLogger(),
});
```

## Images + Text Input

```csharp
using var imageStream = File.OpenRead("./photo.png");

var result = await thread.RunAsync(
[
    new TextInput("Describe these images"),
    new LocalImageInput("./ui.png"),
    new LocalImageInput(new FileInfo("./diagram.jpg")),
    new LocalImageInput(imageStream, "photo.png"),
]);
```

## Resume an Existing GeminiThread

```csharp
var resumed = client.ResumeThread("thread_123");
await resumed.RunAsync("Continue from previous plan");
```

## Microsoft.Extensions.AI Integration

An optional adapter package lets you use GeminiSharpSDK through the standard `IChatClient` interface from `Microsoft.Extensions.AI`.

```bash
dotnet add package ManagedCode.GeminiSharpSDK.Extensions.AI
```

### Basic usage

```csharp
using Microsoft.Extensions.AI;
using ManagedCode.GeminiSharpSDK.Extensions.AI;

IChatClient client = new GeminiChatClient(new GeminiChatClientOptions
{
    DefaultModel = GeminiModels.Gemini25Pro,
});

var response = await client.GetResponseAsync("Diagnose failing tests and propose a fix");
Console.WriteLine(response.Text);
```

### DI registration

```csharp
using ManagedCode.GeminiSharpSDK.Extensions.AI.Extensions;

builder.Services.AddGeminiChatClient(options =>
{
    options.DefaultModel = GeminiModels.Gemini25Pro;
});

// Then inject IChatClient anywhere:
app.MapGet("/ask", async (IChatClient client) =>
{
    var response = await client.GetResponseAsync("Summarize the repo");
    return response.Text;
});
```

### Resolve `IChatClient` from `IServiceProvider`

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ManagedCode.GeminiSharpSDK.Models;
using ManagedCode.GeminiSharpSDK.Extensions.AI.Extensions;

var services = new ServiceCollection();
services.AddGeminiChatClient(options =>
{
    options.DefaultModel = GeminiModels.Gemini25Pro;
});

using var provider = services.BuildServiceProvider();
var chatClient = provider.GetRequiredService<IChatClient>();
```

Keyed registration is also supported:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ManagedCode.GeminiSharpSDK.Models;
using ManagedCode.GeminiSharpSDK.Extensions.AI.Extensions;

var services = new ServiceCollection();
services.AddKeyedGeminiChatClient("gemini-main", options =>
{
    options.DefaultModel = GeminiModels.Gemini25Pro;
});

using var provider = services.BuildServiceProvider();
var keyedChatClient = provider.GetRequiredKeyedService<IChatClient>("gemini-main");
```

### Streaming

```csharp
await foreach (var update in client.GetStreamingResponseAsync("Implement the fix"))
{
    Console.Write(update.Text);
}
```

### Gemini-specific options via ChatOptions

```csharp
var options = new ChatOptions
{
    ModelId = GeminiModels.Gemini25Pro,
    AdditionalProperties = new AdditionalPropertiesDictionary
    {
        ["gemini:sandbox_mode"] = "workspace-write",
        ["gemini:reasoning_effort"] = "high",
    },
};

var response = await client.GetResponseAsync("Refactor the auth module", options);
```

### Rich content types

Gemini-specific output items (commands, file changes, MCP tool calls, web searches) are preserved as typed `AIContent` subclasses:

```csharp
foreach (var content in response.Messages.SelectMany(m => m.Contents))
{
    switch (content)
    {
        case CommandExecutionContent cmd:
            Console.WriteLine($"Command: {cmd.Command} (exit {cmd.ExitCode})");
            break;
        case FileChangeContent file:
            Console.WriteLine($"File changes: {file.Changes.Count}");
            break;
    }
}
```

See [docs/Features/meai-integration.md](https://github.com/managedcode/GeminiSharpSDK/blob/main/docs/Features/meai-integration.md) and [ADR 003](https://github.com/managedcode/GeminiSharpSDK/blob/main/docs/ADR/003-microsoft-extensions-ai-integration.md) for full details.

## Microsoft Agent Framework Integration

An optional adapter package lets you use GeminiSharpSDK with Microsoft Agent Framework `AIAgent`.

```bash
dotnet add package ManagedCode.GeminiSharpSDK.Extensions.AgentFramework --prerelease
```

This package currently ships as a prerelease because it depends on `Microsoft.Agents.AI` `1.0.0-rc4`.

### Basic usage

```csharp
using ManagedCode.GeminiSharpSDK.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

IChatClient chatClient = new GeminiChatClient();

AIAgent agent = chatClient.AsAIAgent(
    name: "GeminiAssistant",
    instructions: "You are a helpful coding assistant.");

AgentResponse response = await agent.RunAsync("Summarize the repository");
Console.WriteLine(response);
```

### DI registration

```csharp
using ManagedCode.GeminiSharpSDK.Extensions.AgentFramework.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

builder.Services.AddGeminiAIAgent(
    configureAgent: options =>
    {
        options.Name = "GeminiAssistant";
        options.ChatOptions = new ChatOptions
        {
            Instructions = "You are a helpful coding assistant."
        };
    });

app.MapGet("/agent", async (AIAgent agent) =>
{
    var response = await agent.RunAsync("Summarize the repository");
    return response.ToString();
});
```

### Keyed DI registration

```csharp
using ManagedCode.GeminiSharpSDK.Extensions.AgentFramework.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddKeyedGeminiAIAgent(
    "gemini-main",
    configureAgent: options =>
    {
        options.Name = "GeminiAssistant";
        options.ChatOptions = new ChatOptions
        {
            Instructions = "You are a helpful coding assistant."
        };
    });

using var provider = services.BuildServiceProvider();
var keyedAgent = provider.GetRequiredKeyedService<AIAgent>("gemini-main");
```

This package builds on the existing `IChatClient` adapter, so the canonical MAF path remains `IChatClient.AsAIAgent(...)`; the new package adds a supported Gemini-specific package boundary and DI convenience methods.

See [docs/Features/agent-framework-integration.md](https://github.com/managedcode/GeminiSharpSDK/blob/main/docs/Features/agent-framework-integration.md) and [ADR 004](https://github.com/managedcode/GeminiSharpSDK/blob/main/docs/ADR/004-microsoft-agent-framework-integration.md) for full details.

## Build and Test

```bash
dotnet build ManagedCode.GeminiSharpSDK.slnx -c Release -warnaserror
dotnet test --solution ManagedCode.GeminiSharpSDK.slnx -c Release
```
