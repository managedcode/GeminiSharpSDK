using System.Text.Json.Nodes;
using ManagedCode.GeminiSharpSDK.Internal;
using Microsoft.Extensions.Logging;

namespace ManagedCode.GeminiSharpSDK.Configuration;

public sealed record GeminiOptions
{
    private const string PathEnvironmentVariable = "PATH";
    public static readonly TimeSpan DefaultProcessTerminationTimeout = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan DefaultCliMetadataProbeTimeout = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan DefaultCliMetadataProbeLeaseTimeout = TimeSpan.FromMinutes(2);

    public const int DefaultCliMetadataMaximumOutputCharacters = 65536;

    public const int DefaultCliMetadataMaximumFileCharacters = 1048576;

    public const int DefaultMaximumProcessOutputCharacters = 1048576;

    public string? GeminiExecutablePath { get; init; }

    public string? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public JsonObject? Config { get; init; }

    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    public bool? InheritEnvironmentVariables { get; init; }

    public TimeSpan CliMetadataProbeTimeout { get; init; } = DefaultCliMetadataProbeTimeout;

    public TimeSpan CliMetadataProbeLeaseTimeout { get; init; } = DefaultCliMetadataProbeLeaseTimeout;

    public int CliMetadataMaximumOutputCharacters { get; init; } = DefaultCliMetadataMaximumOutputCharacters;

    public int CliMetadataMaximumFileCharacters { get; init; } = DefaultCliMetadataMaximumFileCharacters;

    public TimeSpan ProcessTerminationTimeout { get; init; } = DefaultProcessTerminationTimeout;

    public int MaximumProcessOutputCharacters { get; init; } = DefaultMaximumProcessOutputCharacters;

    public ILogger? Logger { get; init; }

    /// <summary>Resolves the installed CLI to an executable and safe literal prefix arguments.</summary>
    public ManagedCode.GeminiSharpSDK.Models.CliLaunchCommand GetCliLaunchCommand()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CliMetadataMaximumFileCharacters);
        return CliLaunchCommandResolver.Resolve(GeminiExecutablePath, GetEffectivePath(), CliMetadataMaximumFileCharacters);
    }

    internal string? GetEffectivePath()
    {
        if (EnvironmentVariables is not null)
        {
            foreach (var (key, value) in EnvironmentVariables)
            {
                if (string.Equals(key, PathEnvironmentVariable, OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
                {
                    return value;
                }
            }
        }

        return (InheritEnvironmentVariables ?? EnvironmentVariables is null)
            ? Environment.GetEnvironmentVariable(PathEnvironmentVariable)
            : null;
    }
}
