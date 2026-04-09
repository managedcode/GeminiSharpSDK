using System.Diagnostics;
using System.Text.Json;
using ManagedCode.GeminiSharpSDK.Client;
using ManagedCode.GeminiSharpSDK.Internal;
using ManagedCode.GeminiSharpSDK.Models;
using ManagedCode.GeminiSharpSDK.Tests.Shared;
using ManagedCode.GeminiSharpSDK.Tests.TestSupport;

namespace ManagedCode.GeminiSharpSDK.Tests.Integration;

[Property("RequiresGeminiAuth", "true")]
[ParallelLimiter<GeminiAuthParallelLimit>]
public class RealGeminiIntegrationTests
{
    private const string SandboxPrefix = "RealGeminiIntegrationTests";
    private const string SessionVisibilitySandboxPrefix = "RealGeminiIntegrationTests-SessionVisibility";
    private const string ListSessionsFlag = "--list-sessions";
    private const string GeminiDirectoryName = ".gemini";
    private const string ProjectsFileName = "projects.json";
    private const string ProjectsPropertyName = "projects";
    private const string TmpDirectoryName = "tmp";
    private const string ChatsDirectoryName = "chats";
    private const string SessionIdPropertyName = "sessionId";
    private const string SessionFileSearchPattern = "session-*.json";
    private const string ProjectSessionVisiblePrompt = "Reply with short plain text: ok.";
    private static readonly TimeSpan SessionVisibilityTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CliCommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    [Test]
    public async Task RunAsync_WithRealGeminiCli_ReturnsStructuredOutput()
    {
        var settings = RealGeminiTestSupport.GetRequiredSettings();
        using var sandbox = await RealGeminiTestSandbox.CreateAsync(SandboxPrefix, CliCommandTimeout);

        using var client = RealGeminiTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model, sandbox.WorkingDirectory);

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
        using var sandbox = await RealGeminiTestSandbox.CreateAsync(SandboxPrefix, CliCommandTimeout);

        using var client = RealGeminiTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model, sandbox.WorkingDirectory);
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
        using var sandbox = await RealGeminiTestSandbox.CreateAsync(SandboxPrefix, CliCommandTimeout);

        using var client = RealGeminiTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model, sandbox.WorkingDirectory);
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

    [Test]
    public async Task RunAsync_WithFreshWorkingDirectory_PersistsSessionVisibleToGeminiCli()
    {
        var settings = RealGeminiTestSupport.GetRequiredSettings();
        using var sandbox = await RealGeminiTestSandbox.CreateAsync(
            SessionVisibilitySandboxPrefix,
            CliCommandTimeout);
        var sandboxDirectory = sandbox.WorkingDirectory;

        using var client = RealGeminiTestSupport.CreateClient();
        var thread = client.StartThread(sandbox.CreateThreadOptions(settings.Model, ephemeral: false));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var result = await thread.RunAsync(
            ProjectSessionVisiblePrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        await Assert.That(result.Usage).IsNotNull();
        await Assert.That(thread.Id).IsNotNull();

        var persistedSessionPath = await FindPersistedSessionPathAsync(
            sandboxDirectory,
            thread.Id!,
            SessionVisibilityTimeout);

        await Assert.That(persistedSessionPath).IsNotNull();

        var listSessionsResult = await RunGeminiAsync(
            sandboxDirectory,
            CliCommandTimeout,
            ListSessionsFlag);

        await Assert.That(listSessionsResult.ExitCode).IsEqualTo(0);
        await Assert.That(string.Concat(listSessionsResult.StandardOutput, listSessionsResult.StandardError))
            .Contains(thread.Id!);
    }

    private static GeminiThread StartRealIntegrationThread(GeminiClient client, string model, string workingDirectory)
    {
        return client.StartThread(new ThreadOptions
        {
            Model = model,
            WorkingDirectory = workingDirectory,
            Ephemeral = false,
        });
    }

    private static async Task<string?> FindPersistedSessionPathAsync(
        string workingDirectory,
        string threadId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= deadline)
        {
            var persistedSessionPath = TryFindPersistedSessionPath(workingDirectory, threadId);
            if (persistedSessionPath is not null)
            {
                return persistedSessionPath;
            }

            await Task.Delay(PollInterval);
        }

        return null;
    }

    private static string? TryFindPersistedSessionPath(string workingDirectory, string threadId)
    {
        var projectKey = TryResolveProjectKey(workingDirectory);
        if (string.IsNullOrWhiteSpace(projectKey))
        {
            return null;
        }

        var geminiHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            GeminiDirectoryName);
        var chatsDirectory = Path.Combine(geminiHome, TmpDirectoryName, projectKey, ChatsDirectoryName);
        if (!Directory.Exists(chatsDirectory))
        {
            return null;
        }

        foreach (var sessionFile in Directory.EnumerateFiles(chatsDirectory, SessionFileSearchPattern, SearchOption.TopDirectoryOnly))
        {
            if (SessionFileMatchesThreadId(sessionFile, threadId))
            {
                return sessionFile;
            }
        }

        return null;
    }

    private static string? TryResolveProjectKey(string workingDirectory)
    {
        var projectsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            GeminiDirectoryName,
            ProjectsFileName);
        if (!File.Exists(projectsPath))
        {
            return null;
        }

        using var stream = File.OpenRead(projectsPath);
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty(ProjectsPropertyName, out var projectsElement)
            || projectsElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var normalizedWorkingDirectory = Path.GetFullPath(workingDirectory);
        foreach (var project in projectsElement.EnumerateObject())
        {
            if (!string.Equals(Path.GetFullPath(project.Name), normalizedWorkingDirectory, StringComparison.Ordinal))
            {
                continue;
            }

            return project.Value.ValueKind == JsonValueKind.String
                ? project.Value.GetString()
                : null;
        }

        return null;
    }

    private static bool SessionFileMatchesThreadId(string sessionFile, string threadId)
    {
        using var stream = File.OpenRead(sessionFile);
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.TryGetProperty(SessionIdPropertyName, out var sessionIdElement)
               && sessionIdElement.ValueKind == JsonValueKind.String
               && string.Equals(sessionIdElement.GetString(), threadId, StringComparison.Ordinal);
    }

    private static Task<GeminiCommandResult> RunGeminiAsync(
        string workingDirectory,
        TimeSpan timeout,
        params string[] arguments)
    {
        var executablePath = GeminiCliLocator.FindGeminiPath(null);
        return RunCommand(executablePath, workingDirectory, timeout, arguments);
    }

    private static async Task<GeminiCommandResult> RunCommand(
        string executablePath,
        string workingDirectory,
        TimeSpan timeout,
        params string[] arguments)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };

        foreach (var argument in arguments)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start command '{executablePath}'.");
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to start command '{executablePath}'.", exception);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellation.Token);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellation.Token);

        await process.WaitForExitAsync(cancellation.Token);

        return new GeminiCommandResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private sealed record GeminiCommandResult(int ExitCode, string StandardOutput, string StandardError);
}
