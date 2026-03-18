using System.Runtime.CompilerServices;
using ManagedCode.GeminiSharpSDK.Extensions.AI.Content;
using ManagedCode.GeminiSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.GeminiSharpSDK.Extensions.AI.Internal;

internal static class StreamingEventMapper
{
    private const string AssistantRole = "assistant";
    private const string UserRole = "user";
    private const string SuccessStatus = "success";

    internal static async IAsyncEnumerable<ChatResponseUpdate> ToUpdates(
        IAsyncEnumerable<ThreadEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case InitEvent init:
                    yield return new ChatResponseUpdate { ConversationId = init.SessionId };
                    break;

                case ThreadStartedEvent started:
                    yield return new ChatResponseUpdate { ConversationId = started.ThreadId };
                    break;

                case MessageEvent { Role: AssistantRole } message:
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(message.Content)],
                    };
                    break;

                case MessageEvent { Role: UserRole } message:
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.User,
                        Contents = [new TextContent(message.Content)],
                    };
                    break;

                case ItemCompletedEvent { Item: AgentMessageItem msg }:
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(msg.Text)],
                    };
                    break;

                case ItemCompletedEvent { Item: ReasoningItem r }:
                    yield return new ChatResponseUpdate
                    {
                        Contents = [new TextReasoningContent(r.Text)],
                    };
                    break;

                case ItemCompletedEvent { Item: CommandExecutionItem c }:
                    yield return new ChatResponseUpdate
                    {
                        Contents =
                        [
                            new CommandExecutionContent
                            {
                                Command = c.Command,
                                AggregatedOutput = c.AggregatedOutput,
                                ExitCode = c.ExitCode,
                                Status = c.Status,
                            },
                        ],
                    };
                    break;

                case ItemCompletedEvent { Item: FileChangeItem f }:
                    yield return new ChatResponseUpdate
                    {
                        Contents =
                        [
                            new FileChangeContent
                            {
                                Changes = f.Changes,
                                Status = f.Status,
                            },
                        ],
                    };
                    break;

                case ItemCompletedEvent { Item: McpToolCallItem m }:
                    yield return new ChatResponseUpdate
                    {
                        Contents =
                        [
                            new McpToolCallContent
                            {
                                Server = m.Server,
                                Tool = m.Tool,
                                Arguments = m.Arguments,
                                Result = m.Result,
                                Error = m.Error,
                                Status = m.Status,
                            },
                        ],
                    };
                    break;

                case ItemCompletedEvent { Item: WebSearchItem w }:
                    yield return new ChatResponseUpdate
                    {
                        Contents =
                        [
                            new WebSearchContent
                            {
                                Query = w.Query,
                            },
                        ],
                    };
                    break;

                case ItemCompletedEvent { Item: CollabToolCallItem col }:
                    yield return new ChatResponseUpdate
                    {
                        Contents =
                        [
                            new CollabToolCallContent
                            {
                                Tool = col.Tool,
                                SenderThreadId = col.SenderThreadId,
                                ReceiverThreadIds = col.ReceiverThreadIds,
                                AgentsStates = col.AgentsStates,
                                Status = col.Status,
                            },
                        ],
                    };
                    break;

                case ItemUpdatedEvent { Item: AgentMessageItem msg }:
                    yield return new ChatResponseUpdate
                    {
                        Contents = [new TextContent(msg.Text)],
                    };
                    break;

                case TurnCompletedEvent tc:
                    yield return new ChatResponseUpdate
                    {
                        FinishReason = ChatFinishReason.Stop,
                        Contents =
                        [
                            new UsageContent(new UsageDetails
                            {
                                InputTokenCount = tc.Usage.InputTokens,
                                OutputTokenCount = tc.Usage.OutputTokens,
                                TotalTokenCount = tc.Usage.InputTokens + tc.Usage.OutputTokens,
                            }),
                        ],
                    };
                    break;

                case ResultEvent { Status: SuccessStatus } resultEvent:
                    yield return new ChatResponseUpdate
                    {
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
                                }),
                            ],
                    };
                    break;

                case TurnFailedEvent tf:
                    throw new InvalidOperationException(tf.Error.Message);

                case ResultEvent { Error: not null } resultEvent:
                    throw new InvalidOperationException(resultEvent.Error.Message);

                case ThreadErrorEvent te:
                    throw new InvalidOperationException(te.Message);
            }
        }
    }
}
