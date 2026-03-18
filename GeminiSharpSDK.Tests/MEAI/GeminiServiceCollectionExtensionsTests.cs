using ManagedCode.GeminiSharpSDK.Extensions.AI.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.GeminiSharpSDK.Extensions.AI.Tests;

public class GeminiServiceCollectionExtensionsTests
{
    private const string ConfiguredDefaultModel = "configured-default-model";

    [Test]
    public async Task AddGeminiChatClient_RegistersIChatClient()
    {
        var services = new ServiceCollection();
        services.AddGeminiChatClient();
        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IChatClient>();
        await Assert.That(client).IsNotNull();
        await Assert.That(client).IsTypeOf<GeminiChatClient>();
    }

    [Test]
    public async Task AddGeminiChatClient_WithConfiguration_RegistersIChatClient()
    {
        var services = new ServiceCollection();
        services.AddGeminiChatClient(options => options.DefaultModel = ConfiguredDefaultModel);
        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IChatClient>();
        await Assert.That(client).IsNotNull();

        var metadata = client!.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.DefaultModelId).IsEqualTo(ConfiguredDefaultModel);
    }

    [Test]
    public async Task AddKeyedGeminiChatClient_RegistersWithKey()
    {
        var services = new ServiceCollection();
        services.AddKeyedGeminiChatClient("gemini");
        var provider = services.BuildServiceProvider();
        var client = provider.GetKeyedService<IChatClient>("gemini");
        await Assert.That(client).IsNotNull();
    }

    [Test]
    public async Task AddKeyedGeminiChatClient_WithConfiguration_AppliesConfiguredDefaultModel()
    {
        var services = new ServiceCollection();
        services.AddKeyedGeminiChatClient("gemini", options => options.DefaultModel = ConfiguredDefaultModel);
        var provider = services.BuildServiceProvider();
        var client = provider.GetKeyedService<IChatClient>("gemini");

        await Assert.That(client).IsNotNull();

        var metadata = client!.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.DefaultModelId).IsEqualTo(ConfiguredDefaultModel);
    }
}
