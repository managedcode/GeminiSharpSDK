using System.Text.Json.Nodes;
using ManagedCode.GeminiSharpSDK.Internal;

namespace ManagedCode.GeminiSharpSDK.Models;

public enum CommandExecutionStatus
{
    InProgress,
    Completed,
    Failed,
}

public enum ToolResultStatus
{
    Success,
    Error,
}

public enum PatchChangeKind
{
    Add,
    Delete,
    Update,
}

public enum PatchApplyStatus
{
    Completed = 0,
    Failed = 1,
    InProgress = 2,
}

public enum McpToolCallStatus
{
    InProgress,
    Completed,
    Failed,
}

public enum CollabToolCallStatus
{
    InProgress,
    Completed,
    Failed,
}

public enum CollabTool
{
    SpawnAgent,
    SendInput,
    Wait,
    CloseAgent,
}

public enum CollabAgentStatus
{
    PendingInit,
    Running,
    Completed,
    Errored,
    Shutdown,
    NotFound,
}

public sealed record FileUpdateChange(string Path, PatchChangeKind Kind);

public sealed record McpToolCallResult(IReadOnlyList<JsonNode> Content, JsonNode? StructuredContent);

public sealed record McpToolCallError(string Message);

public sealed record TodoItem(string Text, bool Completed);

public sealed record CollabAgentState(CollabAgentStatus Status, string? Message);

public abstract record ThreadItem(string Id, string Type);

public sealed record ToolUseItem(string Id, string ToolName, JsonNode? Parameters)
    : ThreadItem(Id, GeminiProtocolConstants.ItemTypes.ToolUse);

public sealed record ToolResultItem(string Id, ToolResultStatus Status, string? Output, ThreadError? Error)
    : ThreadItem(Id, GeminiProtocolConstants.ItemTypes.ToolResult);

public sealed record AgentMessageItem(string Id, string Text)
    : ThreadItem(Id, GeminiProtocolConstants.ItemTypes.AgentMessage);

public sealed record ReasoningItem(string Id, string Text)
    : ThreadItem(Id, GeminiProtocolConstants.ItemTypes.Reasoning);

public sealed record CommandExecutionItem(
    string Id,
    string Command,
    string AggregatedOutput,
    int? ExitCode,
    CommandExecutionStatus Status) : ThreadItem(Id, GeminiProtocolConstants.ItemTypes.CommandExecution);

public sealed record FileChangeItem(string Id, IReadOnlyList<FileUpdateChange> Changes, PatchApplyStatus Status)
    : ThreadItem(Id, GeminiProtocolConstants.ItemTypes.FileChange);

public sealed record McpToolCallItem(
    string Id,
    string Server,
    string Tool,
    JsonNode? Arguments,
    McpToolCallResult? Result,
    McpToolCallError? Error,
    McpToolCallStatus Status) : ThreadItem(Id, GeminiProtocolConstants.ItemTypes.McpToolCall);

public sealed record CollabToolCallItem(
    string Id,
    CollabTool Tool,
    string SenderThreadId,
    IReadOnlyList<string> ReceiverThreadIds,
    string? Prompt,
    IReadOnlyDictionary<string, CollabAgentState> AgentsStates,
    CollabToolCallStatus Status) : ThreadItem(Id, GeminiProtocolConstants.ItemTypes.CollabToolCall);

public sealed record WebSearchItem(string Id, string Query)
    : ThreadItem(Id, GeminiProtocolConstants.ItemTypes.WebSearch);

public sealed record TodoListItem(string Id, IReadOnlyList<TodoItem> Items)
    : ThreadItem(Id, GeminiProtocolConstants.ItemTypes.TodoList);

public sealed record ErrorItem(string Id, string Message)
    : ThreadItem(Id, GeminiProtocolConstants.ItemTypes.Error);
