using System.Diagnostics;
using ManagedCode.GeminiSharpSDK.Client;

namespace ManagedCode.GeminiSharpSDK.Tests.TestSupport;

internal sealed class RealGeminiTestSandbox : IDisposable
{
    private const string SolutionFileName = "ManagedCode.GeminiSharpSDK.slnx";
    private const string TestsDirectoryName = "tests";
    private const string SandboxDirectoryName = ".sandbox";
    private const string GitExecutableName = "git";
    private const string GitInitArgument = "init";
    private const string QuietArgument = "-q";

    private RealGeminiTestSandbox(string workingDirectory)
    {
        WorkingDirectory = workingDirectory;
    }

    public string WorkingDirectory { get; }

    public static async Task<RealGeminiTestSandbox> CreateAsync(string prefix, TimeSpan commandTimeout)
    {
        var repositoryRoot = ResolveRepositoryRootPath();
        var workingDirectory = Path.Combine(
            repositoryRoot,
            TestsDirectoryName,
            SandboxDirectoryName,
            $"{prefix}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(workingDirectory);

        var gitInitResult = await RunCommandAsync(
            GitExecutableName,
            workingDirectory,
            commandTimeout,
            GitInitArgument,
            QuietArgument);

        if (gitInitResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to initialize git sandbox: {gitInitResult.StandardError}");
        }

        return new RealGeminiTestSandbox(workingDirectory);
    }

    public ThreadOptions CreateThreadOptions(string model, bool ephemeral = false)
    {
        return new ThreadOptions
        {
            Model = model,
            WorkingDirectory = WorkingDirectory,
            Ephemeral = ephemeral,
        };
    }

    public void Dispose()
    {
        // Gemini CLI persists visited project directories in ~/.gemini/projects.json and may
        // re-read them later in the same test session. Keeping auth sandboxes on disk avoids
        // spurious "Directory does not exist" startup failures during full-suite runs.
    }

    private static string ResolveRepositoryRootPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, SolutionFileName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test execution directory.");
    }

    private static async Task<CommandResult> RunCommandAsync(
        string fileName,
        string workingDirectory,
        TimeSpan timeout,
        params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);

        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException exception)
        {
            TryKillProcess(process, fileName);
            throw new InvalidOperationException(
                $"Process '{fileName}' timed out after {timeout}.",
                exception);
        }

        return new CommandResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static void TryKillProcess(Process process, string fileName)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to stop timed out process '{fileName}'.", exception);
        }
    }

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
}
