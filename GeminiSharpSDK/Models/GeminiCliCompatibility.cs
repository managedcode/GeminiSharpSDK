namespace ManagedCode.GeminiSharpSDK.Models;

/// <summary>Identifies the upstream Gemini CLI version targeted by this SDK release.</summary>
public static class GeminiCliCompatibility
{
    /// <summary>The upstream CLI version selected for managed installation and release verification; other installed versions are allowed.</summary>
    public const string TargetVersion = "0.65.0";
}
