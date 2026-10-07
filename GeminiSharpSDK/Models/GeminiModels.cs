namespace ManagedCode.GeminiSharpSDK.Models;

public static class GeminiModels
{
    public const string Gemini25Pro = "gemini-2.5-pro";
    public const string Gemini25Flash = "gemini-2.5-flash";
    public const string Gemini25FlashLite = "gemini-2.5-flash-lite";
    public const string Gemini35Flash = "gemini-3.5-flash";
    public const string Gemini38Flash = "gemini-3.8-flash";
    public const string Gemini3Flash = "gemini-3-flash";
    public const string Gemini31FlashLite = "gemini-3.1-flash-lite";
    public const string Gemini35FlashLite = "gemini-3.5-flash-lite";
    public const string Gemini3ProPreview = "gemini-3-pro-preview";
    public const string Gemini31ProPreview = "gemini-3.1-pro-preview";
    public const string Gemini31ProPreviewCustomTools = "gemini-3.1-pro-preview-customtools";
    public const string Gemini3FlashPreview = "gemini-3-flash-preview";
    public const string Gemini31FlashLitePreview = "gemini-3.1-flash-lite-preview";
    public const string GeminiEmbedding001 = "gemini-embedding-001";
    public const string GeminiGemma431BIt = "gemma-4-31b-it";
    public const string GeminiGemma426BA4BIt = "gemma-4-26b-a4b-it";
    public const string Gemini35FlashDefault = "gemini-3.5-flash";
    public const string Gemini3FlashSecondary = "gemini-3-flash";
    public const string Gemini31FlashLiteDefault = "gemini-3.1-flash-lite";
    public const string AutoGemini3 = "auto-gemini-3";
    public const string AutoGemini25 = "auto-gemini-2.5";
    public const string AliasAuto = "auto";
    public const string AliasPro = "pro";
    public const string AliasFlash = "flash";
    public const string AliasFlashLite = "flash-lite";

    public static IReadOnlyList<string> Known { get; } = Array.AsReadOnly<string>(
    [
        Gemini25Pro,
        Gemini25Flash,
        Gemini25FlashLite,
        Gemini35Flash,
        Gemini38Flash,
        Gemini3Flash,
        Gemini31FlashLite,
        Gemini35FlashLite,
        Gemini3ProPreview,
        Gemini31ProPreview,
        Gemini31ProPreviewCustomTools,
        Gemini3FlashPreview,
        AutoGemini3,
        AutoGemini25,
        AliasAuto,
        AliasPro,
        AliasFlash,
        AliasFlashLite,
        GeminiGemma431BIt,
        GeminiGemma426BA4BIt
    ]);
}
