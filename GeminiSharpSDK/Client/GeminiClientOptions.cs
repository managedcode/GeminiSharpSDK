using ManagedCode.GeminiSharpSDK.Configuration;

namespace ManagedCode.GeminiSharpSDK.Client;

public sealed record GeminiClientOptions
{
    public GeminiOptions? GeminiOptions { get; init; }

    public bool AutoStart { get; init; } = true;
}
