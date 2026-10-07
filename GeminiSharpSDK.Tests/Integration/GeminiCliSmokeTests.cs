using System.Diagnostics;
using System.Text.RegularExpressions;
using ManagedCode.GeminiSharpSDK.Internal;

namespace ManagedCode.GeminiSharpSDK.Tests.Integration;

[NotInParallel("CliProcess")]
public class GeminiCliSmokeTests
{
    private const string SolutionFileName = "ManagedCode.GeminiSharpSDK.slnx";
    private const string TestsDirectoryName = "tests";
    private const string SandboxDirectoryName = ".sandbox";
    private const string SandboxPrefix = "GeminiCliSmokeTests-";
    private const string PathEnvironmentVariable = "PATH";
    private const string HomeEnvironmentVariable = "HOME";
    private const string UserProfileEnvironmentVariable = "USERPROFILE";
    private const string XdgConfigHomeEnvironmentVariable = "XDG_CONFIG_HOME";
    private const string AppDataEnvironmentVariable = "APPDATA";
    private const string LocalAppDataEnvironmentVariable = "LOCALAPPDATA";
    private const string OpenAiApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    private const string OpenAiBaseUrlEnvironmentVariable = "OPENAI_BASE_URL";
    private const string GeminiApiKeyEnvironmentVariable = "GEMINI_API_KEY";
    private const string GeminiHomeEnvironmentVariable = "GEMINI_HOME";
    private const string GeminiHomeDirectoryName = ".gemini";
    private const string AppDataDirectoryName = "AppData";
    private const string RoamingDirectoryName = "Roaming";
    private const string LocalDirectoryName = "Local";
    private const string ConfigDirectoryName = ".config";
    private const string VersionFlag = "--version";
    private const string HelpFlag = "--help";
    private const string PromptFlag = "--prompt";
    private const string OutputFormatFlag = "--output-format";
    private const string StreamJsonValue = "stream-json";
    private const string VersionPattern = @"\b\d+\.\d+\.\d+(?:[.+-][0-9A-Za-z.-]+)?\b";
    private const string RootHelpToken = "Usage:";
    private const string RootHelpCommandToken = "Commands:";
    private const string PromptHelpToken = "--prompt";
    private const string OutputFormatHelpToken = "--output-format";
    private const string InitEventToken = "\"type\":\"init\"";
    private const string ResultEventToken = "\"type\":\"result\"";
    private const string NotLoggedInToken = "not logged in";
    private const string NotAuthenticatedToken = "not authenticated";
    private const string LoginGuidanceToken = "gemini login";
    private const string AuthMethodToken = "auth method";
    private const string QuotaToken = "quota";

    [Test]
    public async Task GeminiCli_Smoke_FindExecutablePath_ResolvesExistingBinary()
    {
        var executablePath = ResolveExecutablePath();
        await Assert.That(File.Exists(executablePath)).IsTrue();
    }

    [Test]
    public async Task GeminiCli_Smoke_VersionCommand_ReturnsGeminiCliVersion()
    {
        var executablePath = ResolveExecutablePath();

        var result = await RunGeminiAsync(executablePath, null, VersionFlag);
        await Assert.That(result.ExitCode).IsEqualTo(0);

        var output = string.Concat(result.StandardOutput, result.StandardError);
        await Assert.That(VersionRegex.IsMatch(output)).IsTrue();
    }

    [Test]
    public async Task GeminiCli_Smoke_HelpCommand_ReturnsRootCommands()
    {
        var executablePath = ResolveExecutablePath();

        var result = await RunGeminiAsync(executablePath, null, HelpFlag);
        await Assert.That(result.ExitCode).IsEqualTo(0);

        var output = string.Concat(result.StandardOutput, result.StandardError);
        await Assert.That(output.Contains(RootHelpToken, StringComparison.Ordinal)).IsTrue();
        await Assert.That(output.Contains(RootHelpCommandToken, StringComparison.Ordinal)).IsTrue();
        await Assert.That(output.Contains(PromptHelpToken, StringComparison.Ordinal)).IsTrue();
        await Assert.That(output.Contains(OutputFormatHelpToken, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task GeminiCli_Smoke_StreamJsonMode_EmitsCurrentEnvelope()
    {
        var executablePath = ResolveExecutablePath();
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var result = await RunGeminiAsync(
                executablePath,
                CreateUnauthenticatedEnvironmentOverrides(sandboxDirectory),
                PromptFlag,
                "Reply exactly with OK",
                OutputFormatFlag,
                StreamJsonValue);

            var output = string.Concat(result.StandardOutput, result.StandardError);
            await Assert.That(ContainsCurrentStreamEnvelope(output) || ContainsUnauthenticatedSignal(output)).IsTrue();
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task GeminiCli_Smoke_UnauthenticatedPrompt_ReportsAuthenticationOrQuotaSignal()
    {
        var executablePath = ResolveExecutablePath();
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var environmentOverrides = CreateUnauthenticatedEnvironmentOverrides(sandboxDirectory);
            var result = await RunGeminiAsync(
                executablePath,
                environmentOverrides,
                PromptFlag,
                "Reply exactly with OK",
                OutputFormatFlag,
                StreamJsonValue);

            var output = string.Concat(result.StandardOutput, result.StandardError);
            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(ContainsUnauthenticatedSignal(output)).IsTrue();
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    private static string ResolveExecutablePath()
    {
        var resolvedPath = GeminiCliLocator.FindGeminiPath(null);
        if (Path.IsPathRooted(resolvedPath))
        {
            if (File.Exists(resolvedPath))
            {
                return resolvedPath;
            }

            throw new InvalidOperationException($"Gemini CLI path is rooted but missing: '{resolvedPath}'.");
        }

        if (GeminiCliLocator.TryResolvePathExecutable(
                Environment.GetEnvironmentVariable(PathEnvironmentVariable),
                OperatingSystem.IsWindows(),
                out var pathExecutable))
        {
            return pathExecutable;
        }

        throw new InvalidOperationException("Failed to resolve Gemini CLI path.");
    }

    private static string CreateSandboxDirectory()
    {
        var repositoryRoot = ResolveRepositoryRootPath();
        var sandboxDirectory = Path.Combine(
            repositoryRoot,
            TestsDirectoryName,
            SandboxDirectoryName,
            $"{SandboxPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandboxDirectory);
        return sandboxDirectory;
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

    private static Dictionary<string, string> CreateUnauthenticatedEnvironmentOverrides(string sandboxDirectory)
    {
        var geminiHome = Path.Combine(sandboxDirectory, GeminiHomeDirectoryName);
        var configHome = Path.Combine(sandboxDirectory, ConfigDirectoryName);
        var appData = Path.Combine(sandboxDirectory, AppDataDirectoryName, RoamingDirectoryName);
        var localAppData = Path.Combine(sandboxDirectory, AppDataDirectoryName, LocalDirectoryName);

        Directory.CreateDirectory(geminiHome);
        Directory.CreateDirectory(configHome);
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(localAppData);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GeminiHomeEnvironmentVariable] = geminiHome,
            [HomeEnvironmentVariable] = sandboxDirectory,
            [UserProfileEnvironmentVariable] = sandboxDirectory,
            [XdgConfigHomeEnvironmentVariable] = configHome,
            [AppDataEnvironmentVariable] = appData,
            [LocalAppDataEnvironmentVariable] = localAppData,
            [OpenAiApiKeyEnvironmentVariable] = string.Empty,
            [OpenAiBaseUrlEnvironmentVariable] = string.Empty,
            [GeminiApiKeyEnvironmentVariable] = string.Empty,
        };
    }

    private static async Task<GeminiProcessResult> RunGeminiAsync(
        string executablePath,
        IReadOnlyDictionary<string, string>? environmentOverrides,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            startInfo.ArgumentList.Add(argument);
        }

        if (environmentOverrides is not null)
        {
            foreach (var (key, value) in environmentOverrides)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start Gemini CLI at '{executablePath}'.");
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to start Gemini CLI at '{executablePath}'.", exception);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new GeminiProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static bool ContainsUnauthenticatedSignal(string output)
    {
        return output.Contains(NotLoggedInToken, StringComparison.OrdinalIgnoreCase)
               || output.Contains(NotAuthenticatedToken, StringComparison.OrdinalIgnoreCase)
               || output.Contains(LoginGuidanceToken, StringComparison.OrdinalIgnoreCase)
               || output.Contains(AuthMethodToken, StringComparison.OrdinalIgnoreCase)
               || output.Contains(QuotaToken, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsCurrentStreamEnvelope(string output)
    {
        return output.Contains(InitEventToken, StringComparison.Ordinal)
               && output.Contains(ResultEventToken, StringComparison.Ordinal);
    }

    private static readonly Regex VersionRegex = new(VersionPattern, RegexOptions.Compiled);

    private sealed record GeminiProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
