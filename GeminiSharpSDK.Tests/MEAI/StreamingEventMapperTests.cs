using ManagedCode.GeminiSharpSDK.Extensions.AI.Internal;
using ManagedCode.GeminiSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.GeminiSharpSDK.Extensions.AI.Tests;

public class StreamingEventMapperTests
{
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
    public async Task ToUpdates_ResultError_ThrowsException()
    {
        var events = ToAsyncEnumerable(new ResultEvent("error", null, new ThreadError("something broke")));

        await Assert.That(async () => await CollectUpdates(events))
            .ThrowsExactly<InvalidOperationException>();
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
        await Assert.That(updates.Count).IsGreaterThanOrEqualTo(4);
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

    private static async Task<List<ChatResponseUpdate>> CollectUpdates(IAsyncEnumerable<ThreadEvent> events)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in StreamingEventMapper.ToUpdates(events))
        {
            updates.Add(update);
        }

        return updates;
    }
}
