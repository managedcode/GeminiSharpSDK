using ManagedCode.GeminiSharpSDK.Client;
using ManagedCode.GeminiSharpSDK.Configuration;

namespace ManagedCode.GeminiSharpSDK.Extensions.AI;

public sealed record GeminiChatClientOptions
{
    public GeminiOptions? GeminiOptions { get; set; }
    public string? DefaultModel { get; set; }
    public ThreadOptions? DefaultThreadOptions { get; set; }
}
