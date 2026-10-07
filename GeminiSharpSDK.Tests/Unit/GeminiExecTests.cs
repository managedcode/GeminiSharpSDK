using System.Text.Json;
using ManagedCode.GeminiSharpSDK.Client;
using ManagedCode.GeminiSharpSDK.Execution;
using ManagedCode.GeminiSharpSDK.Models;
using ManagedCode.GeminiSharpSDK.Tests.Shared;
using ManagedCode.GeminiSharpSDK.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagedCode.GeminiSharpSDK.Tests.Unit;

public class GeminiExecTests
{
    private const string PromptFlag = "--prompt";
    private const string YoloFlag = "--yolo";
    private const string ApprovalModeYoloPrompt = "--approval-mode=yolo --yolo";
    private const string ReadOnlySandboxMessageFragment = "read-only sandbox mode";
    private const string NodeExecutableName = "node";
    private const string PathEnvironmentVariable = "PATH";
    private const string NodeLaunchSkipReason = "This Node.js launch regression uses Unix executable naming.";
    private const string JavaScriptFixtureName = "gemini-cli-fixture.js";
    private const string TestDirectoryName = "tests";
    private const string SandboxDirectoryName = ".sandbox";
    private const string JavaScriptInputPropertyName = "input";
    private const string JavaScriptArgumentsPropertyName = "args";
    private const string NodeExecutableMissingMessage = "Node.js was not found on PATH.";
    private const string JavaScriptFixtureSource = "let input='';process.stdin.setEncoding('utf8');process.stdin.on('data',chunk=>input+=chunk);process.stdin.on('end',()=>process.stdout.write(JSON.stringify({input,args:process.argv.slice(2)})+'\\n'));";
    private const string JavaScriptPromptPrefix = "--yolo \"literal\" & metacharacters ";
    private const int JavaScriptPromptLength = 262144;
    [Test]
    public async Task BuildCommandArgs_BuildsCurrentHeadlessGeminiCliArguments()
    {
        var exec = new GeminiExec("gemini", null, null);

        var commandArgs = exec.BuildCommandArgs(new GeminiExecArgs
        {
            Input = "test prompt",
            Model = GeminiModels.AutoGemini3,
            SandboxMode = SandboxMode.WorkspaceWrite,
            AdditionalDirectories = ["/tmp/shared", "/tmp/other"],
            ApprovalPolicy = ApprovalMode.Default,
            ThreadId = "thread_1",
            AdditionalCliArguments = ["--allowed-mcp-server-names", "playwright"],
        });

        await Assert.That(commandArgs[0]).IsEqualTo("--output-format");
        await Assert.That(commandArgs[1]).IsEqualTo("stream-json");
        await Assert.That(commandArgs.Contains(PromptFlag)).IsFalse();
        await Assert.That(ContainsPair(commandArgs, "--model", GeminiModels.AutoGemini3)).IsTrue();
        await Assert.That(commandArgs.Contains("--sandbox")).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--approval-mode", "default")).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--resume", "thread_1")).IsTrue();
        await Assert.That(CollectFlagValues(commandArgs, "--include-directories"))
            .IsEquivalentTo(["/tmp/shared", "/tmp/other"]);
        await Assert.That(commandArgs.Contains("--allowed-mcp-server-names")).IsTrue();
        await Assert.That(commandArgs.Contains("playwright")).IsTrue();
    }

    [Test]
    public async Task PublicExec_LaunchesExplicitJavaScriptThroughRuntimeOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test(NodeLaunchSkipReason);
            return;
        }

        var sandboxDirectory = Path.Combine(Environment.CurrentDirectory, TestDirectoryName, SandboxDirectoryName,
            $"GeminiNodeLaunch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandboxDirectory);
        var nodePath = FindExecutableInPath(NodeExecutableName);
        var scriptPath = Path.Combine(sandboxDirectory, JavaScriptFixtureName);
        var prompt = string.Concat(JavaScriptPromptPrefix, new string('p', JavaScriptPromptLength));
        await File.WriteAllTextAsync(scriptPath, JavaScriptFixtureSource);

        try
        {
            var exec = new GeminiExec(scriptPath, new Dictionary<string, string>
            {
                [PathEnvironmentVariable] = Path.GetDirectoryName(nodePath)!,
            }, null);
            var lines = await DrainToListAsync(exec.RunAsync(new GeminiExecArgs { Input = prompt }));
            await Assert.That(lines).Count().IsEqualTo(1);
            using var document = JsonDocument.Parse(lines[0]);
            await Assert.That(document.RootElement.GetProperty(JavaScriptInputPropertyName).GetString()).IsEqualTo(prompt);
            await Assert.That(document.RootElement.GetProperty(JavaScriptArgumentsPropertyName).GetArrayLength()).IsGreaterThan(0);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task BuildCommandArgs_LeavesPromptOutOfArguments()
    {
        var exec = new GeminiExec("gemini", null, null);

        var commandArgs = exec.BuildCommandArgs(new GeminiExecArgs
        {
            Input = ApprovalModeYoloPrompt,
        });

        await Assert.That(commandArgs.Contains(PromptFlag)).IsFalse();
        await Assert.That(commandArgs.Contains(YoloFlag)).IsFalse();
        await Assert.That(commandArgs.Contains(ApprovalModeYoloPrompt)).IsFalse();
    }

    [Test]
    public async Task BuildCommandArgs_DangerFullAccessOmitsSandboxFlag()
    {
        var exec = new GeminiExec("gemini", null, null);

        var commandArgs = exec.BuildCommandArgs(new GeminiExecArgs
        {
            Input = "test",
            SandboxMode = SandboxMode.DangerFullAccess,
        });

        await Assert.That(commandArgs.Contains("--sandbox")).IsFalse();
    }

    [Test]
    public async Task BuildCommandArgs_ReadOnlySandboxFailsInsteadOfUsingPlanMode()
    {
        var exec = new GeminiExec("gemini", null, null);

        var exception = await Assert.That(() => exec.BuildCommandArgs(new GeminiExecArgs
        {
            Input = "test",
            SandboxMode = SandboxMode.ReadOnly,
        })).ThrowsException();

        await Assert.That(exception).IsTypeOf<NotSupportedException>();
        await Assert.That(exception!.Message).Contains(ReadOnlySandboxMessageFragment);
    }

    [Test]
    public async Task BuildCommandArgs_MapsLegacyOnRequestToDefaultApprovalMode()
    {
        var exec = new GeminiExec("gemini", null, null);

        var commandArgs = exec.BuildCommandArgs(new GeminiExecArgs
        {
            Input = "test",
            ApprovalPolicy = ApprovalMode.OnRequest,
        });

        await Assert.That(ContainsPair(commandArgs, "--approval-mode", "default")).IsTrue();
    }

    [Test]
    public async Task BuildCommandArgs_ThrowsForUnsupportedHeadlessOptions()
    {
        var exec = new GeminiExec("gemini", null, null);

        var action = () => exec.BuildCommandArgs(new GeminiExecArgs
        {
            Input = "test",
            WebSearchMode = WebSearchMode.Disabled,
        });

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<NotSupportedException>();
        await Assert.That(exception!.Message).Contains(nameof(GeminiExecArgs.WebSearchMode));
    }

    [Test]
    public async Task BuildEnvironment_UsesProvidedEnvironmentWithoutLeakingProcessEnvironment()
    {
        Environment.SetEnvironmentVariable("GEMINI_SHOULD_NOT_LEAK", "leak");

        try
        {
            var exec = new GeminiExec(
                executablePath: Environment.ProcessPath!,
                environmentOverride: new Dictionary<string, string>
                {
                    ["CUSTOM_ENV"] = "custom",
                },
                configOverrides: null);

            var environment = exec.BuildEnvironment("https://example.local", "secret");

            await Assert.That(environment["CUSTOM_ENV"]).IsEqualTo("custom");
            await Assert.That(environment.ContainsKey("GEMINI_SHOULD_NOT_LEAK")).IsFalse();
            await Assert.That(environment["OPENAI_BASE_URL"]).IsEqualTo("https://example.local");
            await Assert.That(environment["GEMINI_API_KEY"]).IsEqualTo("secret");
            await Assert.That(environment["GEMINI_INTERNAL_ORIGINATOR_OVERRIDE"]).IsEqualTo("gemini_sdk_csharp");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_SHOULD_NOT_LEAK", null);
        }
    }

    [Test]
    public async Task BuildEnvironment_InheritsEnvironmentWhenOverrideMissing()
    {
        Environment.SetEnvironmentVariable("GEMINI_SHOULD_INHERIT", "yes");

        try
        {
            var exec = new GeminiExec("gemini", null, null);
            var environment = exec.BuildEnvironment(null, null);

            await Assert.That(environment["GEMINI_SHOULD_INHERIT"]).IsEqualTo("yes");
            await Assert.That(environment.ContainsKey("GEMINI_INTERNAL_ORIGINATOR_OVERRIDE")).IsTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_SHOULD_INHERIT", null);
        }
    }

    [Test]
    public async Task RunAsync_WithNullLogger_StillPropagatesFailure()
    {
        var missingExecutable = Path.Combine(
            Environment.CurrentDirectory,
            "tests",
            ".sandbox",
            $"missing-gemini-{Guid.NewGuid():N}",
            "gemini");

        var action = () => new GeminiExec(missingExecutable, null, null, NullLogger.Instance);

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<FileNotFoundException>();
        await Assert.That(exception!.Message).Contains("configured Gemini CLI executable was not found");
    }

    [Test]
    [Property("RequiresGeminiAuth", "true")]
    [ParallelLimiter<GeminiAuthParallelLimit>]
    public async Task RunAsync_WithNullLogger_CompletesSuccessfully_WithRealGeminiCli()
    {
        var settings = RealGeminiTestSupport.GetRequiredSettings();

        var exec = new GeminiExec(logger: NullLogger.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var lines = await DrainToListAsync(exec.RunAsync(new GeminiExecArgs
        {
            Input = "Reply with short plain text: ok.",
            Model = settings.Model,
            CancellationToken = cancellation.Token,
        }));

        await Assert.That(lines.Count).IsGreaterThan(0);
        await Assert.That(lines.Any(line => line.Contains("\"type\":\"init\"", StringComparison.Ordinal))).IsTrue();
        await Assert.That(lines.Any(line => line.Contains("\"type\":\"result\"", StringComparison.Ordinal))).IsTrue();
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

    private static string FindExecutableInPath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        foreach (var entry in path?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
        {
            var candidate = Path.Combine(entry, executableName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(NodeExecutableMissingMessage, executableName);
    }

    private static bool ContainsPair(IReadOnlyList<string> args, string key, string value)
    {
        for (var index = 0; index < args.Count - 1; index += 1)
        {
            if (args[index] == key && args[index + 1] == value)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> CollectFlagValues(IReadOnlyList<string> args, string flag)
    {
        var result = new List<string>();
        for (var index = 0; index < args.Count - 1; index += 1)
        {
            if (args[index] == flag)
            {
                result.Add(args[index + 1]);
            }
        }

        return result;
    }
}
