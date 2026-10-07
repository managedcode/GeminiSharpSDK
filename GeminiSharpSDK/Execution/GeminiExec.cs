using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json.Nodes;
using ManagedCode.GeminiSharpSDK.Client;
using ManagedCode.GeminiSharpSDK.Configuration;
using ManagedCode.GeminiSharpSDK.Internal;
using ManagedCode.GeminiSharpSDK.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagedCode.GeminiSharpSDK.Execution;

public sealed class GeminiExec
{
    private const string OutputFormatFlag = "--output-format";
    private const string StreamJsonOutputFormat = "stream-json";
    private const string ModelFlag = "--model";
    private const string ResumeFlag = "--resume";
    private const string SandboxFlag = "--sandbox";
    private const string IncludeDirectoriesFlag = "--include-directories";
    private const string ApprovalModeFlag = "--approval-mode";
    private const string UnsupportedHeadlessOptionsMessagePrefix =
        "Gemini CLI headless mode does not expose the requested SDK option";
    private const string UnsupportedReadOnlySandboxMessage =
        "Gemini CLI headless mode cannot guarantee the SDK's read-only sandbox mode.";

    private const string InternalOriginatorEnv = "GEMINI_INTERNAL_ORIGINATOR_OVERRIDE";
    private const string CSharpSdkOriginator = "gemini_sdk_csharp";
    private const string OpenAiBaseUrlEnv = "OPENAI_BASE_URL";
    private const string GeminiApiKeyEnv = "GEMINI_API_KEY";
    private const string PathEnvironmentVariable = "PATH";
    private const string ProcessTerminationTimeoutMustBePositiveMessage = "Process termination timeout must be positive.";

    private readonly string _executablePath;
    private readonly IReadOnlyDictionary<string, string>? _environmentOverride;
    private readonly bool _inheritEnvironmentVariables;
    private readonly JsonObject? _configOverrides;
    private readonly IGeminiProcessRunner _processRunner;
    private readonly ILogger _logger;
    private readonly TimeSpan _processTerminationTimeout;
    private readonly int _maximumProcessOutputCharacters;
    private readonly CliLaunchCommand _launchCommand;

    public GeminiExec(
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? environmentOverride = null,
        JsonObject? configOverrides = null,
        ILogger? logger = null)
        : this(executablePath, environmentOverride, configOverrides, null, logger, GeminiOptions.DefaultProcessTerminationTimeout, null)
    {
    }

    public GeminiExec(
        TimeSpan processTerminationTimeout,
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? environmentOverride = null,
        JsonObject? configOverrides = null,
        ILogger? logger = null)
        : this(executablePath, environmentOverride, configOverrides, null, logger, processTerminationTimeout, null)
    {
    }

    internal GeminiExec(
        string? executablePath,
        IReadOnlyDictionary<string, string>? environmentOverride,
        JsonObject? configOverrides,
        IGeminiProcessRunner? processRunner,
        ILogger? logger = null,
        TimeSpan? processTerminationTimeout = null,
        bool? inheritEnvironmentVariables = null,
        int maximumProcessOutputCharacters = GeminiOptions.DefaultMaximumProcessOutputCharacters,
        CliLaunchCommand? launchCommand = null)
    {
        var resolvedTerminationTimeout = processTerminationTimeout ?? GeminiOptions.DefaultProcessTerminationTimeout;
        if (resolvedTerminationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processTerminationTimeout), resolvedTerminationTimeout, ProcessTerminationTimeoutMustBePositiveMessage);
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumProcessOutputCharacters);

        var inheritsEnvironment = inheritEnvironmentVariables ?? environmentOverride is null;
        _launchCommand = launchCommand ?? CliLaunchCommandResolver.Resolve(executablePath,
            ResolvePathVariable(environmentOverride, inheritsEnvironment), GeminiOptions.DefaultCliMetadataMaximumFileCharacters);
        _executablePath = _launchCommand.ExecutablePath;
        _environmentOverride = environmentOverride;
        _inheritEnvironmentVariables = inheritEnvironmentVariables ?? environmentOverride is null;
        _configOverrides = configOverrides;
        _processRunner = processRunner ?? new DefaultGeminiProcessRunner();
        _logger = logger ?? NullLogger.Instance;
        _processTerminationTimeout = resolvedTerminationTimeout;
        _maximumProcessOutputCharacters = maximumProcessOutputCharacters;
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
            _processTerminationTimeout)
        {
            MaximumProcessOutputCharacters = _maximumProcessOutputCharacters,
            Input = args.Input,
            PrefixArguments = _launchCommand.PrefixArguments,
        };

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
            OutputFormatFlag,
            StreamJsonOutputFormat,
        };

        if (!string.IsNullOrWhiteSpace(args.Model))
        {
            commandArgs.Add(ModelFlag);
            commandArgs.Add(args.Model);
        }

        if (args.SandboxMode == SandboxMode.WorkspaceWrite)
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

        if (_inheritEnvironmentVariables)
        {
            foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            {
                if (variable.Key is string key && variable.Value is string value)
                {
                    environment[key] = value;
                }
            }
        }

        if (_environmentOverride is not null)
        {
            foreach (var (key, value) in _environmentOverride)
            {
                environment[key] = value;
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

    private static string? ResolvePathVariable(IReadOnlyDictionary<string, string>? environmentOverride, bool inheritEnvironmentVariables)
    {
        if (environmentOverride is not null)
        {
            foreach (var (key, value) in environmentOverride)
            {
                if (string.Equals(key, PathEnvironmentVariable, OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
                {
                    return value;
                }
            }
        }

        return inheritEnvironmentVariables ? Environment.GetEnvironmentVariable(PathEnvironmentVariable) : null;
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
        if (args.SandboxMode == SandboxMode.ReadOnly)
        {
            throw new NotSupportedException(UnsupportedReadOnlySandboxMessage);
        }

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
    TimeSpan ProcessTerminationTimeout)
{
    public IReadOnlyList<string> PrefixArguments { get; init; } = Array.Empty<string>();
    public int MaximumProcessOutputCharacters { get; init; } = GeminiOptions.DefaultMaximumProcessOutputCharacters;

    public string Input { get; init; } = string.Empty;

    public Action? StandardErrorReaderCompleted { get; init; }

    public Action? StandardOutputReadCompleted { get; init; }

    public Action? StandardErrorOutputLimitExceeded { get; init; }
}

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
    private const string ProcessOutputLimitExceededMessage = "Gemini CLI process exceeded the configured output limit.";
    private const string StandardOutputTerminationUnconfirmedMessage = "Gemini CLI standard output did not close within the configured time limit.";
    private const string ProcessAndReaderCleanupUnconfirmedMessage = "Gemini CLI process and output cleanup could not be confirmed.";
    private const int ProcessOutputBufferCharacters = 4096;
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

        foreach (var argument in invocation.PrefixArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

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
        Task<BoundedProcessOutput>? standardErrorTask = null;
        Task<string?>? standardOutputReadTask = null;
        BoundedProcessOutputReader? standardOutput = null;
        Task? standardInputWriteTask = null;
        using var outputCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var cancellationSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancellationSignal);
        var standardErrorLimitExceeded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? cleanupFailure = null;
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
            standardErrorTask = ReadBoundedProcessOutputAsync(process.StandardError,
                invocation.MaximumProcessOutputCharacters, () =>
                {
                    standardErrorLimitExceeded.TrySetResult(true);
                    invocation.StandardErrorOutputLimitExceeded?.Invoke();
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception)
                    {
                        // The bounded cleanup path below reports whether process exit was confirmed.
                    }

                    outputCancellation.Cancel();
                }, CancellationToken.None, invocation.StandardErrorReaderCompleted);
            standardOutput = new BoundedProcessOutputReader(process.StandardOutput,
                invocation.MaximumProcessOutputCharacters, invocation.StandardOutputReadCompleted);
            standardOutputReadTask = standardOutput.ReadLineAsync(CancellationToken.None).AsTask();
            standardInputWriteTask = WriteStandardInputAsync(process.StandardInput, invocation.Input, outputCancellation.Token);
            while (true)
            {
                var readLineTask = standardOutputReadTask!;
                var completedTask = standardInputWriteTask is null
                    ? await Task.WhenAny(readLineTask, standardErrorLimitExceeded.Task, cancellationSignal.Task).ConfigureAwait(false)
                    : await Task.WhenAny(readLineTask, standardErrorLimitExceeded.Task, standardInputWriteTask, cancellationSignal.Task).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested && process.HasExited)
                {
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    var exitedStandardError = await ReadStandardErrorAsync(
                        standardErrorTask!, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                    if (process.ExitCode != 0)
                    {
                        throw CliExecutionFailureException.FromProcessExit(process.ExitCode,
                            $"Gemini Exec exited with code {process.ExitCode}: {exitedStandardError.Text}");
                    }
                }

                if (completedTask == cancellationSignal.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (completedTask == standardErrorLimitExceeded.Task || standardErrorLimitExceeded.Task.IsCompleted)
                {
                    try
                    {
                        await readLineTask.WaitAsync(invocation.ProcessTerminationTimeout, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // The stderr overflow handler canceled the sibling reader after requesting process exit.
                    }
                    catch (TimeoutException)
                    {
                        // Closing the redirected stream in finally gets a second bounded chance to settle the read.
                    }

                    await EnsureProcessStoppedAsync(process, invocation.ExecutablePath,
                        invocation.ProcessTerminationTimeout, logger).ConfigureAwait(false);
                    throw new InvalidOperationException(ProcessOutputLimitExceededMessage);
                }

                if (standardInputWriteTask is not null && completedTask == standardInputWriteTask)
                {
                    try
                    {
                        await AwaitStandardInputWriteAsync(
                            standardInputWriteTask,
                            process,
                            standardErrorTask,
                            invocation.ProcessTerminationTimeout,
                            cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        standardInputWriteTask = null;
                    }

                    continue;
                }

                string? line;
                try
                {
                    line = await readLineTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    standardOutputReadTask = null;
                    await ThrowIfExitedWithFailureAsync(process, standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                    throw;
                }

                standardOutputReadTask = null;
                if (line is null)
                {
                    if (standardInputWriteTask is not null)
                    {
                        await AwaitStandardInputWriteAsync(
                            standardInputWriteTask,
                            process,
                            standardErrorTask,
                            invocation.ProcessTerminationTimeout,
                            cancellationToken).ConfigureAwait(false);
                        standardInputWriteTask = null;
                    }

                    break;
                }

                yield return line;
                standardOutputReadTask = standardOutput.ReadLineAsync(CancellationToken.None).AsTask();
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
                throw CliExecutionFailureException.FromProcessExit(process.ExitCode,
                    $"Gemini Exec exited with code {process.ExitCode}: {standardError.Text}");
            }
        }
        finally
        {
            var readerFailures = new List<Exception>(5);
            Exception? processExitFailure = null;
            try
            {
                await EnsureProcessStoppedAsync(process, invocation.ExecutablePath,
                    invocation.ProcessTerminationTimeout, logger).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                processExitFailure = exception;
            }

            try
            {
                process.StandardInput.BaseStream.Dispose();
            }
            catch (Exception exception)
            {
                readerFailures.Add(exception);
            }

            if (standardInputWriteTask is not null)
            {
                try
                {
                    await standardInputWriteTask.WaitAsync(invocation.ProcessTerminationTimeout, CancellationToken.None).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (outputCancellation.IsCancellationRequested &&
                                                        standardInputWriteTask.IsCanceled)
                {
                    // Owned I/O cancellation ends the bounded stdin pump after root termination was requested.
                }
                catch (IOException) when (standardErrorLimitExceeded.Task.IsCompleted &&
                                          standardInputWriteTask.IsFaulted &&
                                          standardInputWriteTask.Exception?.GetBaseException() is IOException)
                {
                    // Stderr overflow already owns the visible output-limit failure.
                }
                catch (IOException) when (process.HasExited && process.ExitCode != 0 &&
                                          standardInputWriteTask.IsFaulted &&
                                          standardInputWriteTask.Exception?.GetBaseException() is IOException)
                {
                    // The confirmed nonzero root exit owns this completed stdin broken pipe.
                }
                catch (Exception exception)
                {
                    readerFailures.Add(exception);
                }
            }

            if (standardOutput is not null)
            {
                var drainResult = await DrainStandardOutputToEofAsync(standardOutput, standardOutputReadTask,
                    invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                standardOutputReadTask = drainResult.PendingRead;
                if (drainResult.Failure is not null)
                {
                    readerFailures.Add(drainResult.Failure);
                }
            }

            if (standardErrorTask is not null)
            {
                try
                {
                    await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    readerFailures.Add(exception);
                }
            }

            try
            {
                outputCancellation.Cancel();
            }
            catch (Exception exception)
            {
                readerFailures.Add(exception);
            }

            try
            {
                process.StandardOutput.Dispose();
            }
            catch (Exception exception)
            {
                readerFailures.Add(exception);
            }

            try
            {
                process.StandardError.Dispose();
            }
            catch (Exception exception)
            {
                readerFailures.Add(exception);
            }
            var standardOutputFailure = await ObserveStandardOutputReadAsync(
                standardOutputReadTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
            var standardOutputLimitFailureIsExpected = standardOutputReadTask is { IsFaulted: true } &&
                standardOutputReadTask.Exception?.GetBaseException() is InvalidOperationException standardOutputLimitException &&
                string.Equals(standardOutputLimitException.Message, ProcessOutputLimitExceededMessage, StringComparison.Ordinal);
            Exception? standardErrorFailure = null;
            if (standardErrorTask is not null)
            {
                try
                {
                    await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    standardErrorFailure = exception;
                }
            }

            if (standardOutputFailure is not null && !standardOutputLimitFailureIsExpected)
            {
                readerFailures.Add(standardOutputFailure);
            }

            var standardErrorLimitFailureIsExpected = standardErrorTask is { IsFaulted: true } &&
                standardErrorTask.Exception?.GetBaseException() is InvalidOperationException standardErrorLimitException &&
                string.Equals(standardErrorLimitException.Message, ProcessOutputLimitExceededMessage, StringComparison.Ordinal);
            if (standardErrorFailure is not null && !standardErrorLimitFailureIsExpected)
            {
                readerFailures.Add(standardErrorFailure);
            }

            if (processExitFailure is not null && readerFailures.Count > 0)
            {
                cleanupFailure = new InvalidOperationException(
                    processExitFailure.Message,
                    new AggregateException(new[] { processExitFailure }.Concat(readerFailures)));
            }
            else if (processExitFailure is not null)
            {
                cleanupFailure = processExitFailure;
            }
            else if (readerFailures.Count == 1)
            {
                cleanupFailure = readerFailures[0];
            }
            else if (readerFailures.Count > 1)
            {
                cleanupFailure = new InvalidOperationException(
                    ProcessAndReaderCleanupUnconfirmedMessage,
                    new AggregateException(readerFailures));
            }

            if (cleanupFailure is not null)
            {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
        }
    }

    private static async Task<Exception?> ObserveStandardOutputReadAsync(
        Task<string?>? standardOutputReadTask,
        TimeSpan timeout)
    {
        if (standardOutputReadTask is null)
        {
            return null;
        }

        try
        {
            await standardOutputReadTask.WaitAsync(timeout).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (TimeoutException exception)
        {
            return new InvalidOperationException(StandardOutputTerminationUnconfirmedMessage, exception);
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<StandardOutputDrainResult> DrainStandardOutputToEofAsync(
        BoundedProcessOutputReader reader,
        Task<string?>? pendingRead,
        TimeSpan timeout)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        var currentRead = pendingRead;
        try
        {
            while (true)
            {
                currentRead ??= reader.ReadLineAsync(CancellationToken.None).AsTask();
                var line = await currentRead.WaitAsync(timeoutCancellation.Token).ConfigureAwait(false);
                currentRead = null;
                if (line is null)
                {
                    return new StandardOutputDrainResult(null, null);
                }
            }
        }
        catch (OperationCanceledException exception) when (timeoutCancellation.IsCancellationRequested)
        {
            return new StandardOutputDrainResult(currentRead,
                new InvalidOperationException(StandardOutputTerminationUnconfirmedMessage, exception));
        }
        catch (Exception exception)
        {
            return new StandardOutputDrainResult(currentRead, exception);
        }
    }

    private static async Task ThrowIfExitedWithFailureAsync(
        Process process,
        Task<BoundedProcessOutput> standardErrorTask,
        TimeSpan processTerminationTimeout)
    {
        if (!process.HasExited || process.ExitCode == 0)
        {
            return;
        }

        var standardError = await ReadStandardErrorAsync(standardErrorTask, processTerminationTimeout).ConfigureAwait(false);
        throw CliExecutionFailureException.FromProcessExit(process.ExitCode,
            $"Gemini Exec exited with code {process.ExitCode}: {standardError.Text}");
    }

    private static async Task<BoundedProcessOutput> ReadStandardErrorAsync(Task<BoundedProcessOutput> standardErrorTask, TimeSpan timeout)
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

    private static async Task WriteStandardInputAsync(
        StreamWriter standardInput,
        string input,
        CancellationToken cancellationToken)
    {
        await standardInput.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
        await standardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        standardInput.Close();
    }

    private static async Task AwaitStandardInputWriteAsync(
        Task standardInputWriteTask,
        Process process,
        Task<BoundedProcessOutput> standardErrorTask,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await standardInputWriteTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ThrowIfExitedWithFailureAsync(process, standardErrorTask, timeout).ConfigureAwait(false);
            throw;
        }
        catch (IOException) when (standardInputWriteTask.IsFaulted &&
                                  standardInputWriteTask.Exception?.GetBaseException() is IOException)
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(timeout, CancellationToken.None)
                .ConfigureAwait(false);
            await ThrowIfExitedWithFailureAsync(process, standardErrorTask, timeout).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (process.HasExited)
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            var standardError = await ReadStandardErrorAsync(standardErrorTask, timeout).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw CliExecutionFailureException.FromProcessExit(process.ExitCode,
                    $"Gemini Exec exited with code {process.ExitCode}: {standardError.Text}");
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    private static async Task<BoundedProcessOutput> ReadBoundedProcessOutputAsync(
        TextReader reader,
        int maximumCharacters,
        Action onLimitExceeded,
        CancellationToken cancellationToken,
        Action? onCompleted = null)
    {
        var output = new StringBuilder(Math.Min(maximumCharacters, ProcessOutputBufferCharacters));
        var buffer = new char[ProcessOutputBufferCharacters];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            {
                var append = Math.Min(maximumCharacters - output.Length, read);
                if (append > 0)
                {
                    output.Append(buffer, 0, append);
                }

                if (append < read)
                {
                    onLimitExceeded();
                    throw new InvalidOperationException(ProcessOutputLimitExceededMessage);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Process cancellation is handled by the owner and the partially captured text stays bounded.
        }
        finally
        {
            onCompleted?.Invoke();
        }

        return new BoundedProcessOutput(output.ToString());
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

internal sealed record BoundedProcessOutput(string Text);

internal sealed record StandardOutputDrainResult(Task<string?>? PendingRead, Exception? Failure);

internal sealed class BoundedProcessOutputReader(
    TextReader reader,
    int maximumCharacters,
    Action? onReadCompleted = null)
{
    private const int BufferCharacters = 4096;
    private const string OutputLimitExceededMessage = "Gemini CLI process exceeded the configured output limit.";
    private readonly char[] _buffer = new char[BufferCharacters];
    private readonly StringBuilder _line = new(Math.Min(maximumCharacters, BufferCharacters));
    private int _bufferCount;
    private int _bufferIndex;
    private int _charactersRead;
    private bool _endOfStream;

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_bufferIndex >= _bufferCount)
            {
                if (_endOfStream)
                {
                    return TakeFinalLine();
                }

                _bufferCount = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                _bufferIndex = 0;
                if (_bufferCount == 0)
                {
                    _endOfStream = true;
                    onReadCompleted?.Invoke();
                    return TakeFinalLine();
                }
            }

            var character = _buffer[_bufferIndex++];
            if (_charactersRead >= maximumCharacters)
            {
                throw new InvalidOperationException(OutputLimitExceededMessage);
            }

            _charactersRead++;
            if (character == '\n')
            {
                if (_line.Length > 0 && _line[^1] == '\r')
                {
                    _line.Length--;
                }

                var result = _line.ToString();
                _line.Clear();
                return result;
            }

            _line.Append(character);
        }
    }

    private string? TakeFinalLine()
    {
        if (_line.Length == 0)
        {
            return null;
        }

        var result = _line.ToString();
        _line.Clear();
        return result;
    }
}

internal static class CliValueExtensions
{
    private const string UnsupportedSandboxValueMessage =
        "Gemini CLI represents sandbox modes as execution flags rather than string values.";
    private const string UnsupportedReasoningEffortMessage =
        "The installed Gemini CLI headless mode does not expose model reasoning effort.";
    private const string UnsupportedWebSearchModeMessage =
        "The installed Gemini CLI headless mode does not expose web search mode.";
    private const string UnsupportedApprovalModeMessage =
        "The installed Gemini CLI does not provide this approval mode.";
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
        throw new NotSupportedException(UnsupportedSandboxValueMessage);
    }

    public static string ToCliValue(this ModelReasoningEffort effort)
    {
        throw new NotSupportedException(UnsupportedReasoningEffortMessage);
    }

    public static string ToCliValue(this WebSearchMode mode)
    {
        throw new NotSupportedException(UnsupportedWebSearchModeMessage);
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
            ApprovalMode.Never => throw new NotSupportedException(UnsupportedApprovalModeMessage),
            ApprovalMode.OnFailure => throw new NotSupportedException(UnsupportedApprovalModeMessage),
            ApprovalMode.Untrusted => throw new NotSupportedException(UnsupportedApprovalModeMessage),
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
