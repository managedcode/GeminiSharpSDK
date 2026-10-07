using System.Text.Json.Nodes;
using ManagedCode.GeminiSharpSDK.Extensions.AI.Content;
using ManagedCode.GeminiSharpSDK.Extensions.AI.Internal;
using ManagedCode.GeminiSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.GeminiSharpSDK.Extensions.AI.Tests;

public class StreamingEventMapperTests
{
    private const int MaximumReconciliationCharacters = 1024;
    private const string NativeActivityProperty = "managedcode:activity";
    private const string NativeActivityPhaseProperty = "managedcode:activity_phase";
    private const string NativeToolActivity = "native_tool";
    private const string ActivityStarted = "started";
    private const string ActivityCompleted = "completed";

    [Test]
    public async Task ToUpdates_Init_YieldsConversationId()
    {
        var events = ToAsyncEnumerable(new InitEvent("thread-1", "auto-gemini-3"));
        var updates = await CollectUpdates(events);
        await Assert.That(updates[0].ConversationId).IsEqualTo("thread-1");
    }

    [Test]
    public async Task ToUpdates_AssistantMessage_YieldsTextContent()
    {
        var events = ToAsyncEnumerable(new MessageEvent("assistant", "Hello", true));
        var updates = await CollectUpdates(events);
        await Assert.That(updates[0].Text).IsEqualTo("Hello");
        await Assert.That(updates[0].Role).IsEqualTo(ChatRole.Assistant);
    }

    [Test]
    public async Task ToUpdates_Result_YieldsFinishReason()
    {
        var events = ToAsyncEnumerable(new ResultEvent("success", new Usage(10, 0, 5), null));
        var updates = await CollectUpdates(events);
        await Assert.That(updates[0].FinishReason).IsEqualTo(ChatFinishReason.Stop);
    }

    [Test]
    public async Task ToUpdates_ResultError_DisposesSourceBeforeThrowingConfirmedFailure()
    {
        var sourceDisposed = false;

        var exception = await Assert.That(async () => await CollectUpdates(ProviderFailureEvents(() => sourceDisposed = true)))
            .ThrowsException();

        await Assert.That(exception).IsTypeOf<CliExecutionFailureException>();
        await Assert.That(((CliExecutionFailureException)exception!).RootProcessExitConfirmed).IsTrue();
        await Assert.That(((CliExecutionFailureException)exception!).ExitCode).IsNull();
        await Assert.That(sourceDisposed).IsTrue();
    }

    private static async IAsyncEnumerable<ThreadEvent> ProviderFailureEvents(Action onDispose)
    {
        try
        {
            yield return new ResultEvent("error", null, new ThreadError("something broke"));
            await Task.Yield();
        }
        finally
        {
            onDispose();
        }
    }

    [Test]
    public async Task ToUpdates_FullSequence_MapsCurrentEvents()
    {
        var events = ToAsyncEnumerable(
            new InitEvent("t1", "auto-gemini-3"),
            new MessageEvent("user", "question", false),
            new MessageEvent("assistant", "answer", true),
            new ResultEvent("success", new Usage(10, 0, 5), null));

        var updates = await CollectUpdates(events);
        await Assert.That(updates.Count).IsEqualTo(3);
        await Assert.That(updates.Any(static update => update.Role == ChatRole.User)).IsFalse();
    }

    [Test]
    public async Task ToUpdates_DeltaFalseMessages_AreNotAssumedToBeCumulativeSnapshots()
    {
        var updates = await CollectUpdates(ToAsyncEnumerable(
            new MessageEvent("assistant", "Hello", true),
            new MessageEvent("assistant", "Hello world", false),
            new MessageEvent("assistant", "Hello world", false)));

        await Assert.That(updates.Count).IsEqualTo(3);
        await Assert.That(updates[0].Text).IsEqualTo("Hello");
        await Assert.That(updates[1].Text).IsEqualTo("Hello world");
        await Assert.That(updates[2].Text).IsEqualTo("Hello world");
    }

    [Test]
    public async Task ToUpdates_DeltaFalseMessage_EmitsAsCompleteSegment()
    {
        var updates = await CollectUpdates(ToAsyncEnumerable(
            new MessageEvent("assistant", "complete response", false)));

        await Assert.That(updates.Count).IsEqualTo(1);
        await Assert.That(updates[0].Text).IsEqualTo("complete response");
    }

    [Test]
    public async Task ToUpdates_ConflictingSameIdentityAgentSnapshot_FailsClosed()
    {
        await Assert.That(async () => await CollectUpdates(ToAsyncEnumerable(
                new ItemCompletedEvent(new AgentMessageItem("message-1", "first")),
                new ItemCompletedEvent(new AgentMessageItem("message-1", "replacement")))))
            .ThrowsExactly<InvalidDataException>();
    }

    [Test]
    public async Task ToUpdates_DeltaTextAndMultipleAssistantItemIds_AvoidDuplicateAndCrossItemConflict()
    {
        var updates = await CollectUpdates(ToAsyncEnumerable(
            new MessageEvent("assistant", "first", true),
            new ItemCompletedEvent(new AgentMessageItem("message-1", "first")),
            new ItemUpdatedEvent(new AgentMessageItem("message-2", "second")),
            new ItemCompletedEvent(new AgentMessageItem("message-2", "second"))));

        await Assert.That(updates.Count).IsEqualTo(2);
        await Assert.That(updates[0].Text).IsEqualTo("first");
        await Assert.That(updates[1].Text).IsEqualTo("second");
    }

    [Test]
    public async Task ToUpdates_AssistantDeltaBuffer_EnforcesConfiguredBound()
    {
        await Assert.That(async () => await CollectUpdates(
                ToAsyncEnumerable(new MessageEvent("assistant", "four", true)),
                maximumReconciliationCharacters: 3))
            .ThrowsExactly<InvalidDataException>();
    }

    [Test]
    public async Task ToUpdates_CompletedNativeItems_PreserveTypedContentAndAddSafeMetadata()
    {
        var events = ToAsyncEnumerable(
            new ItemCompletedEvent(new CommandExecutionItem("c", "private command", "private output", 0, CommandExecutionStatus.Completed)),
            new ItemCompletedEvent(new FileChangeItem("f", [new FileUpdateChange("private-path", PatchChangeKind.Update)], PatchApplyStatus.Completed)),
            new ItemCompletedEvent(new McpToolCallItem("m", "private-server", "private-tool", new JsonObject { ["secret"] = "value" }, null, null, McpToolCallStatus.Completed)),
            new ItemCompletedEvent(new WebSearchItem("w", "private search")),
            new ItemCompletedEvent(new CollabToolCallItem("c2", CollabTool.SpawnAgent, "sender", ["receiver"], "private prompt",
                new Dictionary<string, CollabAgentState> { ["receiver"] = new(CollabAgentStatus.Running, "private state") }, CollabToolCallStatus.Completed)));

        var updates = await CollectUpdates(events);
        await Assert.That(updates[0].Contents.OfType<CommandExecutionContent>().Single().Command).IsEqualTo("private command");
        await Assert.That(updates[1].Contents.OfType<FileChangeContent>().Single().Changes[0].Path).IsEqualTo("private-path");
        await Assert.That(updates[2].Contents.OfType<McpToolCallContent>().Single().Tool).IsEqualTo("private-tool");
        await Assert.That(updates[3].Contents.OfType<WebSearchContent>().Single().Query).IsEqualTo("private search");
        await Assert.That(updates[4].Contents.OfType<CollabToolCallContent>().Single().Tool).IsEqualTo(CollabTool.SpawnAgent);
        foreach (var update in updates)
        {
            await Assert.That(update.AdditionalProperties![NativeActivityProperty]).IsNotNull();
            await Assert.That(update.AdditionalProperties![NativeActivityPhaseProperty]).IsEqualTo(ActivityCompleted);
        }
    }

    [Test]
    public async Task ToUpdates_NativeToolUseAndResult_ExposeOnlySafeActivityMetadata()
    {
        var updates = await CollectUpdates(ToAsyncEnumerable(
            new ToolUseEvent("sensitive-tool-name", "tool-id", new JsonObject { ["argument"] = "private" }),
            new ToolResultEvent("tool-id", ToolResultStatus.Success, "private output", null)));

        await Assert.That(updates.Count).IsEqualTo(2);
        await Assert.That(updates[0].Contents.Count).IsEqualTo(0);
        await Assert.That(updates[0].AdditionalProperties!.Count).IsEqualTo(2);
        await Assert.That(updates[0].AdditionalProperties![NativeActivityProperty]).IsEqualTo(NativeToolActivity);
        await Assert.That(updates[0].AdditionalProperties![NativeActivityPhaseProperty]).IsEqualTo(ActivityStarted);
        await Assert.That(updates[1].Contents.Count).IsEqualTo(0);
        await Assert.That(updates[1].AdditionalProperties!.Count).IsEqualTo(2);
        await Assert.That(updates[1].AdditionalProperties![NativeActivityProperty]).IsEqualTo(NativeToolActivity);
        await Assert.That(updates[1].AdditionalProperties![NativeActivityPhaseProperty]).IsEqualTo(ActivityCompleted);
    }

    [Test]
    public async Task ToUpdates_ResultUsage_MapsCachedInputTokens()
    {
        var events = ToAsyncEnumerable(new ResultEvent("success", new Usage(10, 4, 5), null));

        var updates = await CollectUpdates(events);
        var usageContent = updates[0].Contents.OfType<UsageContent>().Single();

        await Assert.That(usageContent.Details.CachedInputTokenCount).IsEqualTo(4);
    }

    [Test]
    public async Task ToUpdates_TurnCompletedUsage_MapsCachedInputTokens()
    {
        var events = ToAsyncEnumerable(new TurnCompletedEvent(new Usage(12, 6, 3)));

        var updates = await CollectUpdates(events);
        var usageContent = updates[0].Contents.OfType<UsageContent>().Single();

        await Assert.That(usageContent.Details.CachedInputTokenCount).IsEqualTo(6);
    }

    private static async IAsyncEnumerable<ThreadEvent> ToAsyncEnumerable(params ThreadEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
            await Task.CompletedTask;
        }
    }

    private static async Task<List<ChatResponseUpdate>> CollectUpdates(
        IAsyncEnumerable<ThreadEvent> events,
        int maximumReconciliationCharacters = MaximumReconciliationCharacters)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in StreamingEventMapper.ToUpdates(events, maximumReconciliationCharacters))
        {
            updates.Add(update);
        }

        return updates;
    }
}
