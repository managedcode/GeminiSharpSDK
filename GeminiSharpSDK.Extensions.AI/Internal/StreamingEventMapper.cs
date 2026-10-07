using System.Runtime.CompilerServices;
using System.Text;
using ManagedCode.GeminiSharpSDK.Extensions.AI.Content;
using ManagedCode.GeminiSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.GeminiSharpSDK.Extensions.AI.Internal;

internal static class StreamingEventMapper
{
    private const string AssistantRole = "assistant";
    private const string SuccessStatus = "success";
    private const string NativeActivityProperty = "managedcode:activity";
    private const string NativeActivityPhaseProperty = "managedcode:activity_phase";
    private const string NativeActivityStarted = "started";
    private const string NativeActivityUpdated = "updated";
    private const string NativeActivityCompleted = "completed";
    private const string NativeToolActivity = "native_tool";
    private const string CommandExecutionActivity = "command_execution";
    private const string FileChangeActivity = "file_change";
    private const string McpToolActivity = "mcp_tool";
    private const string WebSearchActivity = "web_search";
    private const string CollaborationActivity = "collaboration";

    internal static async IAsyncEnumerable<ChatResponseUpdate> ToUpdates(
        IAsyncEnumerable<ThreadEvent> events,
        int maximumReconciliationCharacters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumReconciliationCharacters);
        var assistantDeltas = new StringBuilder();
        var assistantItemSnapshots = new Dictionary<string, string>(StringComparer.Ordinal);
        string? conversationId = null;
        string? failureMessage = null;
        await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case InitEvent init:
                    conversationId = init.SessionId;
                    yield return new ChatResponseUpdate { ConversationId = conversationId };
                    break;

                case ThreadStartedEvent started:
                    conversationId = started.ThreadId;
                    yield return new ChatResponseUpdate { ConversationId = conversationId };
                    break;

                case MessageEvent { Role: AssistantRole } message:
                {
                    // Pinned Gemini CLI only emits assistant delta=true content events. Delta=false
                    // has no cumulative-snapshot contract in the native stream and is treated as one
                    // complete message segment rather than inferred as a whole-turn snapshot.
                    if (message.Delta)
                    {
                        if (message.Content.Length > maximumReconciliationCharacters - assistantDeltas.Length)
                        {
                            throw new InvalidDataException("Gemini streamed assistant text exceeded the configured process output limit.");
                        }
                        assistantDeltas.Append(message.Content);
                    }
                    if (message.Content.Length > 0)
                    {
                        yield return new ChatResponseUpdate
                        {
                            ConversationId = conversationId,
                            Role = ChatRole.Assistant,
                            Contents = [new TextContent(message.Content)],
                        };
                    }
                    break;
                }

                case MessageEvent:
                    // Gemini emits the submitted prompt as role=user in stream-json. It is input echo,
                    // not an assistant response chunk, so it is intentionally not surfaced here.
                    break;

                case ToolUseEvent:
                    yield return CreateNativeActivityUpdate(NativeToolActivity, NativeActivityStarted, conversationId, ChatRole.Assistant);
                    break;

                case ToolResultEvent:
                    yield return CreateNativeActivityUpdate(NativeToolActivity, NativeActivityCompleted, conversationId, ChatRole.Tool);
                    break;

                case ItemCompletedEvent { Item: ReasoningItem r }:
                    yield return new ChatResponseUpdate
                    {
                        ConversationId = conversationId,
                        Contents = [new TextReasoningContent(r.Text)],
                    };
                    break;

                case ItemUpdatedEvent { Item: AgentMessageItem message }:
                {
                    var update = CreateAssistantItemUpdate(message, assistantDeltas, assistantItemSnapshots, conversationId);
                    if (update is not null)
                    {
                        yield return update;
                    }
                    break;
                }

                case ItemCompletedEvent { Item: AgentMessageItem message }:
                {
                    var update = CreateAssistantItemUpdate(message, assistantDeltas, assistantItemSnapshots, conversationId);
                    if (update is not null)
                    {
                        yield return update;
                    }
                    break;
                }

                case ItemStartedEvent { Item: CommandExecutionItem }:
                    yield return CreateNativeActivityUpdate(CommandExecutionActivity, NativeActivityStarted, conversationId);
                    break;

                case ItemUpdatedEvent { Item: CommandExecutionItem }:
                    yield return CreateNativeActivityUpdate(CommandExecutionActivity, NativeActivityUpdated, conversationId);
                    break;

                case ItemCompletedEvent { Item: CommandExecutionItem command }:
                    yield return CreateNativeActivityUpdate(CommandExecutionActivity, NativeActivityCompleted, conversationId,
                        content: new CommandExecutionContent
                        {
                            Command = command.Command,
                            AggregatedOutput = command.AggregatedOutput,
                            ExitCode = command.ExitCode,
                            Status = command.Status,
                        });
                    break;

                case ItemCompletedEvent { Item: FileChangeItem fileChange }:
                    yield return CreateNativeActivityUpdate(FileChangeActivity, NativeActivityCompleted, conversationId,
                        content: new FileChangeContent { Changes = fileChange.Changes, Status = fileChange.Status });
                    break;

                case ItemCompletedEvent { Item: McpToolCallItem mcp }:
                    yield return CreateNativeActivityUpdate(McpToolActivity, NativeActivityCompleted, conversationId,
                        content: new McpToolCallContent
                        {
                            Server = mcp.Server,
                            Tool = mcp.Tool,
                            Arguments = mcp.Arguments,
                            Result = mcp.Result,
                            Error = mcp.Error,
                            Status = mcp.Status,
                        });
                    break;

                case ItemCompletedEvent { Item: WebSearchItem search }:
                    yield return CreateNativeActivityUpdate(WebSearchActivity, NativeActivityCompleted, conversationId,
                        content: new WebSearchContent { Query = search.Query });
                    break;

                case ItemCompletedEvent { Item: CollabToolCallItem collaboration }:
                    yield return CreateNativeActivityUpdate(CollaborationActivity, NativeActivityCompleted, conversationId,
                        content: new CollabToolCallContent
                        {
                            Tool = collaboration.Tool,
                            SenderThreadId = collaboration.SenderThreadId,
                            ReceiverThreadIds = collaboration.ReceiverThreadIds,
                            AgentsStates = collaboration.AgentsStates,
                            Status = collaboration.Status,
                        });
                    break;

                case ItemStartedEvent { Item: McpToolCallItem }:
                    yield return CreateNativeActivityUpdate(McpToolActivity, NativeActivityStarted, conversationId);
                    break;

                case ItemUpdatedEvent { Item: McpToolCallItem }:
                    yield return CreateNativeActivityUpdate(McpToolActivity, NativeActivityUpdated, conversationId);
                    break;

                case ItemStartedEvent { Item: CollabToolCallItem }:
                    yield return CreateNativeActivityUpdate(CollaborationActivity, NativeActivityStarted, conversationId);
                    break;

                case ItemUpdatedEvent { Item: CollabToolCallItem }:
                    yield return CreateNativeActivityUpdate(CollaborationActivity, NativeActivityUpdated, conversationId);
                    break;

                case TurnCompletedEvent tc:
                    yield return new ChatResponseUpdate
                    {
                        ConversationId = conversationId,
                        FinishReason = ChatFinishReason.Stop,
                        Contents =
                        [
                            new UsageContent(new UsageDetails
                            {
                                InputTokenCount = tc.Usage.InputTokens,
                                OutputTokenCount = tc.Usage.OutputTokens,
                                TotalTokenCount = tc.Usage.InputTokens + tc.Usage.OutputTokens,
                                CachedInputTokenCount = tc.Usage.CachedInputTokens > 0 ? tc.Usage.CachedInputTokens : null,
                            }),
                        ],
                    };
                    break;

                case ResultEvent { Status: SuccessStatus } resultEvent:
                    yield return new ChatResponseUpdate
                    {
                        ConversationId = conversationId,
                        FinishReason = ChatFinishReason.Stop,
                        Contents = resultEvent.Usage is null
                            ? []
                            :
                            [
                                new UsageContent(new UsageDetails
                                {
                                    InputTokenCount = resultEvent.Usage.InputTokens,
                                    OutputTokenCount = resultEvent.Usage.OutputTokens,
                                    TotalTokenCount = resultEvent.Usage.InputTokens + resultEvent.Usage.OutputTokens,
                                    CachedInputTokenCount = resultEvent.Usage.CachedInputTokens > 0 ? resultEvent.Usage.CachedInputTokens : null,
                                }),
                            ],
                    };
                    break;

                case TurnFailedEvent tf:
                    failureMessage ??= tf.Error.Message;
                    break;

                case ResultEvent { Error: not null } resultEvent:
                    failureMessage ??= resultEvent.Error.Message;
                    break;

                case ThreadErrorEvent te:
                    failureMessage ??= te.Message;
                    break;
            }

            if (failureMessage is not null)
            {
                break;
            }
        }

        if (failureMessage is not null)
        {
            throw CliExecutionFailureException.FromProviderFailure(failureMessage);
        }
    }

    private static ChatResponseUpdate? CreateAssistantItemUpdate(
        AgentMessageItem message,
        StringBuilder streamedDeltas,
        Dictionary<string, string> itemSnapshots,
        string? conversationId)
    {
        string text;
        if (itemSnapshots.TryGetValue(message.Id, out var previous))
        {
            if (!message.Text.StartsWith(previous, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Gemini emitted an assistant item snapshot that conflicts with the prior snapshot for the same item.");
            }

            text = message.Text[previous.Length..];
        }
        else
        {
            var pendingDeltas = streamedDeltas.ToString();
            text = pendingDeltas.Length > 0 && message.Text.StartsWith(pendingDeltas, StringComparison.Ordinal)
                ? message.Text[pendingDeltas.Length..]
                : message.Text;
        }

        itemSnapshots[message.Id] = message.Text;
        streamedDeltas.Clear();
        return text.Length == 0
            ? null
            : new ChatResponseUpdate
            {
                ConversationId = conversationId,
                Role = ChatRole.Assistant,
                Contents = [new TextContent(text)],
            };
    }

    private static ChatResponseUpdate CreateNativeActivityUpdate(string category, string phase, string? conversationId, ChatRole? role = null, AIContent? content = null) => new()
    {
        ConversationId = conversationId,
        Role = role,
        Contents = content is null ? [] : [content],
        AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [NativeActivityProperty] = category,
            [NativeActivityPhaseProperty] = phase,
        },
    };
}
