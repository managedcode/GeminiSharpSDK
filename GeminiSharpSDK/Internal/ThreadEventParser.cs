using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.GeminiSharpSDK.Models;

namespace ManagedCode.GeminiSharpSDK.Internal;

internal static class ThreadEventParser
{
    public static ThreadEvent Parse(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var type = GetRequiredString(root, GeminiProtocolConstants.Properties.Type);

        return type switch
        {
            GeminiProtocolConstants.EventTypes.Init => new InitEvent(
                GetRequiredString(root, GeminiProtocolConstants.Properties.SessionId),
                GetOptionalString(root, GeminiProtocolConstants.Properties.Model) ?? string.Empty),
            GeminiProtocolConstants.EventTypes.Message => new MessageEvent(
                GetRequiredString(root, GeminiProtocolConstants.Properties.Role),
                GetRequiredString(root, GeminiProtocolConstants.Properties.Content),
                GetOptionalBoolean(root, GeminiProtocolConstants.Properties.Delta) ?? false),
            GeminiProtocolConstants.EventTypes.ToolUse => new ToolUseEvent(
                GetRequiredString(root, GeminiProtocolConstants.Properties.ToolName),
                GetRequiredString(root, GeminiProtocolConstants.Properties.ToolId),
                ParseOptionalNode(root, GeminiProtocolConstants.Properties.Parameters)),
            GeminiProtocolConstants.EventTypes.ToolResult => new ToolResultEvent(
                GetRequiredString(root, GeminiProtocolConstants.Properties.ToolId),
                ParseToolResultStatus(GetRequiredString(root, GeminiProtocolConstants.Properties.Status)),
                GetOptionalString(root, GeminiProtocolConstants.Properties.Output),
                ParseOptionalThreadError(root, GeminiProtocolConstants.Properties.Error)),
            GeminiProtocolConstants.EventTypes.Result => new ResultEvent(
                GetRequiredString(root, GeminiProtocolConstants.Properties.Status),
                ParseOptionalUsage(root, GeminiProtocolConstants.Properties.Stats),
                ParseOptionalThreadError(root, GeminiProtocolConstants.Properties.Error)),
            GeminiProtocolConstants.EventTypes.ThreadStarted => new ThreadStartedEvent(GetRequiredString(root, GeminiProtocolConstants.Properties.ThreadId)),
            GeminiProtocolConstants.EventTypes.TurnStarted => new TurnStartedEvent(),
            GeminiProtocolConstants.EventTypes.TurnCompleted => new TurnCompletedEvent(ParseUsage(GetRequiredProperty(root, GeminiProtocolConstants.Properties.Usage))),
            GeminiProtocolConstants.EventTypes.TurnFailed => new TurnFailedEvent(ParseThreadError(GetRequiredProperty(root, GeminiProtocolConstants.Properties.Error))),
            GeminiProtocolConstants.EventTypes.ItemStarted => new ItemStartedEvent(ParseItem(GetRequiredProperty(root, GeminiProtocolConstants.Properties.Item))),
            GeminiProtocolConstants.EventTypes.ItemUpdated => new ItemUpdatedEvent(ParseItem(GetRequiredProperty(root, GeminiProtocolConstants.Properties.Item))),
            GeminiProtocolConstants.EventTypes.ItemCompleted => new ItemCompletedEvent(ParseItem(GetRequiredProperty(root, GeminiProtocolConstants.Properties.Item))),
            GeminiProtocolConstants.EventTypes.Error => new ThreadErrorEvent(GetRequiredString(root, GeminiProtocolConstants.Properties.Message)),
            _ => throw new InvalidOperationException($"Unsupported thread event type: {type}"),
        };
    }

    private static Usage? ParseOptionalUsage(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var usageElement)
            || usageElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return new Usage(
            GetRequiredInt32(usageElement, GeminiProtocolConstants.Properties.InputTokens),
            GetOptionalInt32(usageElement, GeminiProtocolConstants.Properties.Cached) ?? 0,
            GetRequiredInt32(usageElement, GeminiProtocolConstants.Properties.OutputTokens));
    }

    private static Usage ParseUsage(JsonElement usageElement)
    {
        return new Usage(
            GetRequiredInt32(usageElement, GeminiProtocolConstants.Properties.InputTokens),
            GetRequiredInt32(usageElement, GeminiProtocolConstants.Properties.CachedInputTokens),
            GetRequiredInt32(usageElement, GeminiProtocolConstants.Properties.OutputTokens));
    }

    private static ThreadError ParseThreadError(JsonElement errorElement)
    {
        return new ThreadError(GetRequiredString(errorElement, GeminiProtocolConstants.Properties.Message));
    }

    private static ThreadError? ParseOptionalThreadError(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var errorElement)
            || errorElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return ParseThreadError(errorElement);
    }

    private static ThreadItem ParseItem(JsonElement itemElement)
    {
        var itemType = GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Type);
        return itemType switch
        {
            GeminiProtocolConstants.ItemTypes.AgentMessage => new AgentMessageItem(
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Id),
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Text)),

            GeminiProtocolConstants.ItemTypes.Reasoning => new ReasoningItem(
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Id),
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Text)),

            GeminiProtocolConstants.ItemTypes.CommandExecution => new CommandExecutionItem(
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Id),
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Command),
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.AggregatedOutput),
                GetOptionalInt32(itemElement, GeminiProtocolConstants.Properties.ExitCode),
                ParseCommandExecutionStatus(GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Status))),

            GeminiProtocolConstants.ItemTypes.FileChange => new FileChangeItem(
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Id),
                ParseFileUpdateChanges(GetRequiredProperty(itemElement, GeminiProtocolConstants.Properties.Changes)),
                ParsePatchApplyStatus(GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Status))),

            GeminiProtocolConstants.ItemTypes.McpToolCall => new McpToolCallItem(
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Id),
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Server),
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Tool),
                ParseOptionalNode(itemElement, GeminiProtocolConstants.Properties.Arguments),
                ParseMcpResult(itemElement),
                ParseMcpError(itemElement),
                ParseMcpToolCallStatus(GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Status))),

            GeminiProtocolConstants.ItemTypes.CollabToolCall => ParseCollabToolCall(itemElement),

            GeminiProtocolConstants.ItemTypes.WebSearch => new WebSearchItem(
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Id),
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Query)),

            GeminiProtocolConstants.ItemTypes.TodoList => new TodoListItem(
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Id),
                ParseTodoItems(GetRequiredProperty(itemElement, GeminiProtocolConstants.Properties.Items))),

            GeminiProtocolConstants.ItemTypes.Error => new ErrorItem(
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Id),
                GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Message)),

            _ => throw new InvalidOperationException($"Unsupported thread item type: {itemType}"),
        };
    }

    private static List<FileUpdateChange> ParseFileUpdateChanges(JsonElement changesElement)
    {
        if (changesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("file_change.changes must be an array");
        }

        var changes = new List<FileUpdateChange>();
        foreach (var change in changesElement.EnumerateArray())
        {
            changes.Add(new FileUpdateChange(
                GetRequiredString(change, GeminiProtocolConstants.Properties.Path),
                ParsePatchChangeKind(GetRequiredString(change, GeminiProtocolConstants.Properties.Kind))));
        }

        return changes;
    }

    private static List<TodoItem> ParseTodoItems(JsonElement itemsElement)
    {
        if (itemsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("todo_list.items must be an array");
        }

        var items = new List<TodoItem>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            items.Add(new TodoItem(
                GetRequiredString(item, GeminiProtocolConstants.Properties.Text),
                GetRequiredBoolean(item, GeminiProtocolConstants.Properties.Completed)));
        }

        return items;
    }

    private static McpToolCallResult? ParseMcpResult(JsonElement element)
    {
        if (!element.TryGetProperty(GeminiProtocolConstants.Properties.Result, out var resultElement)
            || resultElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var content = new List<JsonNode>();
        if (resultElement.TryGetProperty(GeminiProtocolConstants.Properties.Content, out var contentElement)
            && contentElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in contentElement.EnumerateArray())
            {
                var parsed = JsonNode.Parse(entry.GetRawText());
                if (parsed is not null)
                {
                    content.Add(parsed);
                }
            }
        }

        JsonNode? structuredContent = null;
        if (resultElement.TryGetProperty(GeminiProtocolConstants.Properties.StructuredContent, out var structuredContentElement)
            && structuredContentElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            structuredContent = JsonNode.Parse(structuredContentElement.GetRawText());
        }

        return new McpToolCallResult(content, structuredContent);
    }

    private static McpToolCallError? ParseMcpError(JsonElement element)
    {
        if (!element.TryGetProperty(GeminiProtocolConstants.Properties.Error, out var errorElement)
            || errorElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return new McpToolCallError(GetRequiredString(errorElement, GeminiProtocolConstants.Properties.Message));
    }

    private static CollabToolCallItem ParseCollabToolCall(JsonElement itemElement)
    {
        return new CollabToolCallItem(
            GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Id),
            ParseCollabTool(GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Tool)),
            GetRequiredString(itemElement, GeminiProtocolConstants.Properties.SenderThreadId),
            ParseStringArray(
                GetRequiredProperty(itemElement, GeminiProtocolConstants.Properties.ReceiverThreadIds),
                "collab_tool_call.receiver_thread_ids"),
            GetOptionalString(itemElement, GeminiProtocolConstants.Properties.Prompt),
            ParseCollabAgentStates(GetRequiredProperty(itemElement, GeminiProtocolConstants.Properties.AgentsStates)),
            ParseCollabToolCallStatus(GetRequiredString(itemElement, GeminiProtocolConstants.Properties.Status)));
    }

    private static Dictionary<string, CollabAgentState> ParseCollabAgentStates(JsonElement agentsStatesElement)
    {
        if (agentsStatesElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("collab_tool_call.agents_states must be an object");
        }

        var states = new Dictionary<string, CollabAgentState>(StringComparer.Ordinal);
        foreach (var property in agentsStatesElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("collab_tool_call.agents_states values must be objects");
            }

            states[property.Name] = new CollabAgentState(
                ParseCollabAgentStatus(GetRequiredString(property.Value, GeminiProtocolConstants.Properties.Status)),
                GetOptionalString(property.Value, GeminiProtocolConstants.Properties.Message));
        }

        return states;
    }

    private static List<string> ParseStringArray(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{context} must be an array");
        }

        var items = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"{context} entries must be strings");
            }

            items.Add(item.GetString() ?? string.Empty);
        }

        return items;
    }

    private static JsonNode? ParseOptionalNode(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var valueElement)
            || valueElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonNode.Parse(valueElement.GetRawText());
    }

    private static PatchChangeKind ParsePatchChangeKind(string kind)
    {
        return kind switch
        {
            GeminiProtocolConstants.PatchKinds.Add => PatchChangeKind.Add,
            GeminiProtocolConstants.PatchKinds.Delete => PatchChangeKind.Delete,
            GeminiProtocolConstants.PatchKinds.Update => PatchChangeKind.Update,
            _ => throw new InvalidOperationException($"Unsupported patch change kind: {kind}"),
        };
    }

    private static PatchApplyStatus ParsePatchApplyStatus(string status)
    {
        return status switch
        {
            GeminiProtocolConstants.Statuses.Completed => PatchApplyStatus.Completed,
            GeminiProtocolConstants.Statuses.Failed => PatchApplyStatus.Failed,
            _ => throw new InvalidOperationException($"Unsupported patch apply status: {status}"),
        };
    }

    private static ToolResultStatus ParseToolResultStatus(string status)
    {
        return status switch
        {
            GeminiProtocolConstants.EventStatuses.Success => ToolResultStatus.Success,
            GeminiProtocolConstants.EventStatuses.Error => ToolResultStatus.Error,
            _ => throw new InvalidOperationException($"Unsupported tool result status: {status}"),
        };
    }

    private static McpToolCallStatus ParseMcpToolCallStatus(string status)
    {
        return status switch
        {
            GeminiProtocolConstants.Statuses.InProgress => McpToolCallStatus.InProgress,
            GeminiProtocolConstants.Statuses.Completed => McpToolCallStatus.Completed,
            GeminiProtocolConstants.Statuses.Failed => McpToolCallStatus.Failed,
            _ => throw new InvalidOperationException($"Unsupported MCP tool call status: {status}"),
        };
    }

    private static CollabToolCallStatus ParseCollabToolCallStatus(string status)
    {
        return status switch
        {
            GeminiProtocolConstants.Statuses.InProgress => CollabToolCallStatus.InProgress,
            GeminiProtocolConstants.Statuses.Completed => CollabToolCallStatus.Completed,
            GeminiProtocolConstants.Statuses.Failed => CollabToolCallStatus.Failed,
            _ => throw new InvalidOperationException($"Unsupported collab tool call status: {status}"),
        };
    }

    private static CollabTool ParseCollabTool(string tool)
    {
        return tool switch
        {
            GeminiProtocolConstants.CollabTools.SpawnAgent => CollabTool.SpawnAgent,
            GeminiProtocolConstants.CollabTools.SendInput => CollabTool.SendInput,
            GeminiProtocolConstants.CollabTools.Wait => CollabTool.Wait,
            GeminiProtocolConstants.CollabTools.CloseAgent => CollabTool.CloseAgent,
            _ => throw new InvalidOperationException($"Unsupported collab tool: {tool}"),
        };
    }

    private static CollabAgentStatus ParseCollabAgentStatus(string status)
    {
        return status switch
        {
            GeminiProtocolConstants.CollabAgentStatuses.PendingInit => CollabAgentStatus.PendingInit,
            GeminiProtocolConstants.CollabAgentStatuses.Running => CollabAgentStatus.Running,
            GeminiProtocolConstants.CollabAgentStatuses.Completed => CollabAgentStatus.Completed,
            GeminiProtocolConstants.CollabAgentStatuses.Errored => CollabAgentStatus.Errored,
            GeminiProtocolConstants.CollabAgentStatuses.Shutdown => CollabAgentStatus.Shutdown,
            GeminiProtocolConstants.CollabAgentStatuses.NotFound => CollabAgentStatus.NotFound,
            _ => throw new InvalidOperationException($"Unsupported collab agent status: {status}"),
        };
    }

    private static CommandExecutionStatus ParseCommandExecutionStatus(string status)
    {
        return status switch
        {
            GeminiProtocolConstants.Statuses.InProgress => CommandExecutionStatus.InProgress,
            GeminiProtocolConstants.Statuses.Completed => CommandExecutionStatus.Completed,
            GeminiProtocolConstants.Statuses.Failed => CommandExecutionStatus.Failed,
            _ => throw new InvalidOperationException($"Unsupported command execution status: {status}"),
        };
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var propertyValue))
        {
            throw new InvalidOperationException($"Missing required property '{property}'");
        }

        return propertyValue;
    }

    private static string GetRequiredString(JsonElement element, string property)
    {
        var value = GetRequiredProperty(element, property);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Property '{property}' must be a string");
        }

        return value.GetString() ?? string.Empty;
    }

    private static string? GetOptionalString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Property '{property}' must be a string");
        }

        return value.GetString();
    }

    private static bool? GetOptionalBoolean(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException($"Property '{property}' must be a boolean"),
        };
    }

    private static int GetRequiredInt32(JsonElement element, string property)
    {
        var value = GetRequiredProperty(element, property);
        if (!value.TryGetInt32(out var intValue))
        {
            throw new InvalidOperationException($"Property '{property}' must be an integer");
        }

        return intValue;
    }

    private static int? GetOptionalInt32(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (!value.TryGetInt32(out var intValue))
        {
            throw new InvalidOperationException($"Property '{property}' must be an integer");
        }

        return intValue;
    }

    private static bool GetRequiredBoolean(JsonElement element, string property)
    {
        var value = GetRequiredProperty(element, property);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException($"Property '{property}' must be a boolean"),
        };
    }
}
