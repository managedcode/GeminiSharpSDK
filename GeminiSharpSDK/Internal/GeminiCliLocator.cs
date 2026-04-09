using System.Runtime.InteropServices;

namespace ManagedCode.GeminiSharpSDK.Internal;

internal static class GeminiCliLocator
{
    private const string PathEnvironmentVariable = "PATH";
    private const string CmdScriptExtension = ".cmd";
    private const string BatScriptExtension = ".bat";
    private const string NodeModulesDirectory = "node_modules";
    private const string GoogleScopeDirectory = "@google";
    private const string VendorDirectory = "vendor";
    private const string TargetGeminiDirectory = "gemini";
    private const string NestedGeminiPackageDirectory = "gemini";

    private const string TargetLinuxX64 = "x86_64-unknown-linux-musl";
    private const string TargetLinuxArm64 = "aarch64-unknown-linux-musl";
    private const string TargetDarwinX64 = "x86_64-apple-darwin";
    private const string TargetDarwinArm64 = "aarch64-apple-darwin";
    private const string TargetWindowsX64 = "x86_64-pc-windows-msvc";
    private const string TargetWindowsArm64 = "aarch64-pc-windows-msvc";

    private const string PackageGeminiLinuxX64 = "gemini-cli-linux-x64";
    private const string PackageGeminiLinuxArm64 = "gemini-cli-linux-arm64";
    private const string PackageGeminiDarwinX64 = "gemini-cli-darwin-x64";
    private const string PackageGeminiDarwinArm64 = "gemini-cli-darwin-arm64";
    private const string PackageGeminiWindowsX64 = "gemini-cli-win32-x64";
    private const string PackageGeminiWindowsArm64 = "gemini-cli-win32-arm64";

    internal const string GeminiExecutableName = "gemini";
    internal const string GeminiWindowsExecutableName = "gemini.exe";
    internal const string GeminiWindowsCommandName = GeminiExecutableName + CmdScriptExtension;
    internal const string GeminiWindowsBatchName = GeminiExecutableName + BatScriptExtension;

    private static readonly string[] WindowsPathExecutableCandidates =
    [
        GeminiWindowsExecutableName,
        GeminiWindowsCommandName,
        GeminiWindowsBatchName,
        GeminiExecutableName,
    ];

    private static readonly string[] UnixPathExecutableCandidates =
    [
        GeminiExecutableName,
    ];

    private static readonly Dictionary<string, string> PlatformPackageByTarget =
        new(StringComparer.Ordinal)
        {
            [TargetLinuxX64] = PackageGeminiLinuxX64,
            [TargetLinuxArm64] = PackageGeminiLinuxArm64,
            [TargetDarwinX64] = PackageGeminiDarwinX64,
            [TargetDarwinArm64] = PackageGeminiDarwinArm64,
            [TargetWindowsX64] = PackageGeminiWindowsX64,
            [TargetWindowsArm64] = PackageGeminiWindowsArm64,
        };

    public static string FindGeminiPath(string? geminiPathOverride)
    {
        if (!string.IsNullOrWhiteSpace(geminiPathOverride))
        {
            return geminiPathOverride;
        }

        if (TryResolveNpmInstalledBinary(out var resolvedPath))
        {
            return resolvedPath;
        }

        if (TryResolvePathExecutable(Environment.GetEnvironmentVariable(PathEnvironmentVariable), OperatingSystem.IsWindows(), out var pathExecutable))
        {
            return pathExecutable;
        }

        return OperatingSystem.IsWindows()
            ? GeminiWindowsExecutableName
            : GeminiExecutableName;
    }

    internal static bool TryResolvePathExecutable(string? pathVariable, bool isWindows, out string executablePath)
    {
        executablePath = string.Empty;

        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return false;
        }

        var candidateNames = GetPathExecutableCandidates(isWindows);
        foreach (var pathEntry in SplitPathVariable(pathVariable))
        {
            foreach (var candidateName in candidateNames)
            {
                var candidatePath = Path.Combine(pathEntry, candidateName);
                if (File.Exists(candidatePath))
                {
                    executablePath = candidatePath;
                    return true;
                }
            }
        }

        return false;
    }

    internal static IReadOnlyList<string> GetPathExecutableCandidates(bool isWindows)
    {
        return isWindows
            ? WindowsPathExecutableCandidates
            : UnixPathExecutableCandidates;
    }

    internal static string? GetCurrentTargetTriple()
    {
        return GetTargetTriple();
    }

    private static bool TryResolveNpmInstalledBinary(out string binaryPath)
    {
        binaryPath = string.Empty;

        var targetTriple = GetTargetTriple();
        if (targetTriple is null)
        {
            return false;
        }

        return TryResolveNpmInstalledBinary(EnumerateSearchRoots(), targetTriple, OperatingSystem.IsWindows(), out binaryPath);
    }

    internal static bool TryResolveNpmInstalledBinary(
        IEnumerable<string> searchRoots,
        string targetTriple,
        bool isWindows,
        out string binaryPath)
    {
        ArgumentNullException.ThrowIfNull(searchRoots);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTriple);

        if (!PlatformPackageByTarget.TryGetValue(targetTriple, out var packageName))
        {
            binaryPath = string.Empty;
            return false;
        }

        binaryPath = string.Empty;
        var executableName = isWindows ? GeminiWindowsExecutableName : GeminiExecutableName;

        foreach (var root in searchRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var primaryPath = Path.Combine(
                root,
                NodeModulesDirectory,
                GoogleScopeDirectory,
                packageName,
                VendorDirectory,
                targetTriple,
                TargetGeminiDirectory,
                executableName);

            if (File.Exists(primaryPath))
            {
                binaryPath = primaryPath;
                return true;
            }

            var nestedPath = Path.Combine(
                root,
                NodeModulesDirectory,
                GoogleScopeDirectory,
                NestedGeminiPackageDirectory,
                NodeModulesDirectory,
                GoogleScopeDirectory,
                packageName,
                VendorDirectory,
                targetTriple,
                TargetGeminiDirectory,
                executableName);

            if (File.Exists(nestedPath))
            {
                binaryPath = nestedPath;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SplitPathVariable(string pathVariable)
    {
        foreach (var rawPathEntry in pathVariable.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmedPathEntry = rawPathEntry.Trim('"');
            if (string.IsNullOrWhiteSpace(trimmedPathEntry))
            {
                continue;
            }

            yield return trimmedPathEntry;
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in EnumerateUpwards(Environment.CurrentDirectory))
        {
            if (seen.Add(root))
            {
                yield return root;
            }
        }

        foreach (var root in EnumerateUpwards(AppContext.BaseDirectory))
        {
            if (seen.Add(root))
            {
                yield return root;
            }
        }
    }

    private static IEnumerable<string> EnumerateUpwards(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            yield break;
        }

        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static string? GetTargetTriple()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => TargetLinuxX64,
                Architecture.Arm64 => TargetLinuxArm64,
                _ => null,
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => TargetDarwinX64,
                Architecture.Arm64 => TargetDarwinArm64,
                _ => null,
            };
        }

        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => TargetWindowsX64,
                Architecture.Arm64 => TargetWindowsArm64,
                _ => null,
            };
        }

        return null;
    }
}
