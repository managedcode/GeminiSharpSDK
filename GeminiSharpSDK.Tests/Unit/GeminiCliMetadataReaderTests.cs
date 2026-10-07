using System.Text.Json;
using ManagedCode.GeminiSharpSDK.Internal;
using ManagedCode.GeminiSharpSDK.Models;

namespace ManagedCode.GeminiSharpSDK.Tests.Unit;

public class GeminiCliMetadataReaderTests
{
    private const string GeminiNpmUpdateCommand = "npm install --global @google/gemini-cli@latest";
    private const string GeminiBunUpdateCommand = "bun add --global @google/gemini-cli@latest";
    private const string UserInstallExecutablePath = "/usr/local/bin/gemini";
    private const string SettingsJson = "{ \"model\": { \"name\": \"" + GeminiModels.Gemini35Flash + "\" } }";
    [Test]
    public async Task ParseInstalledVersion_ReturnsVersionTokenForGeminiCliOutput()
    {
        const string versionOutput = "gemini-cli 0.110.0";

        var parsed = GeminiCliMetadataReader.ParseInstalledVersion(versionOutput);

        await Assert.That(parsed).IsEqualTo("0.110.0");
    }

    [Test]
    public async Task ParseLatestPublishedVersion_ReturnsVersionTokenForNpmOutput()
    {
        const string npmOutput = "0.111.0";

        var parsed = GeminiCliMetadataReader.ParseLatestPublishedVersion(npmOutput);

        await Assert.That(parsed).IsEqualTo("0.111.0");
    }

    [Test]
    public async Task ParseLatestPublishedVersion_ExtractsVersionFromNoisyOutput()
    {
        const string npmOutput = """
                                 npm notice
                                 gemini-cli release: "0.112.0-beta.1"
                                 """;

        var parsed = GeminiCliMetadataReader.ParseLatestPublishedVersion(npmOutput);

        await Assert.That(parsed).IsEqualTo("0.112.0-beta.1");
    }

    [Test]
    public async Task IsNewerVersion_ReturnsTrueForHigherStableVersion()
    {
        var isNewer = GeminiCliMetadataReader.IsNewerVersion("0.111.0", "0.110.0");

        await Assert.That(isNewer).IsTrue();
    }

    [Test]
    public async Task IsNewerVersion_ReturnsFalseForLowerVersion()
    {
        var isNewer = GeminiCliMetadataReader.IsNewerVersion("0.110.0", "0.111.0");

        await Assert.That(isNewer).IsFalse();
    }

    [Test]
    public async Task IsNewerVersion_ReturnsFalseWhenLatestIsPrereleaseAndInstalledIsStable()
    {
        var isNewer = GeminiCliMetadataReader.IsNewerVersion("0.111.0-beta.1", "0.111.0");

        await Assert.That(isNewer).IsFalse();
    }

    [Test]
    public async Task ResolveUpdateCommand_ReturnsBunCommand_ForBunManagedPath()
    {
        const string executablePath = "/Users/example/.bun/bin/gemini";

        var command = GeminiCliMetadataReader.ResolveUpdateCommand(executablePath);

        await Assert.That(command).IsEqualTo(GeminiBunUpdateCommand);
    }

    [Test]
    public async Task ResolveUpdateCommand_ReturnsBunCommand_ForBunUserAgent()
    {
        const string executablePath = "/usr/local/bin/gemini";

        var command = GeminiCliMetadataReader.ResolveUpdateCommand(
            executablePath,
            npmUserAgent: "bun/1.2.0 npm/? node/v20.0.0");

        await Assert.That(command).IsEqualTo(GeminiBunUpdateCommand);
    }

    [Test]
    public async Task ResolveUpdateCommand_ReturnsNpmCommand_ByDefault()
    {
        const string executablePath = "/usr/local/bin/gemini";

        var command = GeminiCliMetadataReader.ResolveUpdateCommand(executablePath, npmUserAgent: "npm/10.0.0");

        await Assert.That(command).IsEqualTo(GeminiNpmUpdateCommand);
    }

    [Test]
    public async Task ResolveUpdateCommand_DoesNotConsultParentEnvironmentWhenDisabled()
    {
        var command = GeminiCliMetadataReader.ResolveUpdateCommand(
            UserInstallExecutablePath,
            npmUserAgent: null,
            bunInstallRoot: null,
            useProcessEnvironmentFallback: false);

        await Assert.That(command).IsEqualTo(GeminiNpmUpdateCommand);
    }

    [Test]
    public async Task ParseDefaultModelFromSettingsJson_ReadsNativeModelName()
    {
        var parsed = GeminiCliMetadataReader.ParseDefaultModelFromSettingsJson(SettingsJson);

        await Assert.That(parsed).IsEqualTo(GeminiModels.Gemini35Flash);
    }

    [Test]
    public async Task KnownModels_ContainsCurrentCliModelCatalog()
    {
        await Assert.That(GeminiModels.Known).Contains(GeminiModels.Gemini35Flash);
        await Assert.That(GeminiModels.Known).Contains(GeminiModels.Gemini31FlashLite);
    }

    [Test]
    public async Task ParseDefaultModelFromTomlLines_UsesTopLevelModelOnly()
    {
        string[] lines =
        [
            "model = \"gpt-5.3-gemini\"",
            "",
            "[profiles.fast]",
            "model = \"gpt-5.2-gemini\"",
        ];

        var parsed = GeminiCliMetadataReader.ParseDefaultModelFromTomlLines(lines);

        await Assert.That(parsed).IsEqualTo("gpt-5.3-gemini");
    }

    [Test]
    public async Task ParseDefaultModelFromTomlLines_HandlesInlineComments()
    {
        string[] lines =
        [
            "model = \"gpt-5.3-gemini\" # default model",
        ];

        var parsed = GeminiCliMetadataReader.ParseDefaultModelFromTomlLines(lines);

        await Assert.That(parsed).IsEqualTo("gpt-5.3-gemini");
    }

    [Test]
    public async Task ParseModelsCache_ParsesCurrentUpstreamModelShape()
    {
        const string json = """
                            {
                              "models": [
                                {
                                  "slug": "gpt-5.3-gemini",
                                  "display_name": "gpt-5.3-gemini",
                                  "description": "Latest frontier agentic coding model.",
                                  "visibility": "list",
                                  "supported_in_api": true,
                                  "default_reasoning_summary": "none",
                                  "availability_nux": null,
                                  "upgrade": {
                                    "model": "gpt-5.4",
                                    "migration_markdown": "Introducing GPT-5.4"
                                  },
                                  "supported_reasoning_levels": [
                                    { "effort": "low" },
                                    { "effort": "high" }
                                  ]
                                },
                                {
                                  "slug": "gpt-5.4",
                                  "display_name": "gpt-5.4",
                                  "description": "Latest frontier agentic coding model.",
                                  "visibility": "list",
                                  "supported_in_api": true,
                                  "default_reasoning_summary": "none",
                                  "availability_nux": null,
                                  "upgrade": null,
                                  "supported_reasoning_levels": [
                                    { "effort": "medium" },
                                    { "effort": "xhigh" }
                                  ]
                                },
                                {
                                  "slug": "gpt-5.1-gemini-mini",
                                  "display_name": "gpt-5.1-gemini-mini",
                                  "visibility": "hidden",
                                  "supported_in_api": false,
                                  "default_reasoning_summary": "auto",
                                  "availability_nux": null,
                                  "supported_reasoning_levels": []
                                },
                                {
                                  "display_name": "missing-slug"
                                }
                              ]
                            }
                            """;

        using var document = JsonDocument.Parse(json);
        var parsed = GeminiCliMetadataReader.ParseModelsCache(document.RootElement);

        await Assert.That(parsed).Count().IsEqualTo(3);
        await Assert.That(parsed[0].Slug).IsEqualTo("gpt-5.3-gemini");
        await Assert.That(parsed[0].IsListed).IsTrue();
        await Assert.That(parsed[0].IsApiSupported).IsTrue();
        await Assert.That(parsed[0].SupportedReasoningEfforts).IsEquivalentTo(["low", "high"]);

        await Assert.That(parsed[1].Slug).IsEqualTo("gpt-5.4");
        await Assert.That(parsed[1].IsListed).IsTrue();
        await Assert.That(parsed[1].IsApiSupported).IsTrue();
        await Assert.That(parsed[1].SupportedReasoningEfforts).IsEquivalentTo(["medium", "xhigh"]);

        await Assert.That(parsed[2].Slug).IsEqualTo("gpt-5.1-gemini-mini");
        await Assert.That(parsed[2].IsListed).IsFalse();
        await Assert.That(parsed[2].IsApiSupported).IsFalse();
    }
}
