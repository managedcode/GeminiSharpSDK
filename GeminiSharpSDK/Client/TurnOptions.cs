using ManagedCode.GeminiSharpSDK.Models;

namespace ManagedCode.GeminiSharpSDK.Client;

public sealed record TurnOptions
{
    public StructuredOutputSchema? OutputSchema { get; init; }

    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
}
