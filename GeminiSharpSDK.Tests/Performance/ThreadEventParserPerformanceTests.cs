using System.Diagnostics;
using ManagedCode.GeminiSharpSDK.Internal;
using ManagedCode.GeminiSharpSDK.Models;

namespace ManagedCode.GeminiSharpSDK.Tests.Performance;

public class ThreadEventParserPerformanceTests
{
    private static readonly string[] SupportedEventStream =
    [
        """{"type":"init","session_id":"thread_1","model":"auto-gemini-3"}""",
        """{"type":"message","role":"user","content":"hello"}""",
        """{"type":"message","role":"assistant","content":"ok","delta":true}""",
        """{"type":"tool_use","tool_name":"read_file","tool_id":"tool_1","parameters":{"path":"README.md"}}""",
        """{"type":"tool_result","tool_id":"tool_1","status":"success","output":"done"}""",
        """{"type":"error","severity":"warning","message":"watch out"}""",
        """{"type":"result","status":"success","stats":{"input_tokens":10,"cached":3,"output_tokens":5}}""",
        """{"type":"thread.started","thread_id":"legacy_thread"}""",
        """{"type":"turn.failed","error":{"message":"legacy failed"}}""",
        """{"type":"item.completed","item":{"id":"agent_1","type":"agent_message","text":"legacy text"}}""",
    ];

    [Test]
    public async Task Parse_MixedSupportedEventStream_CompletesWithinBudgetAndCoversBranches()
    {
        const int iterations = 2_500;
        var expectedTotal = iterations * SupportedEventStream.Length;

        var eventKinds = new HashSet<string>(StringComparer.Ordinal);
        var toolResultStatuses = new HashSet<ToolResultStatus>();
        var assistantMessages = 0;
        var usageEvents = 0;

        var stopwatch = Stopwatch.StartNew();
        var parsedCount = 0;

        for (var iteration = 0; iteration < iterations; iteration += 1)
        {
            foreach (var line in SupportedEventStream)
            {
                var parsed = ThreadEventParser.Parse(line);
                parsedCount += 1;
                eventKinds.Add(parsed.GetType().FullName ?? parsed.GetType().Name);

                switch (parsed)
                {
                    case MessageEvent { Role: "assistant" }:
                        assistantMessages += 1;
                        break;

                    case ToolResultEvent toolResultEvent:
                        toolResultStatuses.Add(toolResultEvent.Status);
                        break;

                    case ResultEvent { Usage: not null }:
                    case TurnCompletedEvent:
                        usageEvents += 1;
                        break;
                }
            }
        }

        stopwatch.Stop();

        await Assert.That(parsedCount).IsEqualTo(expectedTotal);
        await Assert.That(eventKinds).Contains(typeof(InitEvent).FullName!);
        await Assert.That(eventKinds).Contains(typeof(MessageEvent).FullName!);
        await Assert.That(eventKinds).Contains(typeof(ToolUseEvent).FullName!);
        await Assert.That(eventKinds).Contains(typeof(ToolResultEvent).FullName!);
        await Assert.That(eventKinds).Contains(typeof(ResultEvent).FullName!);
        await Assert.That(eventKinds).Contains(typeof(ThreadStartedEvent).FullName!);
        await Assert.That(eventKinds).Contains(typeof(TurnFailedEvent).FullName!);
        await Assert.That(eventKinds).Contains(typeof(ItemCompletedEvent).FullName!);
        await Assert.That(toolResultStatuses).IsEquivalentTo([ToolResultStatus.Success]);
        await Assert.That(assistantMessages).IsGreaterThan(0);
        await Assert.That(usageEvents).IsGreaterThan(0);
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(20));
    }
}
