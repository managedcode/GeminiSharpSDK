using System.Text.Json.Nodes;
using ManagedCode.GeminiSharpSDK.Internal;

namespace ManagedCode.GeminiSharpSDK.Models;

public sealed record Usage(int InputTokens, int CachedInputTokens, int OutputTokens);

public sealed record ThreadError(string Message);

public abstract record ThreadEvent(string Type);

public sealed record InitEvent(string SessionId, string Model)
    : ThreadEvent(GeminiProtocolConstants.EventTypes.Init);

public sealed record MessageEvent(string Role, string Content, bool Delta)
    : ThreadEvent(GeminiProtocolConstants.EventTypes.Message);

public sealed record ToolUseEvent(string ToolName, string ToolId, JsonNode? Parameters)
    : ThreadEvent(GeminiProtocolConstants.EventTypes.ToolUse);

public sealed record ToolResultEvent(string ToolId, ToolResultStatus Status, string? Output, ThreadError? Error)
    : ThreadEvent(GeminiProtocolConstants.EventTypes.ToolResult);

public sealed record ResultEvent(string Status, Usage? Usage, ThreadError? Error)
    : ThreadEvent(GeminiProtocolConstants.EventTypes.Result);

public sealed record ThreadStartedEvent(string ThreadId)
    : ThreadEvent(GeminiProtocolConstants.EventTypes.ThreadStarted);

public sealed record TurnStartedEvent()
    : ThreadEvent(GeminiProtocolConstants.EventTypes.TurnStarted);

public sealed record TurnCompletedEvent(Usage Usage)
    : ThreadEvent(GeminiProtocolConstants.EventTypes.TurnCompleted);

public sealed record TurnFailedEvent(ThreadError Error)
    : ThreadEvent(GeminiProtocolConstants.EventTypes.TurnFailed);

public sealed record ItemStartedEvent(ThreadItem Item)
    : ThreadEvent(GeminiProtocolConstants.EventTypes.ItemStarted);

public sealed record ItemUpdatedEvent(ThreadItem Item)
    : ThreadEvent(GeminiProtocolConstants.EventTypes.ItemUpdated);

public sealed record ItemCompletedEvent(ThreadItem Item)
    : ThreadEvent(GeminiProtocolConstants.EventTypes.ItemCompleted);

public sealed record ThreadErrorEvent(string Message)
    : ThreadEvent(GeminiProtocolConstants.EventTypes.Error);

public sealed record UnknownThreadEvent(string EventType, JsonNode Payload) : ThreadEvent(EventType);
