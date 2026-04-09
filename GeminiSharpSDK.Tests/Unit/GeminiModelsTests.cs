using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ManagedCode.GeminiSharpSDK.Models;

namespace ManagedCode.GeminiSharpSDK.Tests.Unit;

public class GeminiModelsTests
{
    private const string SolutionFileName = "ManagedCode.GeminiSharpSDK.slnx";
    private const string BundledModelsFileName = "models.json";
    private const string BundledModelsTsFileName = "models.ts";
    private const string ValidGeminiModelsSetName = "VALID_GEMINI_MODELS";

    [Test]
    public async Task GeminiModels_ContainAllBundledUpstreamModelSlugs()
    {
        var bundledModelSlugs = await ReadBundledModelSlugsAsync();
        var sdkModelSlugs = GetSdkModelSlugs();
        var missingBundledSlugs = bundledModelSlugs
            .Except(sdkModelSlugs, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(missingBundledSlugs).IsEmpty();
    }

    private static string[] GetSdkModelSlugs()
    {
        return typeof(GeminiModels)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field =>
                field is { IsLiteral: true, IsInitOnly: false, FieldType: not null } &&
                field.FieldType == typeof(string) &&
                field.Name.StartsWith("Gemini", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();
    }

    private static async Task<string[]> ReadBundledModelSlugsAsync()
    {
        var modelsPath = ResolveBundledModelSlugsFilePath();

        if (Path.GetExtension(modelsPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = File.OpenRead(modelsPath);
            using var document = await JsonDocument.ParseAsync(stream);
            return document.RootElement
                .GetProperty("models")
                .EnumerateArray()
                .Select(model => model.GetProperty("slug").GetString())
                .OfType<string>()
                .ToArray();
        }

        var content = await File.ReadAllTextAsync(modelsPath);
        return ReadBundledModelSlugsFromTypeScript(content);
    }

    private static string[] ReadBundledModelSlugsFromTypeScript(string content)
    {
        var setMatch = ValidModelsSetRegex.Match(content);
        if (setMatch.Success)
        {
            return ValidModelValuesRegex
                .Matches(setMatch.Groups["values"].Value)
                .Select(match => match.Groups["value"].Value)
                .Where(IsModelSlug)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        return ModelDeclarationRegex
            .Matches(content)
            .Select(match => new
            {
                Name = match.Groups["name"].Value,
                Value = match.Groups["value"].Value,
            })
            .Where(item => ModelNameRegex.IsMatch(item.Name))
            .Select(item => item.Value)
            .Where(IsModelSlug)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveBundledModelSlugsFilePath()
    {
        var repositoryRoot = ResolveRepositoryRootPath();
        var candidates = new[]
        {
            Path.Combine(repositoryRoot, "submodules", "google-gemini-cli", "packages", "core", "src", "config", BundledModelsTsFileName),
            Path.Combine(repositoryRoot, "submodules", "google-gemini-cli", "gemini-rs", "core", BundledModelsFileName),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var candidatesList = string.Join(", ", candidates);
        throw new FileNotFoundException(
            $"Could not find bundled model definitions at expected paths: {candidatesList}");
    }

    private static bool IsModelSlug(string value)
    {
        return ModelSlugPattern.IsMatch(value);
    }

    private static string ResolveRepositoryRootPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, SolutionFileName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test execution directory.");
    }

    private static readonly Regex ModelDeclarationRegex = new(
        @"export\s+const\s+(?<name>[A-Z0-9_]+)\s*=\s*['""](?<value>[^'""]+)['""]",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex ValidModelsSetRegex = new(
        @"export\s+const\s+" + ValidGeminiModelsSetName + @"\s*=\s*new\s+Set\s*\(\s*\[(?<values>.*?)\]\s*\)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ValidModelValuesRegex = new(
        @"['""](?<value>[^'""]+)['""]",
        RegexOptions.Compiled);

    private static readonly Regex ModelNameRegex = new(
        @"_MODEL\b|_EMBEDDING_MODEL\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ModelSlugPattern = new(
        @"^[a-z][a-z0-9.-]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
