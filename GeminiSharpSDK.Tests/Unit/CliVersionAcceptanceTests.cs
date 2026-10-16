using ManagedCode.GeminiSharpSDK.Internal;

namespace ManagedCode.GeminiSharpSDK.Tests.Unit;

public sealed class CliVersionAcceptanceTests
{
    private const string OlderVersion = "0.63.0";
    private const string CurrentVersion = "0.64.0";
    private const string FutureVersion = "9999.0.1-preview.1";

    [Test]
    [Arguments(OlderVersion)]
    [Arguments(CurrentVersion)]
    [Arguments(FutureVersion)]
    public async Task InstalledVersionDoesNotNeedToMatchTheSdkTarget(string version)
    {
        var output = version;
        var observed = GeminiCliMetadataReader.ParseInstalledVersion(output);

        await Assert.That(observed).IsEqualTo(version);
    }
}
