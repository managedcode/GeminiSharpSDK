using ManagedCode.GeminiSharpSDK.Client;
using ManagedCode.GeminiSharpSDK.Extensions.AI.Internal;
using Microsoft.Extensions.AI;

namespace ManagedCode.GeminiSharpSDK.Extensions.AI.Tests;

public class ChatOptionsMapperTests
{
    [Test]
    public async Task ToThreadOptions_NullOptions_UsesDefaults()
    {
        var clientOptions = new GeminiChatClientOptions { DefaultModel = "test-model" };
        var result = ChatOptionsMapper.ToThreadOptions(null, clientOptions);
        await Assert.That(result.Model).IsEqualTo("test-model");
    }

    [Test]
    public async Task ToThreadOptions_ModelId_MapsToModel()
    {
        var chatOptions = new ChatOptions { ModelId = "gpt-5" };
        var clientOptions = new GeminiChatClientOptions { DefaultModel = "default" };
        var result = ChatOptionsMapper.ToThreadOptions(chatOptions, clientOptions);
        await Assert.That(result.Model).IsEqualTo("gpt-5");
    }

    [Test]
    public async Task ToThreadOptions_AdditionalProperties_MapsGeminiKeys()
    {
        var chatOptions = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatOptionsMapper.SandboxModeKey] = SandboxMode.WorkspaceWrite,
                [ChatOptionsMapper.FullAutoKey] = true,
                [ChatOptionsMapper.ProfileKey] = "strict",
                [ChatOptionsMapper.ReasoningEffortKey] = ModelReasoningEffort.High,
            },
        };
        var result = ChatOptionsMapper.ToThreadOptions(chatOptions, new GeminiChatClientOptions());
        await Assert.That(result.SandboxMode).IsEqualTo(SandboxMode.WorkspaceWrite);
        await Assert.That(result.FullAuto).IsTrue();
        await Assert.That(result.Profile).IsEqualTo("strict");
        await Assert.That(result.ModelReasoningEffort).IsEqualTo(ModelReasoningEffort.High);
    }

    [Test]
    public async Task ToTurnOptions_SetsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var result = ChatOptionsMapper.ToTurnOptions(null, cts.Token);
        await Assert.That(result.CancellationToken).IsEqualTo(cts.Token);
    }
}
