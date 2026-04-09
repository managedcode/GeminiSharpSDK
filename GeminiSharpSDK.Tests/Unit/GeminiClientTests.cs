using ManagedCode.GeminiSharpSDK.Client;
using ManagedCode.GeminiSharpSDK.Configuration;
using ManagedCode.GeminiSharpSDK.Models;
using ManagedCode.GeminiSharpSDK.Tests.Shared;
using ManagedCode.GeminiSharpSDK.Tests.TestSupport;

namespace ManagedCode.GeminiSharpSDK.Tests.Unit;

public class GeminiClientTests
{
    private const string ResumeSandboxPrefix = "GeminiClientTests-ResumeThread-";
    private static readonly TimeSpan SandboxCommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MultiTurnTimeout = TimeSpan.FromMinutes(3);

    [Test]
    public async Task StartAsync_CanBeCalledConcurrently()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        var starts = Enumerable.Range(0, 64)
            .Select(_ => client.StartAsync())
            .ToArray();

        await Task.WhenAll(starts);
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Connected);
    }

    [Test]
    public async Task StartThread_CanBeCalledConcurrently()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        await client.StartAsync();

        var createdThreads = await Task.WhenAll(
            Enumerable.Range(0, 64)
                .Select(_ => Task.Run(() => client.StartThread())));

        await Assert.That(createdThreads).Count().IsEqualTo(64);
        await Assert.That(createdThreads.All(thread => thread.Id is null)).IsTrue();
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Connected);
    }

    [Test]
    public async Task StopAsync_CanBeCalledConcurrently()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        await client.StartAsync();

        var stops = Enumerable.Range(0, 64)
            .Select(_ => client.StopAsync())
            .ToArray();

        await Task.WhenAll(stops);
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Disconnected);
    }

    [Test]
    public async Task StartAsync_IsIdempotentAndSetsConnectedState()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        await client.StartAsync();
        await client.StartAsync();

        await Assert.That(client.State).IsEqualTo(GeminiClientState.Connected);
    }

    [Test]
    public async Task StartThread_AutoStartEnabledStartsImplicitly()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
        });

        var thread = client.StartThread();

        await Assert.That(thread.Id).IsNull();
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Connected);
    }

    [Test]
    public async Task StartThread_ParameterlessClientUsesDefaultAutoStart()
    {
        using var client = new GeminiClient();

        var thread = client.StartThread(new ThreadOptions
        {
            Model = GeminiModels.Gemini3ProPreview,
            ModelReasoningEffort = ModelReasoningEffort.Medium,
        });

        await Assert.That(thread.Id).IsNull();
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Connected);
    }

    [Test]
    public async Task ResumeThread_CreatesThreadWithProvidedId()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
        });

        var thread = client.ResumeThread("thread_1");
        await Assert.That(thread.Id).IsEqualTo("thread_1");
    }

    [Test]
    public async Task ResumeThread_ThrowsForInvalidId()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
        });

        var action = () => client.ResumeThread(" ");
        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task StartThread_ThrowsWhenAutoStartDisabled()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        var action = () => client.StartThread();
        await Assert.That(action).ThrowsException();
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Disconnected);
    }

    [Test]
    public async Task StopAsync_SetsDisconnectedState()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        await client.StartAsync();
        await client.StopAsync();

        await Assert.That(client.State).IsEqualTo(GeminiClientState.Disconnected);
    }

    [Test]
    public async Task Dispose_SetsDisposedStateAndBlocksOperations()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        client.Dispose();

        await Assert.That(client.State).IsEqualTo(GeminiClientState.Disposed);

        var action = async () => await client.StartAsync();
        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public async Task Dispose_CanBeCalledConcurrently()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        var disposals = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => client.Dispose()))
            .ToArray();

        await Task.WhenAll(disposals);
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Disposed);
    }

    [Test]
    public async Task GeminiCli_Smoke_GetCliMetadata_ReturnsInstalledVersion()
    {
        using var client = new GeminiClient(new GeminiOptions());

        var metadata = client.GetCliMetadata();

        await Assert.That(string.IsNullOrWhiteSpace(metadata.InstalledVersion)).IsFalse();
        await Assert.That(metadata.InstalledVersion.Contains('.')).IsTrue();
    }

    [Test]
    public async Task GeminiCli_Smoke_GetCliUpdateStatus_ReturnsInstalledVersion()
    {
        using var client = new GeminiClient(new GeminiOptions());

        var status = client.GetCliUpdateStatus();

        await Assert.That(string.IsNullOrWhiteSpace(status.InstalledVersion)).IsFalse();
        await Assert.That(status.InstalledVersion.Contains('.')).IsTrue();
    }

    [Test]
    [Property("RequiresGeminiAuth", "true")]
    [ParallelLimiter<GeminiAuthParallelLimit>]
    public async Task ResumeThread_WithThreadOptions_RunsWithRealGeminiCli()
    {
        var settings = RealGeminiTestSupport.GetRequiredSettings();
        using var sandbox = await RealGeminiTestSandbox.CreateAsync(
            ResumeSandboxPrefix,
            SandboxCommandTimeout);

        using var client = RealGeminiTestSupport.CreateClient();

        var startedThread = client.StartThread(sandbox.CreateThreadOptions(settings.Model, ephemeral: false));
        using var firstCancellation = new CancellationTokenSource(MultiTurnTimeout);

        var firstResult = await startedThread.RunAsync(
            "Reply with short plain text: ok.",
            new TurnOptions { CancellationToken = firstCancellation.Token });

        var threadId = startedThread.Id;
        await Assert.That(threadId).IsNotNull();
        await Assert.That(firstResult.Usage).IsNotNull();

        var resumedThread = client.ResumeThread(
            threadId!,
            sandbox.CreateThreadOptions(settings.Model, ephemeral: false));
        using var secondCancellation = new CancellationTokenSource(MultiTurnTimeout);

        var secondResult = await resumedThread.RunAsync(
            "Reply with short plain text: ok.",
            new TurnOptions { CancellationToken = secondCancellation.Token });

        await Assert.That(secondResult.Usage).IsNotNull();
        await Assert.That(resumedThread.Id).IsEqualTo(threadId);
    }
}
