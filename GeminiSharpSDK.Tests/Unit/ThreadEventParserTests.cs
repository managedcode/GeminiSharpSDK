using ManagedCode.GeminiSharpSDK.Internal;
using ManagedCode.GeminiSharpSDK.Models;

namespace ManagedCode.GeminiSharpSDK.Tests.Unit;

public class ThreadEventParserTests
{
    [Test]
    public async Task Parse_RecognizesCurrentStreamJsonEventKinds()
    {
        var events = new[]
        {
            "{\"type\":\"init\",\"session_id\":\"thread_1\",\"model\":\"auto-gemini-3\"}",
            "{\"type\":\"message\",\"role\":\"user\",\"content\":\"hello\"}",
            "{\"type\":\"message\",\"role\":\"assistant\",\"content\":\"ok\",\"delta\":true}",
            "{\"type\":\"tool_use\",\"tool_name\":\"read_file\",\"tool_id\":\"tool_1\",\"parameters\":{\"path\":\"README.md\"}}",
            "{\"type\":\"tool_result\",\"tool_id\":\"tool_1\",\"status\":\"success\",\"output\":\"done\"}",
            "{\"type\":\"error\",\"severity\":\"warning\",\"message\":\"watch out\"}",
            "{\"type\":\"result\",\"status\":\"success\",\"stats\":{\"input_tokens\":1,\"cached\":0,\"output_tokens\":2}}",
        };

        var parsed = events.Select(ThreadEventParser.Parse).ToList();

        await Assert.That(parsed).Count().IsEqualTo(7);
        await Assert.That(parsed[0]).IsTypeOf<InitEvent>();
        await Assert.That(parsed[1]).IsTypeOf<MessageEvent>();
        await Assert.That(parsed[2]).IsTypeOf<MessageEvent>();
        await Assert.That(parsed[3]).IsTypeOf<ToolUseEvent>();
        await Assert.That(parsed[4]).IsTypeOf<ToolResultEvent>();
        await Assert.That(parsed[5]).IsTypeOf<ThreadErrorEvent>();
        await Assert.That(parsed[6]).IsTypeOf<ResultEvent>();
    }

    [Test]
    public async Task Parse_ParsesResultUsageFromCurrentStatsShape()
    {
        var parsed = (ResultEvent)ThreadEventParser.Parse(
            "{\"type\":\"result\",\"status\":\"success\",\"stats\":{\"input_tokens\":10,\"cached\":3,\"output_tokens\":5}}");

        await Assert.That(parsed.Status).IsEqualTo("success");
        await Assert.That(parsed.Usage).IsNotNull();
        await Assert.That(parsed.Usage!.InputTokens).IsEqualTo(10);
        await Assert.That(parsed.Usage.CachedInputTokens).IsEqualTo(3);
        await Assert.That(parsed.Usage.OutputTokens).IsEqualTo(5);
    }

    [Test]
    public async Task Parse_ParsesResultUsageFromCachedInputTokensStatsShape()
    {
        var parsed = (ResultEvent)ThreadEventParser.Parse(
            "{\"type\":\"result\",\"status\":\"success\",\"stats\":{\"input_tokens\":10,\"cached_input_tokens\":7,\"output_tokens\":5}}");

        await Assert.That(parsed.Usage).IsNotNull();
        await Assert.That(parsed.Usage!.CachedInputTokens).IsEqualTo(7);
    }

    [Test]
    public async Task Parse_ParsesTurnCompletedUsageFromLegacyCachedShape()
    {
        var parsed = (TurnCompletedEvent)ThreadEventParser.Parse(
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":10,\"cached\":2,\"output_tokens\":5}}");

        await Assert.That(parsed.Usage.CachedInputTokens).IsEqualTo(2);
    }

    [Test]
    public async Task Parse_ParsesToolResultError()
    {
        var parsed = (ToolResultEvent)ThreadEventParser.Parse(
            "{\"type\":\"tool_result\",\"tool_id\":\"tool_1\",\"status\":\"error\",\"error\":{\"message\":\"tool failed\"}}");

        await Assert.That(parsed.Status).IsEqualTo(ToolResultStatus.Error);
        await Assert.That(parsed.Error).IsNotNull();
        await Assert.That(parsed.Error!.Message).IsEqualTo("tool failed");
    }

    [Test]
    public async Task Parse_PreservesLegacyThreadStartedSupport()
    {
        var parsed = ThreadEventParser.Parse("{\"type\":\"thread.started\",\"thread_id\":\"thread_1\"}");

        await Assert.That(parsed).IsTypeOf<ThreadStartedEvent>();
    }

    [Test]
    public async Task Parse_ParsesFileChangeItemStartedWithInProgressStatus()
    {
        var parsed = (ItemStartedEvent)ThreadEventParser.Parse(
            "{\"type\":\"item.started\",\"item\":{\"id\":\"item_1\",\"type\":\"file_change\",\"changes\":[{\"path\":\"C:/git/GeminiSandbox/apple.txt\",\"kind\":\"add\"}],\"status\":\"in_progress\"}}");

        await Assert.That(parsed.Item).IsTypeOf<FileChangeItem>();

        var fileChange = (FileChangeItem)parsed.Item;
        await Assert.That(fileChange.Status).IsEqualTo(PatchApplyStatus.InProgress);
        await Assert.That(fileChange.Changes).Count().IsEqualTo(1);
        await Assert.That(fileChange.Changes[0].Path).IsEqualTo("C:/git/GeminiSandbox/apple.txt");
        await Assert.That(fileChange.Changes[0].Kind).IsEqualTo(PatchChangeKind.Add);
    }

    [Test]
    public async Task PatchApplyStatus_PublicNumericValues_RemainStable()
    {
        var completed = (int)PatchApplyStatus.Completed;
        var failed = (int)PatchApplyStatus.Failed;
        var inProgress = (int)PatchApplyStatus.InProgress;

        await Assert.That(completed).IsEqualTo(0);
        await Assert.That(failed).IsEqualTo(1);
        await Assert.That(inProgress).IsEqualTo(2);
    }

    [Test]
    public async Task Parse_ThrowsForUnsupportedEventType()
    {
        var action = () => ThreadEventParser.Parse("{\"type\":\"unknown\"}");

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception!.Message).Contains("Unsupported thread event type");
    }

    [Test]
    public async Task Parse_ThrowsForUnsupportedToolResultStatus()
    {
        var action = () => ThreadEventParser.Parse(
            "{\"type\":\"tool_result\",\"tool_id\":\"tool_1\",\"status\":\"unknown\"}");

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception!.Message).Contains("Unsupported tool result status");
    }
}
