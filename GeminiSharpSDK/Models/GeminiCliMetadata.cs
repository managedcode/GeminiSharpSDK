namespace ManagedCode.GeminiSharpSDK.Models;

public sealed record GeminiCliMetadata(
    string InstalledVersion,
    string? DefaultModel,
    IReadOnlyList<GeminiModelMetadata> Models);

public sealed record GeminiCliUpdateStatus(
    string InstalledVersion,
    string? LatestVersion,
    bool IsUpdateAvailable,
    string? UpdateMessage,
    string? UpdateCommand);

public sealed record GeminiModelMetadata(
    string Slug,
    string DisplayName,
    string? Description,
    bool IsListed,
    bool IsApiSupported,
    IReadOnlyList<string> SupportedReasoningEfforts);
