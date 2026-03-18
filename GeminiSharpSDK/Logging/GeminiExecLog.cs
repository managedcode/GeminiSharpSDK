using Microsoft.Extensions.Logging;

namespace ManagedCode.GeminiSharpSDK.Logging;

internal static partial class GeminiExecLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Starting Gemini CLI '{ExecutablePath}' with {ArgumentCount} arguments.")]
    public static partial void Starting(ILogger logger, string executablePath, int argumentCount);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Gemini CLI execution was cancelled.")]
    public static partial void Cancelled(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Gemini CLI execution failed.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Gemini CLI finished successfully with {LineCount} output lines.")]
    public static partial void Completed(ILogger logger, int lineCount);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Failed to terminate Gemini CLI process '{ExecutablePath}' during cleanup.")]
    public static partial void ProcessKillFailed(ILogger logger, string executablePath, Exception exception);
}
