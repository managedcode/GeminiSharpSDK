using System.Text.Json;
using ManagedCode.GeminiSharpSDK.Client;
using ManagedCode.GeminiSharpSDK.Configuration;
using ManagedCode.GeminiSharpSDK.Internal;

namespace ManagedCode.GeminiSharpSDK.Tests.Shared;

internal static class RealGeminiTestSupport
{
    private const string ModelEnvVar = "GEMINI_TEST_MODEL";
    private const string GeminiDirectoryName = ".gemini";
    private const string TmpDirectoryName = "tmp";
    private const string SessionFileSearchPattern = "session-*.json";
    private const string ModelPropertyName = "model";

    public static RealGeminiTestSettings GetRequiredSettings()
    {
        if (!IsGeminiAvailable())
        {
            throw new InvalidOperationException(
                "Real Gemini tests require the gemini CLI. Install it first and ensure it is available in PATH.");
        }

        return new RealGeminiTestSettings(ResolveModel());
    }

    public static GeminiClient CreateClient()
    {
        return new GeminiClient(new GeminiOptions());
    }

    private static string ResolveModel()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ModelEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var fromConfig = TryReadModelFromGeminiConfig();
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig;
        }

        var fromRecentSession = TryReadModelFromRecentSession();
        if (!string.IsNullOrWhiteSpace(fromRecentSession))
        {
            return fromRecentSession;
        }

        throw new InvalidOperationException(
            $"Real Gemini tests require a model. Set {ModelEnvVar}, define model in '~/.gemini/config.toml', or ensure your Gemini CLI profile has recent session history.");
    }

    private static string? TryReadModelFromGeminiConfig()
    {
        var configPath = GetGeminiConfigPath();
        if (configPath is null || !File.Exists(configPath))
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines(configPath))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("model", StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = trimmed[..separatorIndex].Trim();
                if (!string.Equals(key, "model", StringComparison.Ordinal))
                {
                    continue;
                }

                var rawValue = trimmed[(separatorIndex + 1)..].Trim();
                if (rawValue.Length >= 2
                    && rawValue.StartsWith('"')
                    && rawValue.EndsWith('"'))
                {
                    rawValue = rawValue[1..^1];
                }

                return string.IsNullOrWhiteSpace(rawValue)
                    ? null
                    : rawValue;
            }
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Failed to read Gemini config at '{configPath}'.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"Failed to read Gemini config at '{configPath}'.", exception);
        }

        return null;
    }

    private static string? GetGeminiConfigPath()
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            return null;
        }

        return Path.Combine(homeDirectory, ".gemini", "config.toml");
    }

    private static string? TryReadModelFromRecentSession()
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            return null;
        }

        var sessionRoot = Path.Combine(homeDirectory, GeminiDirectoryName, TmpDirectoryName);
        if (!Directory.Exists(sessionRoot))
        {
            return null;
        }

        string? fallbackModel = null;
        var sessionFiles = Directory
            .EnumerateFiles(sessionRoot, SessionFileSearchPattern, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc);

        foreach (var sessionFile in sessionFiles)
        {
            var model = TryReadModelFromSessionFile(sessionFile);
            if (!string.IsNullOrWhiteSpace(model))
            {
                if (IsPreferredRecentModel(model))
                {
                    return model;
                }

                fallbackModel ??= model;
            }
        }

        return fallbackModel;
    }

    private static string? TryReadModelFromSessionFile(string sessionFile)
    {
        try
        {
            using var stream = File.OpenRead(sessionFile);
            using var document = JsonDocument.Parse(stream);
            return FindFirstModel(document.RootElement);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindFirstModel(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(ModelPropertyName, out var modelElement)
                && modelElement.ValueKind == JsonValueKind.String)
            {
                var model = modelElement.GetString();
                if (!string.IsNullOrWhiteSpace(model))
                {
                    return model;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nestedModel = FindFirstModel(property.Value);
                if (!string.IsNullOrWhiteSpace(nestedModel))
                {
                    return nestedModel;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nestedModel = FindFirstModel(item);
                if (!string.IsNullOrWhiteSpace(nestedModel))
                {
                    return nestedModel;
                }
            }
        }

        return null;
    }

    private static bool IsPreferredRecentModel(string model)
    {
        return model.Contains("flash", StringComparison.OrdinalIgnoreCase)
               || model.StartsWith("auto-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeminiAvailable()
    {
        var resolvedPath = GeminiCliLocator.FindGeminiPath(null);
        if (Path.IsPathRooted(resolvedPath))
        {
            return File.Exists(resolvedPath);
        }

        return GeminiCliLocator.TryResolvePathExecutable(
            Environment.GetEnvironmentVariable("PATH"),
            OperatingSystem.IsWindows(),
            out _);
    }
}

internal sealed record RealGeminiTestSettings(string Model);
