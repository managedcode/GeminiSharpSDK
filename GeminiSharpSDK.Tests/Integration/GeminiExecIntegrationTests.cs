using ManagedCode.GeminiSharpSDK.Client;
using ManagedCode.GeminiSharpSDK.Execution;
using ManagedCode.GeminiSharpSDK.Tests.Shared;
using ManagedCode.GeminiSharpSDK.Tests.TestSupport;

namespace ManagedCode.GeminiSharpSDK.Tests.Integration;

[Property("RequiresGeminiAuth", "true")]
[ParallelLimiter<GeminiAuthParallelLimit>]
public class GeminiExecIntegrationTests
{
    private const string FirstPrompt = "Reply with short plain text: first.";
    private const string SecondPrompt = "Reply with short plain text: second.";
    private const string InvalidModel = "__geminisharp_invalid_model__";
    private const string SandboxPrefix = "GeminiExecIntegrationTests";
    private static readonly TimeSpan SandboxCommandTimeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task RunAsync_UsesDefaultProcessRunner_EndToEnd()
    {
        var settings = RealGeminiTestSupport.GetRequiredSettings();
        using var sandbox = await RealGeminiTestSandbox.CreateAsync(SandboxPrefix, SandboxCommandTimeout);

        var exec = new GeminiExec();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var lines = await DrainToListAsync(exec.RunAsync(new GeminiExecArgs
        {
            Input = FirstPrompt,
            Model = settings.Model,
            WorkingDirectory = sandbox.WorkingDirectory,
            CancellationToken = cancellation.Token,
        }));

        await Assert.That(lines.Any(line => line.Contains("\"type\":\"init\"", StringComparison.Ordinal))).IsTrue();
        await Assert.That(lines.Any(line => line.Contains("\"type\":\"result\"", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task RunAsync_SecondCallPassesResumeArgument_EndToEnd()
    {
        var settings = RealGeminiTestSupport.GetRequiredSettings();
        using var sandbox = await RealGeminiTestSandbox.CreateAsync(SandboxPrefix, SandboxCommandTimeout);

        using var client = RealGeminiTestSupport.CreateClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var thread = client.StartThread(sandbox.CreateThreadOptions(settings.Model, ephemeral: false));

        var firstResult = await thread.RunAsync(
            FirstPrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        var threadId = thread.Id;
        await Assert.That(threadId).IsNotNull();
        await Assert.That(firstResult.Usage).IsNotNull();

        var secondResult = await thread.RunAsync(
            SecondPrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        await Assert.That(secondResult.Usage).IsNotNull();
        await Assert.That(thread.Id).IsEqualTo(threadId);
    }

    [Test]
    public async Task RunAsync_PropagatesNonZeroExitCode_EndToEnd()
    {
        var settings = RealGeminiTestSupport.GetRequiredSettings();
        using var sandbox = await RealGeminiTestSandbox.CreateAsync(SandboxPrefix, SandboxCommandTimeout);

        var exec = new GeminiExec();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var action = async () => await DrainAsync(exec.RunAsync(new GeminiExecArgs
        {
            Input = FirstPrompt,
            Model = InvalidModel,
            WorkingDirectory = sandbox.WorkingDirectory,
            CancellationToken = cancellation.Token,
        }));

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("exited with code");
    }

    private static async Task DrainAsync(IAsyncEnumerable<string> lines)
    {
        await foreach (var _ in lines)
        {
            // Intentionally empty.
        }
    }

    private static async Task<List<string>> DrainToListAsync(IAsyncEnumerable<string> lines)
    {
        var result = new List<string>();

        await foreach (var line in lines)
        {
            result.Add(line);
        }

        return result;
    }
}
