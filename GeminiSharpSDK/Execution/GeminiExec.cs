using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ManagedCode.GeminiSharpSDK.Client;
using ManagedCode.GeminiSharpSDK.Configuration;
using ManagedCode.GeminiSharpSDK.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagedCode.GeminiSharpSDK.Execution;

public sealed class GeminiExec
{
    private const string PromptFlag = "--prompt";
    private const string OutputFormatFlag = "--output-format";
    private const string StreamJsonOutputFormat = "stream-json";
    private const string ModelFlag = "--model";
    private const string ResumeFlag = "--resume";
    private const string SandboxFlag = "--sandbox";
    private const string IncludeDirectoriesFlag = "--include-directories";
    private const string ApprovalModeFlag = "--approval-mode";
    private const string UnsupportedHeadlessOptionsMessagePrefix =
        "Gemini CLI 0.34.0 headless mode does not expose the requested SDK option";

    private const string InternalOriginatorEnv = "GEMINI_INTERNAL_ORIGINATOR_OVERRIDE";
    private const string CSharpSdkOriginator = "gemini_sdk_csharp";
    private const string OpenAiBaseUrlEnv = "OPENAI_BASE_URL";
    private const string GeminiApiKeyEnv = "GEMINI_API_KEY";
    private const string ProcessTerminationTimeoutMustBePositiveMessage = "Process termination timeout must be positive.";

    private readonly string _executablePath;
    private readonly IReadOnlyDictionary<string, string>? _environmentOverride;
    private readonly JsonObject? _configOverrides;
    private readonly IGeminiProcessRunner _processRunner;
    private readonly ILogger _logger;
    private readonly TimeSpan _processTerminationTimeout;

    public GeminiExec(
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? environmentOverride = null,
        JsonObject? configOverrides = null,
        ILogger? logger = null)
        : this(executablePath, environmentOverride, configOverrides, null, logger, GeminiOptions.DefaultProcessTerminationTimeout)
    {
    }

    public GeminiExec(
        TimeSpan processTerminationTimeout,
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? environmentOverride = null,
        JsonObject? configOverrides = null,
        ILogger? logger = null)
        : this(executablePath, environmentOverride, configOverrides, null, logger, processTerminationTimeout)
    {
    }

    internal GeminiExec(
        string? executablePath,
        IReadOnlyDictionary<string, string>? environmentOverride,
        JsonObject? configOverrides,
        IGeminiProcessRunner? processRunner,
        ILogger? logger = null,
        TimeSpan? processTerminationTimeout = null)
    {
        var resolvedTerminationTimeout = processTerminationTimeout ?? GeminiOptions.DefaultProcessTerminationTimeout;
        if (resolvedTerminationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processTerminationTimeout), resolvedTerminationTimeout, ProcessTerminationTimeoutMustBePositiveMessage);
        }

        _executablePath = GeminiCliLocator.FindGeminiPath(executablePath);
        _environmentOverride = environmentOverride;
        _configOverrides = configOverrides;
        _processRunner = processRunner ?? new DefaultGeminiProcessRunner();
        _logger = logger ?? NullLogger.Instance;
        _processTerminationTimeout = resolvedTerminationTimeout;
    }

    public IAsyncEnumerable<string> RunAsync(GeminiExecArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var commandArgs = BuildCommandArgs(args);
        var environment = BuildEnvironment(args.BaseUrl, args.ApiKey);
        var invocation = new GeminiProcessInvocation(
            _executablePath,
            commandArgs,
            environment,
            args.WorkingDirectory,
            _processTerminationTimeout);

        return RunWithDiagnosticsAsync(invocation, args.CancellationToken);
    }

    private async IAsyncEnumerable<string> RunWithDiagnosticsAsync(
        GeminiProcessInvocation invocation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Logging.GeminiExecLog.Starting(_logger, invocation.ExecutablePath, invocation.Arguments.Count);

        if (cancellationToken.IsCancellationRequested)
        {
            Logging.GeminiExecLog.Cancelled(_logger);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var lineCount = 0;

        IAsyncEnumerator<string> enumerator;
        try
        {
            enumerator = _processRunner
                .RunAsync(invocation, _logger, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logging.GeminiExecLog.Cancelled(_logger);
            throw;
        }
        catch (Exception)
        {
            Logging.GeminiExecLog.Failed(_logger);
            throw;
        }

        await using (enumerator)
        {
            while (true)
            {
                string line;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    line = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    Logging.GeminiExecLog.Cancelled(_logger);
                    throw;
                }
                catch (Exception)
                {
                    Logging.GeminiExecLog.Failed(_logger);
                    throw;
                }

                lineCount += 1;
                yield return line;
            }

            Logging.GeminiExecLog.Completed(_logger, lineCount);
        }
    }

    internal IReadOnlyList<string> BuildCommandArgs(GeminiExecArgs args)
    {
        ValidateSupportedArgs(args);

        var commandArgs = new List<string>
        {
            PromptFlag,
            args.Input,
            OutputFormatFlag,
            StreamJsonOutputFormat,
        };

        if (!string.IsNullOrWhiteSpace(args.Model))
        {
            commandArgs.Add(ModelFlag);
            commandArgs.Add(args.Model);
        }

        if (args.SandboxMode.HasValue && args.SandboxMode.Value != SandboxMode.DangerFullAccess)
        {
            commandArgs.Add(SandboxFlag);
        }

        if (args.AdditionalDirectories is not null)
        {
            AddRepeatedFlag(commandArgs, IncludeDirectoriesFlag, args.AdditionalDirectories);
        }

        if (args.ApprovalPolicy.HasValue)
        {
            commandArgs.Add(ApprovalModeFlag);
            commandArgs.Add(args.ApprovalPolicy.Value.ToCliValue());
        }

        if (args.AdditionalCliArguments is not null)
        {
            foreach (var argument in args.AdditionalCliArguments)
            {
                if (string.IsNullOrWhiteSpace(argument))
                {
                    continue;
                }

                commandArgs.Add(argument);
            }
        }

        if (!string.IsNullOrWhiteSpace(args.ThreadId))
        {
            commandArgs.Add(ResumeFlag);
            commandArgs.Add(args.ThreadId);
        }

        return commandArgs;
    }

    internal IReadOnlyDictionary<string, string> BuildEnvironment(string? baseUrl, string? apiKey)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_environmentOverride is not null)
        {
            foreach (var (key, value) in _environmentOverride)
            {
                environment[key] = value;
            }
        }
        else
        {
            foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            {
                if (variable.Key is string key && variable.Value is string value)
                {
                    environment[key] = value;
                }
            }
        }

        if (!environment.ContainsKey(InternalOriginatorEnv))
        {
            environment[InternalOriginatorEnv] = CSharpSdkOriginator;
        }

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            environment[OpenAiBaseUrlEnv] = baseUrl;
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            environment[GeminiApiKeyEnv] = apiKey;
        }

        return environment;
    }

    private static void AddRepeatedFlag(
        List<string> commandArgs,
        string flag,
        IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            commandArgs.Add(flag);
            commandArgs.Add(value);
        }
    }

    private void ValidateSupportedArgs(GeminiExecArgs args)
    {
        ThrowIfUnsupported(args.Images is { Count: > 0 }, nameof(GeminiExecArgs.Images));
        ThrowIfUnsupported(!string.IsNullOrWhiteSpace(args.Profile), nameof(GeminiExecArgs.Profile));
        ThrowIfUnsupported(args.UseOss, nameof(GeminiExecArgs.UseOss));
        ThrowIfUnsupported(args.LocalProvider.HasValue, nameof(GeminiExecArgs.LocalProvider));
        ThrowIfUnsupported(args.FullAuto, nameof(GeminiExecArgs.FullAuto));
        ThrowIfUnsupported(
            args.DangerouslyBypassApprovalsAndSandbox,
            nameof(GeminiExecArgs.DangerouslyBypassApprovalsAndSandbox));
        ThrowIfUnsupported(args.Ephemeral, nameof(GeminiExecArgs.Ephemeral));
        ThrowIfUnsupported(args.Color.HasValue, nameof(GeminiExecArgs.Color));
        ThrowIfUnsupported(args.ProgressCursor, nameof(GeminiExecArgs.ProgressCursor));
        ThrowIfUnsupported(!string.IsNullOrWhiteSpace(args.OutputLastMessageFile), nameof(GeminiExecArgs.OutputLastMessageFile));
        ThrowIfUnsupported(args.EnabledFeatures is { Count: > 0 }, nameof(GeminiExecArgs.EnabledFeatures));
        ThrowIfUnsupported(args.DisabledFeatures is { Count: > 0 }, nameof(GeminiExecArgs.DisabledFeatures));
        ThrowIfUnsupported(args.SkipGitRepoCheck, nameof(GeminiExecArgs.SkipGitRepoCheck));
        ThrowIfUnsupported(!string.IsNullOrWhiteSpace(args.OutputSchemaFile), nameof(GeminiExecArgs.OutputSchemaFile));
        ThrowIfUnsupported(args.ModelReasoningEffort.HasValue, nameof(GeminiExecArgs.ModelReasoningEffort));
        ThrowIfUnsupported(args.NetworkAccessEnabled.HasValue, nameof(GeminiExecArgs.NetworkAccessEnabled));
        ThrowIfUnsupported(args.WebSearchMode.HasValue, nameof(GeminiExecArgs.WebSearchMode));
        ThrowIfUnsupported(args.WebSearchEnabled.HasValue, nameof(GeminiExecArgs.WebSearchEnabled));

        if (_configOverrides is not null)
        {
            throw new NotSupportedException(
                $"{UnsupportedHeadlessOptionsMessagePrefix} '{nameof(GeminiExec)}.{nameof(_configOverrides)}'.");
        }
    }

    private static void ThrowIfUnsupported(bool isUnsupported, string optionName)
    {
        if (!isUnsupported)
        {
            return;
        }

        throw new NotSupportedException($"{UnsupportedHeadlessOptionsMessagePrefix} '{optionName}'.");
    }
}

internal sealed record GeminiProcessInvocation(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string? WorkingDirectory,
    TimeSpan ProcessTerminationTimeout);

internal interface IGeminiProcessRunner
{
    IAsyncEnumerable<string> RunAsync(
        GeminiProcessInvocation invocation,
        ILogger logger,
        CancellationToken cancellationToken);
}

internal sealed class DefaultGeminiProcessRunner : IGeminiProcessRunner
{
    private const string ProcessTerminationUnconfirmedMessage = "Could not confirm that the Gemini CLI process exited after termination was requested.";
    private const string StderrTerminationUnconfirmedMessage = "Could not confirm that the Gemini CLI stderr stream closed within the configured process termination timeout.";

    public async IAsyncEnumerable<string> RunAsync(
        GeminiProcessInvocation invocation,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(invocation.ExecutablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(invocation.WorkingDirectory))
        {
            startInfo.WorkingDirectory = invocation.WorkingDirectory;
        }

        startInfo.Environment.Clear();
        foreach (var (key, value) in invocation.Environment)
        {
            startInfo.Environment[key] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        Task<string>? standardErrorTask = null;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start Gemini CLI at '{invocation.ExecutablePath}'");
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to start Gemini CLI at '{invocation.ExecutablePath}'", exception);
        }

        try
        {
            standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            process.StandardInput.Close();

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await ThrowIfExitedWithFailureAsync(process, standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                    throw;
                }

                if (line is null)
                {
                    break;
                }

                yield return line;
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await ThrowIfExitedWithFailureAsync(process, standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                throw;
            }

            var standardError = await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Gemini Exec exited with code {process.ExitCode}: {standardError}");
            }
        }
        finally
        {
            await EnsureProcessStoppedAsync(process, invocation.ExecutablePath, invocation.ProcessTerminationTimeout, logger).ConfigureAwait(false);
            if (standardErrorTask is not null)
            {
                await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
            }
        }
    }

    private static async Task ThrowIfExitedWithFailureAsync(
        Process process,
        Task<string> standardErrorTask,
        TimeSpan processTerminationTimeout)
    {
        if (!process.HasExited || process.ExitCode == 0)
        {
            return;
        }

        var standardError = await ReadStandardErrorAsync(standardErrorTask, processTerminationTimeout).ConfigureAwait(false);
        throw new InvalidOperationException($"Gemini Exec exited with code {process.ExitCode}: {standardError}");
    }

    private static async Task<string> ReadStandardErrorAsync(Task<string> standardErrorTask, TimeSpan timeout)
    {
        try
        {
            return await standardErrorTask.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(StderrTerminationUnconfirmedMessage, exception);
        }
    }

    private static async Task EnsureProcessStoppedAsync(
        Process process,
        string executablePath,
        TimeSpan processTerminationTimeout,
        ILogger logger)
    {
        if (process.HasExited)
        {
            return;
        }

        Exception? killException = null;
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
        {
            Logging.GeminiExecLog.ProcessKillFailed(logger, executablePath);
            killException = exception;
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(processTerminationTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            Exception failure = killException is null ? exception : new AggregateException(killException, exception);
            Logging.GeminiExecLog.ProcessKillFailed(logger, executablePath);
            throw new InvalidOperationException(ProcessTerminationUnconfirmedMessage, failure);
        }
    }
}

internal static class CliValueExtensions
{
    private const string ApprovalDefault = "default";
    private const string ApprovalAutoEdit = "auto_edit";
    private const string ApprovalYolo = "yolo";
    private const string ApprovalPlan = "plan";

    private const string OssProviderLmStudio = "lmstudio";
    private const string OssProviderOllama = "ollama";

    private const string ColorAlways = "always";
    private const string ColorNever = "never";
    private const string ColorAuto = "auto";

    public static string ToCliValue(this SandboxMode mode)
    {
        throw new NotSupportedException($"Sandbox mode '{mode}' is represented as a boolean flag in Gemini CLI 0.34.0.");
    }

    public static string ToCliValue(this ModelReasoningEffort effort)
    {
        throw new NotSupportedException(
            $"Model reasoning effort '{effort}' is not exposed as a Gemini CLI 0.34.0 headless flag.");
    }

    public static string ToCliValue(this WebSearchMode mode)
    {
        throw new NotSupportedException($"Web search mode '{mode}' is not exposed as a Gemini CLI 0.34.0 headless flag.");
    }

    public static string ToCliValue(this ApprovalMode mode)
    {
        return mode switch
        {
            ApprovalMode.Default => ApprovalDefault,
            ApprovalMode.AutoEdit => ApprovalAutoEdit,
            ApprovalMode.Yolo => ApprovalYolo,
            ApprovalMode.Plan => ApprovalPlan,
            ApprovalMode.OnRequest => ApprovalDefault,
            ApprovalMode.Never => throw new NotSupportedException("Gemini CLI 0.34.0 does not provide a 'never' approval mode."),
            ApprovalMode.OnFailure => throw new NotSupportedException("Gemini CLI 0.34.0 does not provide an 'on-failure' approval mode."),
            ApprovalMode.Untrusted => throw new NotSupportedException("Gemini CLI 0.34.0 does not provide an 'untrusted' approval mode."),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static string ToCliValue(this OssProvider provider)
    {
        return provider switch
        {
            OssProvider.LmStudio => OssProviderLmStudio,
            OssProvider.Ollama => OssProviderOllama,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
    }

    public static string ToCliValue(this ExecOutputColor color)
    {
        return color switch
        {
            ExecOutputColor.Always => ColorAlways,
            ExecOutputColor.Never => ColorNever,
            ExecOutputColor.Auto => ColorAuto,
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
        };
    }
}
