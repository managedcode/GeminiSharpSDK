using ManagedCode.GeminiSharpSDK.Internal;

namespace ManagedCode.GeminiSharpSDK.Tests.Unit;

public class GeminiCliLocatorTests
{
    [Test]
    public async Task GetPathExecutableCandidates_Windows_IncludeCommandWrappers()
    {
        var candidates = GeminiCliLocator.GetPathExecutableCandidates(isWindows: true);

        await Assert.That(candidates).IsEquivalentTo(
        [
            GeminiCliLocator.GeminiWindowsExecutableName,
            $"{GeminiCliLocator.GeminiExecutableName}.cmd",
            $"{GeminiCliLocator.GeminiExecutableName}.bat",
            GeminiCliLocator.GeminiExecutableName,
        ]);
    }

    [Test]
    public async Task TryResolvePathExecutable_Windows_ResolvesGeminiCmdWhenExeMissing()
    {
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var firstPathEntry = Path.Combine(sandboxDirectory, "first");
            var secondPathEntry = Path.Combine(sandboxDirectory, "second");
            Directory.CreateDirectory(firstPathEntry);
            Directory.CreateDirectory(secondPathEntry);

            var cmdPath = Path.Combine(secondPathEntry, $"{GeminiCliLocator.GeminiExecutableName}.cmd");
            await File.WriteAllTextAsync(cmdPath, "@echo off");

            var pathVariable = string.Join(Path.PathSeparator, firstPathEntry, secondPathEntry);

            var resolved = GeminiCliLocator.TryResolvePathExecutable(pathVariable, isWindows: true, out var executablePath);
            await Assert.That(resolved).IsTrue();
            await Assert.That(executablePath).IsEqualTo(cmdPath);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task TryResolvePathExecutable_Unix_DoesNotTreatCmdAsExecutable()
    {
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var pathEntry = Path.Combine(sandboxDirectory, "unix");
            Directory.CreateDirectory(pathEntry);

            var cmdPath = Path.Combine(pathEntry, $"{GeminiCliLocator.GeminiExecutableName}.cmd");
            await File.WriteAllTextAsync(cmdPath, "#!/usr/bin/env bash");

            var pathVariable = pathEntry;

            var resolved = GeminiCliLocator.TryResolvePathExecutable(pathVariable, isWindows: false, out var executablePath);
            await Assert.That(resolved).IsFalse();
            await Assert.That(executablePath).IsEqualTo(string.Empty);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task TryResolvePathExecutable_Unix_ResolvesGeminiBinary()
    {
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var pathEntry = Path.Combine(sandboxDirectory, "unix");
            Directory.CreateDirectory(pathEntry);

            var binaryPath = Path.Combine(pathEntry, GeminiCliLocator.GeminiExecutableName);
            await File.WriteAllTextAsync(binaryPath, "#!/usr/bin/env bash");

            var resolved = GeminiCliLocator.TryResolvePathExecutable(pathEntry, isWindows: false, out var executablePath);
            await Assert.That(resolved).IsTrue();
            await Assert.That(executablePath).IsEqualTo(binaryPath);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task TryResolveNpmInstalledBinary_ResolvesPrimaryVendoredBinary()
    {
        var targetTriple = GeminiCliLocator.GetCurrentTargetTriple();
        if (targetTriple is null)
        {
            return;
        }

        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var binaryPath = CreateVendoredBinaryPath(sandboxDirectory, targetTriple, nested: false);
            Directory.CreateDirectory(Path.GetDirectoryName(binaryPath)!);
            await File.WriteAllTextAsync(binaryPath, "binary");

            var resolved = GeminiCliLocator.TryResolveNpmInstalledBinary([sandboxDirectory], targetTriple, OperatingSystem.IsWindows(), out var executablePath);

            await Assert.That(resolved).IsTrue();
            await Assert.That(executablePath).IsEqualTo(binaryPath);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task TryResolveNpmInstalledBinary_ResolvesNestedVendoredBinary()
    {
        var targetTriple = GeminiCliLocator.GetCurrentTargetTriple();
        if (targetTriple is null)
        {
            return;
        }

        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var binaryPath = CreateVendoredBinaryPath(sandboxDirectory, targetTriple, nested: true);
            Directory.CreateDirectory(Path.GetDirectoryName(binaryPath)!);
            await File.WriteAllTextAsync(binaryPath, "binary");

            var resolved = GeminiCliLocator.TryResolveNpmInstalledBinary([sandboxDirectory], targetTriple, OperatingSystem.IsWindows(), out var executablePath);

            await Assert.That(resolved).IsTrue();
            await Assert.That(executablePath).IsEqualTo(binaryPath);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    private static string CreateSandboxDirectory()
    {
        var sandboxDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "tests",
            ".sandbox",
            $"GeminiCliLocatorTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandboxDirectory);
        return sandboxDirectory;
    }

    private static string CreateVendoredBinaryPath(string sandboxDirectory, string targetTriple, bool nested)
    {
        var packageDirectory = GetPackageDirectory(targetTriple);
        var executableName = OperatingSystem.IsWindows()
            ? GeminiCliLocator.GeminiWindowsExecutableName
            : GeminiCliLocator.GeminiExecutableName;

        var segments = new List<string>
        {
            sandboxDirectory,
            "node_modules",
            "@google",
        };

        if (nested)
        {
            segments.Add("gemini");
            segments.Add("node_modules");
            segments.Add("@google");
        }

        segments.Add(packageDirectory);
        segments.Add("vendor");
        segments.Add(targetTriple);
        segments.Add("gemini");
        segments.Add(executableName);

        return Path.Combine([.. segments]);
    }

    private static string GetPackageDirectory(string targetTriple)
    {
        return targetTriple switch
        {
            "x86_64-unknown-linux-musl" => "gemini-cli-linux-x64",
            "aarch64-unknown-linux-musl" => "gemini-cli-linux-arm64",
            "x86_64-apple-darwin" => "gemini-cli-darwin-x64",
            "aarch64-apple-darwin" => "gemini-cli-darwin-arm64",
            "x86_64-pc-windows-msvc" => "gemini-cli-win32-x64",
            "aarch64-pc-windows-msvc" => "gemini-cli-win32-arm64",
            _ => throw new InvalidOperationException($"Unsupported test target triple: {targetTriple}"),
        };
    }
}
