using ManagedCode.GeminiSharpSDK.Client;
using ManagedCode.GeminiSharpSDK.Configuration;
using ManagedCode.GeminiSharpSDK.Internal;
using ManagedCode.GeminiSharpSDK.Models;
using ManagedCode.GeminiSharpSDK.Tests.Shared;
using ManagedCode.GeminiSharpSDK.Tests.TestSupport;

namespace ManagedCode.GeminiSharpSDK.Tests.Unit;

[NotInParallel("CliProcess")]
public class GeminiClientTests
{
    private const string NpmFixtureSkipReason = "The npm.cmd invocation fixture is Windows-specific.";
    private const string NpmFixtureDirectoryPrefix = "GeminiNpmProbe-";
    private const string NpmFixtureScriptFileName = "npm.cmd";
    private const string NpmFixtureArgumentsFileName = "npm-arguments.txt";
    private const string NpmFixtureVersionOutput = "99.0.0";
    private const string NpmFixtureExpectedArguments = "view @google/gemini-cli version --silent";
    private const string NpmFixturePackageName = "npm";
    private const string NpmFixtureScriptRelativePath = "bin/npm-cli.js";
    private const string NpmFixtureScriptContent = "const fs = require('node:fs');\n" +
        "fs.writeFileSync(process.env.SDK_NPM_ARGS_FILE, process.argv.slice(2).join(' '));\n" +
        "console.log('" + NpmFixtureVersionOutput + "');\n";
    private const string NpmFixtureShimContent = "@ECHO off\r\nexit /b 91\r\n";
    private const string NpmArgumentsEnvironmentVariable = "SDK_NPM_ARGS_FILE";
    private const string SystemRootEnvironmentVariable = "SystemRoot";
    private const string HomeEnvironmentVariable = "HOME";
    private const string UserProfileEnvironmentVariable = "USERPROFILE";
    private const string MetadataSandboxPrefix = "GeminiClientMetadata-";
    private const string PathEnvironmentVariable = "PATH";
    private const string AmbientInstallFallbackName = "GeminiAmbientInstallFallback-";
    private const string GeminiCliHomeEnvironmentVariable = "GEMINI_CLI_HOME";
    private const string DotGeminiDirectoryName = ".gemini";
    private const string GeminiSettingsFileName = "settings.json";
    private const int SmallMetadataFileLimit = 256;
    private const string MetadataFileLimitMessage = "CLI metadata file exceeded the configured character limit.";
    private const string OversizedSettingsPrefix = "{ \"model\": { \"name\": \"";
    private const string OversizedSettingsPaddingPrefix = "\" }, \"padding\": \"";
    private const string OversizedSettingsSuffix = "\" }";
    private static readonly TimeSpan RealCliMetadataProbeTimeout = TimeSpan.FromSeconds(20);
    private const string GeminiSettingsFixture =
        "{ \"model\": { \"name\": \"" + GeminiModels.Gemini35Flash + "\" } }";
    private const string ResumeSandboxPrefix = "GeminiClientTests-ResumeThread-";
    private static readonly TimeSpan SandboxCommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MultiTurnTimeout = TimeSpan.FromMinutes(3);

    [Test]
    public async Task GeminiOptions_LaunchResolutionUsesOnlyIsolatedEnvironmentPath()
    {
        var emptyPath = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox",
            $"{AmbientInstallFallbackName}{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyPath);
        try
        {
            var options = new GeminiOptions
            {
                InheritEnvironmentVariables = false,
                EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PathEnvironmentVariable] = emptyPath,
                },
            };

            var exception = await Assert.That(() => options.GetCliLaunchCommand()).ThrowsException();

            await Assert.That(exception).IsTypeOf<FileNotFoundException>();
        }
        finally
        {
            Directory.Delete(emptyPath, recursive: true);
        }
    }

    [Test]
    public async Task StartAsync_CanBeCalledConcurrently()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        var starts = Enumerable.Range(0, 64)
            .Select(_ => client.StartAsync())
            .ToArray();

        await Task.WhenAll(starts);
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Connected);
    }

    [Test]
    public async Task StartThread_CanBeCalledConcurrently()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        await client.StartAsync();

        var createdThreads = await Task.WhenAll(
            Enumerable.Range(0, 64)
                .Select(_ => Task.Run(() => client.StartThread())));

        await Assert.That(createdThreads).Count().IsEqualTo(64);
        await Assert.That(createdThreads.All(thread => thread.Id is null)).IsTrue();
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Connected);
    }

    [Test]
    public async Task StopAsync_CanBeCalledConcurrently()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        await client.StartAsync();

        var stops = Enumerable.Range(0, 64)
            .Select(_ => client.StopAsync())
            .ToArray();

        await Task.WhenAll(stops);
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Disconnected);
    }

    [Test]
    public async Task StartAsync_IsIdempotentAndSetsConnectedState()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        await client.StartAsync();
        await client.StartAsync();

        await Assert.That(client.State).IsEqualTo(GeminiClientState.Connected);
    }

    [Test]
    public async Task StartThread_AutoStartEnabledStartsImplicitly()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
        });

        var thread = client.StartThread();

        await Assert.That(thread.Id).IsNull();
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Connected);
    }

    [Test]
    public async Task StartThread_ParameterlessClientUsesDefaultAutoStart()
    {
        using var client = new GeminiClient();

        var thread = client.StartThread(new ThreadOptions
        {
            Model = GeminiModels.Gemini3ProPreview,
            ModelReasoningEffort = ModelReasoningEffort.Medium,
        });

        await Assert.That(thread.Id).IsNull();
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Connected);
    }

    [Test]
    public async Task ResumeThread_CreatesThreadWithProvidedId()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
        });

        var thread = client.ResumeThread("thread_1");
        await Assert.That(thread.Id).IsEqualTo("thread_1");
    }

    [Test]
    public async Task ResumeThread_ThrowsForInvalidId()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
        });

        var action = () => client.ResumeThread(" ");
        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task StartThread_ThrowsWhenAutoStartDisabled()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        var action = () => client.StartThread();
        await Assert.That(action).ThrowsException();
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Disconnected);
    }

    [Test]
    public async Task StopAsync_SetsDisconnectedState()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        await client.StartAsync();
        await client.StopAsync();

        await Assert.That(client.State).IsEqualTo(GeminiClientState.Disconnected);
    }

    [Test]
    public async Task Dispose_SetsDisposedStateAndBlocksOperations()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        client.Dispose();

        await Assert.That(client.State).IsEqualTo(GeminiClientState.Disposed);

        var action = async () => await client.StartAsync();
        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public async Task Dispose_CanBeCalledConcurrently()
    {
        var client = new GeminiClient(new GeminiClientOptions
        {
            GeminiOptions = new GeminiOptions
            {
                GeminiExecutablePath = "gemini",
            },
            AutoStart = false,
        });

        var disposals = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => client.Dispose()))
            .ToArray();

        await Task.WhenAll(disposals);
        await Assert.That(client.State).IsEqualTo(GeminiClientState.Disposed);
    }

    [Test]
    public async Task GeminiCli_Smoke_GetCliMetadata_ReturnsInstalledVersion()
    {
        using var client = new GeminiClient(new GeminiOptions
        {
            CliMetadataProbeTimeout = RealCliMetadataProbeTimeout,
        });

        var metadata = client.GetCliMetadata();

        await Assert.That(string.IsNullOrWhiteSpace(metadata.InstalledVersion)).IsFalse();
        await Assert.That(metadata.InstalledVersion.Contains('.')).IsTrue();
    }

    [Test]
    public async Task GeminiCli_GetCliMetadata_UsesNativeSettingsAndKnownCatalogWithEnvironmentAllowlist()
    {
        var cliHome = CreateMetadataSandbox();
        var configDirectory = Path.Combine(cliHome, DotGeminiDirectoryName);
        Directory.CreateDirectory(configDirectory);
        try
        {
            File.WriteAllText(Path.Combine(configDirectory, GeminiSettingsFileName), GeminiSettingsFixture);
            using var client = new GeminiClient(new GeminiOptions
            {
                GeminiExecutablePath = GeminiCliLocator.FindGeminiPath(null),
                EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PathEnvironmentVariable] = Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty,
                    [SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable) ?? string.Empty,
                    [HomeEnvironmentVariable] = Environment.GetEnvironmentVariable(HomeEnvironmentVariable) ?? string.Empty,
                    [UserProfileEnvironmentVariable] = Environment.GetEnvironmentVariable(UserProfileEnvironmentVariable) ?? string.Empty,
                    [GeminiCliHomeEnvironmentVariable] = cliHome,
                },
                InheritEnvironmentVariables = false,
                CliMetadataProbeTimeout = RealCliMetadataProbeTimeout,
            });

            var metadata = client.GetCliMetadata();

            await Assert.That(metadata.DefaultModel).IsEqualTo(GeminiModels.Gemini35Flash);
            await Assert.That(metadata.Models.Any(model => model.Slug == GeminiModels.Gemini35Flash)).IsTrue();
            await Assert.That(metadata.Models.Any(model => model.Slug == GeminiModels.Gemini31FlashLite)).IsTrue();
            foreach (var alias in new[]
                     {
                         GeminiModels.AliasAuto,
                         GeminiModels.AliasPro,
                         GeminiModels.AliasFlash,
                         GeminiModels.AliasFlashLite,
                         GeminiModels.AutoGemini3,
                         GeminiModels.AutoGemini25,
                     })
            {
                await Assert.That(metadata.Models.Any(model => model.Slug == alias && model.IsListed)).IsTrue();
            }

            await Assert.That(metadata.Models.Any(model => model.Slug == GeminiModels.Gemini31FlashLitePreview)).IsFalse();
            await Assert.That(metadata.Models.Single(model => model.Slug == GeminiModels.Gemini35Flash).IsApiSupported)
                .IsFalse();
        }
        finally
        {
            Directory.Delete(cliHome, recursive: true);
        }
    }

    [Test]
    public async Task GeminiCli_GetCliMetadata_RejectsOversizedSettingsFile()
    {
        var cliHome = CreateMetadataSandbox();
        var configDirectory = Path.Combine(cliHome, DotGeminiDirectoryName);
        Directory.CreateDirectory(configDirectory);
        try
        {
            File.WriteAllText(Path.Combine(configDirectory, GeminiSettingsFileName),
                string.Concat(OversizedSettingsPrefix, GeminiModels.Gemini35Flash,
                    OversizedSettingsPaddingPrefix, new string('x', SmallMetadataFileLimit + 1), OversizedSettingsSuffix));
            using var client = new GeminiClient(new GeminiOptions
            {
                GeminiExecutablePath = GeminiCliLocator.FindGeminiPath(null),
                EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PathEnvironmentVariable] = Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty,
                    [SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable) ?? string.Empty,
                    [HomeEnvironmentVariable] = Environment.GetEnvironmentVariable(HomeEnvironmentVariable) ?? string.Empty,
                    [UserProfileEnvironmentVariable] = Environment.GetEnvironmentVariable(UserProfileEnvironmentVariable) ?? string.Empty,
                    [GeminiCliHomeEnvironmentVariable] = cliHome,
                },
                InheritEnvironmentVariables = false,
                CliMetadataMaximumFileCharacters = SmallMetadataFileLimit,
            });

            var action = () => client.GetCliMetadata();
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).Contains(MetadataFileLimitMessage);
        }
        finally
        {
            Directory.Delete(cliHome, recursive: true);
        }
    }

    [Test]
    public async Task GeminiCli_GetCliMetadata_RejectsNonPositiveProbeLeaseTimeout()
    {
        foreach (var timeout in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(-1) })
        {
            using var client = new GeminiClient(new GeminiOptions { CliMetadataProbeLeaseTimeout = timeout });
            var action = () => client.GetCliMetadata();
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task GeminiCli_Smoke_GetCliUpdateStatus_ReturnsInstalledVersion()
    {
        using var client = new GeminiClient(new GeminiOptions
        {
            CliMetadataProbeTimeout = RealCliMetadataProbeTimeout,
        });

        var status = client.GetCliUpdateStatus();

        await Assert.That(string.IsNullOrWhiteSpace(status.InstalledVersion)).IsFalse();
        await Assert.That(status.InstalledVersion.Contains('.')).IsTrue();
    }

    [Test]
    public async Task GeminiCli_UpdateStatus_InvokesValidatedNpmPackageThroughNode()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test(NpmFixtureSkipReason);
            return;
        }

        var sandbox = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox",
            $"{NpmFixtureDirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandbox);
        var npmScriptPath = Path.Combine(sandbox, NpmFixtureScriptFileName);
        var argumentsPath = Path.Combine(sandbox, NpmFixtureArgumentsFileName);
        var nodePath = FindNodeExecutable();
        var npmCliPath = Path.Combine(sandbox, "node_modules", NpmFixturePackageName,
            NpmFixtureScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            File.WriteAllText(npmScriptPath, NpmFixtureShimContent);
            Directory.CreateDirectory(Path.GetDirectoryName(npmCliPath)!);
            File.WriteAllText(Path.Combine(sandbox, "node_modules", NpmFixturePackageName, "package.json"),
                "{\"name\":\"" + NpmFixturePackageName + "\",\"bin\":{\"npm\":\"" + NpmFixtureScriptRelativePath + "\"}}");
            File.WriteAllText(npmCliPath, NpmFixtureScriptContent);
            using var client = new GeminiClient(new GeminiOptions
            {
                GeminiExecutablePath = GeminiCliLocator.FindGeminiPath(null),
                InheritEnvironmentVariables = false,
                EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PathEnvironmentVariable] = string.Join(Path.PathSeparator, sandbox, Path.GetDirectoryName(nodePath)),
                    [SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable) ?? string.Empty,
                    [NpmArgumentsEnvironmentVariable] = argumentsPath,
                },
            });

            var status = client.GetCliUpdateStatus();

            await Assert.That(status.LatestVersion).IsEqualTo(NpmFixtureVersionOutput);
            await Assert.That(status.IsUpdateAvailable).IsTrue();
            await Assert.That(File.ReadAllText(argumentsPath).Trim()).IsEqualTo(NpmFixtureExpectedArguments);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    private static string FindNodeExecutable()
    {
        foreach (var entry in (Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(entry.Trim('"'), "node.exe");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException("Node.js must be available on PATH for this Windows CLI metadata regression.");
    }

    private static string CreateMetadataSandbox()
    {
        var sandbox = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox",
            $"{MetadataSandboxPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandbox);
        return sandbox;
    }

    [Test]
    [Property("RequiresGeminiAuth", "true")]
    [ParallelLimiter<GeminiAuthParallelLimit>]
    public async Task ResumeThread_WithThreadOptions_RunsWithRealGeminiCli()
    {
        var settings = RealGeminiTestSupport.GetRequiredSettings();
        using var sandbox = await RealGeminiTestSandbox.CreateAsync(
            ResumeSandboxPrefix,
            SandboxCommandTimeout);

        using var client = RealGeminiTestSupport.CreateClient();

        var startedThread = client.StartThread(sandbox.CreateThreadOptions(settings.Model, ephemeral: false));
        using var firstCancellation = new CancellationTokenSource(MultiTurnTimeout);

        var firstResult = await startedThread.RunAsync(
            "Reply with short plain text: ok.",
            new TurnOptions { CancellationToken = firstCancellation.Token });

        var threadId = startedThread.Id;
        await Assert.That(threadId).IsNotNull();
        await Assert.That(firstResult.Usage).IsNotNull();

        var resumedThread = client.ResumeThread(
            threadId!,
            sandbox.CreateThreadOptions(settings.Model, ephemeral: false));
        using var secondCancellation = new CancellationTokenSource(MultiTurnTimeout);

        var secondResult = await resumedThread.RunAsync(
            "Reply with short plain text: ok.",
            new TurnOptions { CancellationToken = secondCancellation.Token });

        await Assert.That(secondResult.Usage).IsNotNull();
        await Assert.That(resumedThread.Id).IsEqualTo(threadId);
    }
}
