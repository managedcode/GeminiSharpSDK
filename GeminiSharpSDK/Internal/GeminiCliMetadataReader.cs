using System.Globalization;
using System.Text.Json;
using ManagedCode.GeminiSharpSDK.Configuration;
using ManagedCode.GeminiSharpSDK.Models;

namespace ManagedCode.GeminiSharpSDK.Internal;

internal static class GeminiCliMetadataReader
{
    private static readonly IReadOnlyDictionary<string, string> EmptyEnvironment = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(10);
    private const int DefaultMaximumOutputCharacters = 65536;
    private const string ProbeFailureMessage = "Gemini CLI metadata probe failed.";
    private const string InvalidProbeOutputMessage = "Gemini CLI metadata probe returned invalid output.";
    private const string VersionFlag = "--version";
    private const string CliVersionPrefix = "gemini-cli";
    private const string NpmExecutableName = "npm";
    private const string NpmWindowsScriptName = "npm.cmd";
    private const string WindowsCommandProcessorName = "cmd.exe";
    private const string WindowsCommandDisableAutoRunFlag = "/d";
    private const string WindowsCommandFlag = "/c";
    private const string NpmViewCommand = "view";
    private const string NpmPackageName = "@google/gemini-cli";
    private const string NpmVersionProperty = "version";
    private const string NpmSilentFlag = "--silent";
    private const string NpmGlobalUpdateCommand = "npm install --global @google/gemini-cli@latest";
    private const string BunGlobalUpdateCommand = "bun add --global @google/gemini-cli@latest";
    private const string NpmUserAgentEnvironmentVariable = "npm_config_user_agent";
    private const string BunInstallEnvironmentVariable = "BUN_INSTALL";
    private const string BunUserAgentPrefix = "bun/";
    private const string BunPathMarker = ".bun";
    private const string UpdateAvailableMessagePrefix = "Gemini CLI update is available:";
    private const string UpdateCheckFailedMessagePrefix = "Failed to check latest Gemini CLI version from npm:";

    private const string DotGeminiDirectory = ".gemini";
    private const string SettingsFileName = "settings.json";
    private const string GeminiCliHomeEnvironmentVariable = "GEMINI_CLI_HOME";
    private const string HomeEnvironmentVariable = "HOME";
    private const string UserProfileEnvironmentVariable = "USERPROFILE";
    private const string ModelPropertyName = "model";
    private const string ModelNamePropertyName = "name";

    private const string ModelsPropertyName = "models";
    private const string SlugPropertyName = "slug";
    private const string DisplayNamePropertyName = "display_name";
    private const string DescriptionPropertyName = "description";
    private const string VisibilityPropertyName = "visibility";
    private const string VisibilityListValue = "list";
    private const string SupportedInApiPropertyName = "supported_in_api";
    private const string SupportedReasoningLevelsPropertyName = "supported_reasoning_levels";
    private const string EffortPropertyName = "effort";

    private const string ModelKeyName = "model";
    private const char AssignmentSeparator = '=';
    private const char CommentPrefix = '#';
    private const char Quote = '"';
    private const char Apostrophe = '\'';
    private const char Escape = '\\';
    private const char SectionPrefix = '[';

    public static GeminiCliMetadata Read(string executablePath) =>
        Read(executablePath, EmptyEnvironment, true, DefaultProbeTimeout, DefaultMaximumOutputCharacters,
            GeminiOptions.DefaultCliMetadataMaximumFileCharacters);

    public static GeminiCliMetadata Read(
        string executablePath,
        IReadOnlyDictionary<string, string> environment,
        bool inheritEnvironmentVariables,
        TimeSpan probeTimeout,
        int maximumOutputCharacters,
        int maximumFileCharacters = GeminiOptions.DefaultCliMetadataMaximumFileCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(environment);

        var installedVersion = ReadInstalledVersion(executablePath, environment, inheritEnvironmentVariables,
            probeTimeout, maximumOutputCharacters);
        var homeDirectory = ResolveHomeDirectory(environment, inheritEnvironmentVariables);
        var defaultModel = string.IsNullOrWhiteSpace(homeDirectory)
            ? null
            : ReadDefaultModel(homeDirectory, maximumFileCharacters);
        var models = ReadKnownModels();
        return new GeminiCliMetadata(installedVersion, defaultModel, models);
    }

    public static GeminiCliUpdateStatus ReadUpdateStatus(string executablePath) =>
        ReadUpdateStatus(executablePath, EmptyEnvironment, true, DefaultProbeTimeout, DefaultMaximumOutputCharacters);

    public static GeminiCliUpdateStatus ReadUpdateStatus(
        string executablePath,
        IReadOnlyDictionary<string, string> environment,
        bool inheritEnvironmentVariables,
        TimeSpan probeTimeout,
        int maximumOutputCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(environment);

        var installedVersion = ReadInstalledVersion(executablePath, environment, inheritEnvironmentVariables,
            probeTimeout, maximumOutputCharacters);
        var probe = ProbeLatestPublishedVersion(environment, inheritEnvironmentVariables, probeTimeout,
            maximumOutputCharacters);
        if (!string.IsNullOrWhiteSpace(probe.ErrorMessage))
        {
            var failureMessage = $"{UpdateCheckFailedMessagePrefix} {probe.ErrorMessage}";
            return new GeminiCliUpdateStatus(installedVersion, null, false, failureMessage, null);
        }

        if (string.IsNullOrWhiteSpace(probe.LatestVersion))
        {
            return new GeminiCliUpdateStatus(installedVersion, null, false, null, null);
        }

        var isUpdateAvailable = IsNewerVersion(probe.LatestVersion, installedVersion);
        if (!isUpdateAvailable)
        {
            return new GeminiCliUpdateStatus(installedVersion, probe.LatestVersion, false, null, null);
        }

        var updateCommand = ResolveUpdateCommand(executablePath,
            environment.GetValueOrDefault(NpmUserAgentEnvironmentVariable),
            environment.GetValueOrDefault(BunInstallEnvironmentVariable),
            inheritEnvironmentVariables);
        var message =
            $"{UpdateAvailableMessagePrefix} installed {installedVersion}, latest {probe.LatestVersion}. Run '{updateCommand}'.";
        return new GeminiCliUpdateStatus(
            installedVersion,
            probe.LatestVersion,
            true,
            message,
            updateCommand);
    }

    internal static string ParseInstalledVersion(string versionOutput)
    {
        if (string.IsNullOrWhiteSpace(versionOutput))
        {
            throw new InvalidOperationException("Gemini CLI version output is empty.");
        }

        var trimmedOutput = versionOutput.Trim();
        var prefix = $"{CliVersionPrefix} ";
        if (!trimmedOutput.StartsWith(prefix, StringComparison.Ordinal))
        {
            return trimmedOutput;
        }

        var parts = trimmedOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new InvalidOperationException($"Failed to parse Gemini CLI version output: '{trimmedOutput}'.");
        }

        return parts[1];
    }

    internal static string? ParseLatestPublishedVersion(string npmOutput)
    {
        if (string.IsNullOrWhiteSpace(npmOutput))
        {
            return null;
        }

        var tokens = npmOutput.Split(
            [Environment.NewLine, "\n", "\r", "\t", " "],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var candidate = token.Trim(Quote, Apostrophe, ',');
            if (TryParseSemanticVersion(candidate, out var version))
            {
                return version.ToNormalizedString();
            }
        }

        return null;
    }

    internal static bool IsNewerVersion(string latestVersion, string installedVersion)
    {
        if (!TryParseSemanticVersion(latestVersion, out var latest))
        {
            return false;
        }

        if (!TryParseSemanticVersion(installedVersion, out var installed))
        {
            return false;
        }

        return CompareSemanticVersion(latest, installed) > 0;
    }

    internal static string ResolveUpdateCommand(
        string executablePath,
        string? npmUserAgent = null,
        string? bunInstallRoot = null,
        bool useProcessEnvironmentFallback = true)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return NpmGlobalUpdateCommand;
        }

        if (IsLikelyBunManagedPath(executablePath))
        {
            return BunGlobalUpdateCommand;
        }

        var resolvedUserAgent = string.IsNullOrWhiteSpace(npmUserAgent) && useProcessEnvironmentFallback
            ? Environment.GetEnvironmentVariable(NpmUserAgentEnvironmentVariable)
            : npmUserAgent;
        if (IsBunUserAgent(resolvedUserAgent))
        {
            return BunGlobalUpdateCommand;
        }

        var resolvedBunInstallRoot = string.IsNullOrWhiteSpace(bunInstallRoot) && useProcessEnvironmentFallback
            ? Environment.GetEnvironmentVariable(BunInstallEnvironmentVariable)
            : bunInstallRoot;
        if (IsPathUnderRoot(executablePath, resolvedBunInstallRoot))
        {
            return BunGlobalUpdateCommand;
        }

        return NpmGlobalUpdateCommand;
    }

    internal static string? ParseDefaultModelFromTomlLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var insideSection = false;
        foreach (var rawLine in lines)
        {
            var stripped = StripInlineComment(rawLine);
            if (string.IsNullOrWhiteSpace(stripped))
            {
                continue;
            }

            var trimmed = stripped.Trim();
            if (trimmed.StartsWith(SectionPrefix))
            {
                insideSection = true;
                continue;
            }

            if (insideSection)
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf(AssignmentSeparator);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            if (!string.Equals(key, ModelKeyName, StringComparison.Ordinal))
            {
                continue;
            }

            var rawValue = trimmed[(separatorIndex + 1)..].Trim();
            var value = Unquote(rawValue);
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }

        return null;
    }

    internal static IReadOnlyList<GeminiModelMetadata> ParseModelsCache(JsonElement rootElement)
    {
        if (rootElement.ValueKind != JsonValueKind.Object
            || !rootElement.TryGetProperty(ModelsPropertyName, out var modelsElement)
            || modelsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<GeminiModelMetadata>();
        foreach (var modelElement in modelsElement.EnumerateArray())
        {
            if (modelElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var slug = ReadStringProperty(modelElement, SlugPropertyName);
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            var displayName = ReadStringProperty(modelElement, DisplayNamePropertyName);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = slug;
            }

            var description = ReadStringProperty(modelElement, DescriptionPropertyName);
            var visibility = ReadStringProperty(modelElement, VisibilityPropertyName);
            var isListed = string.Equals(visibility, VisibilityListValue, StringComparison.Ordinal);
            var isApiSupported = ReadBooleanProperty(modelElement, SupportedInApiPropertyName);
            var reasoningEfforts = ReadReasoningEfforts(modelElement);

            models.Add(new GeminiModelMetadata(
                slug,
                displayName,
                description,
                isListed,
                isApiSupported,
                reasoningEfforts));
        }

        return models;
    }

    private static string ReadInstalledVersion(
        string executablePath,
        IReadOnlyDictionary<string, string> environment,
        bool inheritEnvironmentVariables,
        TimeSpan probeTimeout,
        int maximumOutputCharacters)
    {
        var probe = BoundedCliProcessProbe.Run(executablePath, [VersionFlag], environment,
            inheritEnvironmentVariables, probeTimeout, maximumOutputCharacters,
            leaseAcquisitionTimeout: probeTimeout);
        if (probe.ExitCode != 0)
        {
            throw new InvalidOperationException(ProbeFailureMessage);
        }

        var versionOutput = string.IsNullOrWhiteSpace(probe.StandardOutput)
            ? probe.StandardError
            : probe.StandardOutput;
        try
        {
            return ParseInstalledVersion(versionOutput);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(InvalidProbeOutputMessage);
        }
    }

    private static LatestVersionProbe ProbeLatestPublishedVersion(
        IReadOnlyDictionary<string, string> environment,
        bool inheritEnvironmentVariables,
        TimeSpan probeTimeout,
        int maximumOutputCharacters)
    {
        try
        {
            var probe = RunNpmVersionProbe(environment, inheritEnvironmentVariables,
                probeTimeout, maximumOutputCharacters);
            if (probe.ExitCode != 0)
            {
                return LatestVersionProbe.WithError(ProbeFailureMessage);
            }

            var latestVersion = ParseLatestPublishedVersion(probe.StandardOutput);
            return string.IsNullOrWhiteSpace(latestVersion)
                ? LatestVersionProbe.WithError(InvalidProbeOutputMessage)
                : LatestVersionProbe.WithLatest(latestVersion);
        }
        catch (Exception)
        {
            return LatestVersionProbe.WithError(ProbeFailureMessage);
        }
    }

    private static CliProcessProbeResult RunNpmVersionProbe(
        IReadOnlyDictionary<string, string> environment,
        bool inheritEnvironmentVariables,
        TimeSpan probeTimeout,
        int maximumOutputCharacters)
    {
        var npmArguments = new[] { NpmViewCommand, NpmPackageName, NpmVersionProperty, NpmSilentFlag };
        if (!OperatingSystem.IsWindows())
        {
            return BoundedCliProcessProbe.Run(NpmExecutableName, npmArguments, environment,
                inheritEnvironmentVariables, probeTimeout, maximumOutputCharacters,
                leaseAcquisitionTimeout: probeTimeout);
        }

        var commandProcessor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            WindowsCommandProcessorName);
        return BoundedCliProcessProbe.Run(commandProcessor,
            [WindowsCommandDisableAutoRunFlag, WindowsCommandFlag, NpmWindowsScriptName, .. npmArguments],
            environment, inheritEnvironmentVariables, probeTimeout, maximumOutputCharacters,
            leaseAcquisitionTimeout: probeTimeout);
    }

    private static string? ReadDefaultModel(string homeDirectory, int maximumCharacters)
    {
        var configPath = Path.Combine(homeDirectory, DotGeminiDirectory, SettingsFileName);
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            var settings = BoundedMetadataFileReader.ReadAllText(configPath, maximumCharacters);
            return ParseDefaultModelFromSettingsJson(settings);
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

    private static string ResolveHomeDirectory(
        IReadOnlyDictionary<string, string> environment,
        bool inheritEnvironmentVariables)
    {
        if (environment.TryGetValue(GeminiCliHomeEnvironmentVariable, out var cliHome) &&
            !string.IsNullOrWhiteSpace(cliHome))
        {
            return cliHome;
        }

        if (inheritEnvironmentVariables &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GeminiCliHomeEnvironmentVariable)))
        {
            return Environment.GetEnvironmentVariable(GeminiCliHomeEnvironmentVariable)!;
        }

        if (environment.TryGetValue(HomeEnvironmentVariable, out var home) && !string.IsNullOrWhiteSpace(home))
        {
            return home;
        }

        if (environment.TryGetValue(UserProfileEnvironmentVariable, out var profile) && !string.IsNullOrWhiteSpace(profile))
        {
            return profile;
        }

        if (inheritEnvironmentVariables)
        {
            var inheritedHome = Environment.GetEnvironmentVariable(HomeEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(inheritedHome))
            {
                return inheritedHome;
            }

            var inheritedProfile = Environment.GetEnvironmentVariable(UserProfileEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(inheritedProfile))
            {
                return inheritedProfile;
            }
        }

        return inheritEnvironmentVariables ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : string.Empty;
    }

    private static GeminiModelMetadata[] ReadKnownModels() =>
        GeminiModels.Known.Select(static slug => new GeminiModelMetadata(
            slug, slug, null, true, false, Array.Empty<string>())).ToArray();

    internal static string? ParseDefaultModelFromSettingsJson(string settingsJson)
    {
        ArgumentNullException.ThrowIfNull(settingsJson);
        using var document = JsonDocument.Parse(settingsJson);
        if (!document.RootElement.TryGetProperty(ModelPropertyName, out var model) ||
            model.ValueKind != JsonValueKind.Object ||
            !model.TryGetProperty(ModelNamePropertyName, out var name) ||
            name.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = name.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static List<string> ReadReasoningEfforts(JsonElement modelElement)
    {
        if (!modelElement.TryGetProperty(SupportedReasoningLevelsPropertyName, out var levelsElement)
            || levelsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var efforts = new List<string>();
        foreach (var levelElement in levelsElement.EnumerateArray())
        {
            if (levelElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var effort = ReadStringProperty(levelElement, EffortPropertyName);
            if (!string.IsNullOrWhiteSpace(effort))
            {
                efforts.Add(effort);
            }
        }

        return efforts;
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement)
            || valueElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return valueElement.GetString();
    }

    private static bool ReadBooleanProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement)
            || (valueElement.ValueKind != JsonValueKind.True && valueElement.ValueKind != JsonValueKind.False))
        {
            return false;
        }

        return valueElement.GetBoolean();
    }

    private static bool IsLikelyBunManagedPath(string executablePath)
    {
        return executablePath.Contains(BunPathMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBunUserAgent(string? npmUserAgent)
    {
        if (string.IsNullOrWhiteSpace(npmUserAgent))
        {
            return false;
        }

        return npmUserAgent.StartsWith(BunUserAgentPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathUnderRoot(string executablePath, string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (normalizedRoot.Length == 0)
        {
            return false;
        }

        return executablePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSemanticVersion(string value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith('v'))
        {
            candidate = candidate[1..];
        }

        var metadataSeparatorIndex = candidate.IndexOf('+');
        if (metadataSeparatorIndex >= 0)
        {
            candidate = candidate[..metadataSeparatorIndex];
        }

        var preReleaseSeparatorIndex = candidate.IndexOf('-');
        var core = preReleaseSeparatorIndex >= 0
            ? candidate[..preReleaseSeparatorIndex]
            : candidate;
        var preRelease = preReleaseSeparatorIndex >= 0
            ? candidate[(preReleaseSeparatorIndex + 1)..]
            : null;

        var coreParts = core.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (coreParts.Length < 3)
        {
            return false;
        }

        if (!int.TryParse(coreParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major))
        {
            return false;
        }

        if (!int.TryParse(coreParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }

        if (!int.TryParse(coreParts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(preRelease))
        {
            preRelease = null;
        }

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    private static int CompareSemanticVersion(SemanticVersion left, SemanticVersion right)
    {
        var majorComparison = left.Major.CompareTo(right.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = left.Minor.CompareTo(right.Minor);
        if (minorComparison != 0)
        {
            return minorComparison;
        }

        var patchComparison = left.Patch.CompareTo(right.Patch);
        if (patchComparison != 0)
        {
            return patchComparison;
        }

        if (left.PreRelease is null && right.PreRelease is null)
        {
            return 0;
        }

        if (left.PreRelease is null)
        {
            return 1;
        }

        if (right.PreRelease is null)
        {
            return -1;
        }

        return ComparePreRelease(left.PreRelease, right.PreRelease);
    }

    private static int ComparePreRelease(string left, string right)
    {
        var leftIdentifiers = left.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rightIdentifiers = right.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var maxLength = Math.Max(leftIdentifiers.Length, rightIdentifiers.Length);

        for (var index = 0; index < maxLength; index += 1)
        {
            if (index >= leftIdentifiers.Length)
            {
                return -1;
            }

            if (index >= rightIdentifiers.Length)
            {
                return 1;
            }

            var leftIdentifier = leftIdentifiers[index];
            var rightIdentifier = rightIdentifiers[index];

            var leftIsNumeric = int.TryParse(leftIdentifier, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightIsNumeric = int.TryParse(rightIdentifier, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

            if (leftIsNumeric && rightIsNumeric)
            {
                var numericComparison = leftNumber.CompareTo(rightNumber);
                if (numericComparison != 0)
                {
                    return numericComparison;
                }

                continue;
            }

            if (leftIsNumeric && !rightIsNumeric)
            {
                return -1;
            }

            if (!leftIsNumeric && rightIsNumeric)
            {
                return 1;
            }

            var textComparison = string.CompareOrdinal(leftIdentifier, rightIdentifier);
            if (textComparison != 0)
            {
                return textComparison;
            }
        }

        return 0;
    }

    private static string StripInlineComment(string line)
    {
        var insideQuotes = false;
        for (var index = 0; index < line.Length; index += 1)
        {
            var current = line[index];
            if (current == Quote)
            {
                var escaped = index > 0 && line[index - 1] == Escape;
                if (!escaped)
                {
                    insideQuotes = !insideQuotes;
                }

                continue;
            }

            if (current == CommentPrefix && !insideQuotes)
            {
                return line[..index];
            }
        }

        return line;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == Quote && value[^1] == Quote)
        {
            return value[1..^1];
        }

        return value;
    }

    private readonly record struct LatestVersionProbe(string? LatestVersion, string? ErrorMessage)
    {
        public static LatestVersionProbe WithLatest(string latestVersion) => new(latestVersion, null);

        public static LatestVersionProbe WithError(string errorMessage)
        {
            return new(
                null,
                string.IsNullOrWhiteSpace(errorMessage)
                    ? "unknown error"
                    : errorMessage);
        }
    }

    private readonly record struct SemanticVersion(
        int Major,
        int Minor,
        int Patch,
        string? PreRelease)
    {
        public string ToNormalizedString()
        {
            return PreRelease is null
                ? $"{Major}.{Minor}.{Patch}"
                : $"{Major}.{Minor}.{Patch}-{PreRelease}";
        }
    }
}
