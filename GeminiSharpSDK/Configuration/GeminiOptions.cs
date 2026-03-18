using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ManagedCode.GeminiSharpSDK.Configuration;

public sealed record GeminiOptions
{
    public string? GeminiExecutablePath { get; init; }

    public string? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public JsonObject? Config { get; init; }

    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    public ILogger? Logger { get; init; }
}
