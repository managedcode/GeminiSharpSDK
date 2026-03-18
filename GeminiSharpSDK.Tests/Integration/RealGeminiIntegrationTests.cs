using ManagedCode.GeminiSharpSDK.Client;
using ManagedCode.GeminiSharpSDK.Models;
using ManagedCode.GeminiSharpSDK.Tests.Shared;

namespace ManagedCode.GeminiSharpSDK.Tests.Integration;

[Property("RequiresGeminiAuth", "true")]
public class RealGeminiIntegrationTests
{
    [Test]
    public async Task RunAsync_WithRealGeminiCli_ReturnsStructuredOutput()
    {
        var settings = RealGeminiTestSupport.GetRequiredSettings();

        using var client = RealGeminiTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var schema = IntegrationOutputSchemas.StatusOnly();

        var result = await thread.RunAsync<StatusResponse>(
            "Reply with a JSON object where status is exactly \"ok\".",
            schema,
            IntegrationOutputJsonContext.Default.StatusResponse,
            cancellation.Token);

        await Assert.That(result.TypedResponse.Status).IsEqualTo("ok");
        await Assert.That(result.Usage).IsNotNull();
    }

    [Test]
    public async Task RunStreamedAsync_WithRealGeminiCli_YieldsCurrentStreamJsonEvents()
    {
        var settings = RealGeminiTestSupport.GetRequiredSettings();

        using var client = RealGeminiTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var streamed = await thread.RunStreamedAsync(
            "Reply with short plain text: ok.",
            new TurnOptions { CancellationToken = cancellation.Token });

        var hasInit = false;
        var hasAssistantMessage = false;
        var hasResult = false;

        await foreach (var threadEvent in streamed.Events.WithCancellation(cancellation.Token))
        {
            hasInit |= threadEvent is InitEvent;
            hasAssistantMessage |= threadEvent is MessageEvent { Role: "assistant" };
            hasResult |= threadEvent is ResultEvent { Status: "success" };
        }

        await Assert.That(hasInit).IsTrue();
        await Assert.That(hasAssistantMessage).IsTrue();
        await Assert.That(hasResult).IsTrue();
        await Assert.That(thread.Id).IsNotNull();
    }

    [Test]
    public async Task RunAsync_WithRealGeminiCli_SecondTurnKeepsThreadId()
    {
        var settings = RealGeminiTestSupport.GetRequiredSettings();

        using var client = RealGeminiTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var schema = IntegrationOutputSchemas.StatusOnly();

        var first = await thread.RunAsync<StatusResponse>(
            "Reply with a JSON object where status is exactly \"ok\".",
            schema,
            IntegrationOutputJsonContext.Default.StatusResponse,
            cancellation.Token);

        var firstThreadId = thread.Id;
        await Assert.That(firstThreadId).IsNotNull();
        await Assert.That(first.Usage).IsNotNull();

        var second = await thread.RunAsync<StatusResponse>(
            "Again: reply with a JSON object where status is exactly \"ok\".",
            schema,
            IntegrationOutputJsonContext.Default.StatusResponse,
            cancellation.Token);

        await Assert.That(second.TypedResponse.Status).IsEqualTo("ok");
        await Assert.That(second.Usage).IsNotNull();
        await Assert.That(thread.Id).IsEqualTo(firstThreadId);
    }

    private static GeminiThread StartRealIntegrationThread(GeminiClient client, string model)
    {
        return client.StartThread(new ThreadOptions
        {
            Model = model,
        });
    }
}
