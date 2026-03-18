using ManagedCode.GeminiSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.GeminiSharpSDK.Extensions.AI.Content;

public sealed class FileChangeContent : AIContent
{
    public required IReadOnlyList<FileUpdateChange> Changes { get; init; }
    public required PatchApplyStatus Status { get; init; }
}
