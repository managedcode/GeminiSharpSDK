using ManagedCode.GeminiSharpSDK.Extensions.AgentFramework.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.GeminiSharpSDK.Tests.AgentFramework;

public class GeminiAgentServiceCollectionExtensionsTests
{
    private const string AgentDescription = "Agent description";
    private const string AgentInstructions = "You are a coding assistant.";
    private const string AgentName = "gemini-agent";
    private const string ConfiguredDefaultModel = "configured-default-model";
    private const string KeyedServiceName = "gemini-agent";

    [Test]
    public async Task AddGeminiAIAgent_ThrowsForNullServices()
    {
        ServiceCollection? services = null;

        var action = () => services!.AddGeminiAIAgent();

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ArgumentNullException>();
        await Assert.That(((ArgumentNullException)exception!).ParamName).IsEqualTo("services");
    }

    [Test]
    public async Task AddGeminiAIAgent_RegistersAIAgentAndChatClient()
    {
        var services = new ServiceCollection();
        services.AddGeminiAIAgent();

        var provider = services.BuildServiceProvider();
        var agent = provider.GetService<AIAgent>();
        var chatClient = provider.GetService<IChatClient>();
        var resolvedAgentChatClient = agent?.GetService(typeof(IChatClient)) as IChatClient;
        var metadata = resolvedAgentChatClient?.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;

        await Assert.That(agent).IsNotNull();
        await Assert.That(chatClient).IsNotNull();
        await Assert.That(resolvedAgentChatClient).IsNotNull();
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.ProviderName).IsEqualTo("GeminiCLI");
    }

    [Test]
    public async Task AddGeminiAIAgent_WithConfiguration_AppliesAgentOptions()
    {
        var services = new ServiceCollection();
        services.AddGeminiAIAgent(
            configureChatClient: options => options.DefaultModel = ConfiguredDefaultModel,
            configureAgent: options =>
            {
                options.Name = AgentName;
                options.Description = AgentDescription;
                options.ChatOptions = new ChatOptions
                {
                    Instructions = AgentInstructions,
                };
            });

        var provider = services.BuildServiceProvider();
        var agent = provider.GetRequiredService<AIAgent>();
        var chatClient = agent.GetService<IChatClient>();
        var metadata = chatClient?.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
        var agentOptions = agent.GetService<ChatClientAgentOptions>();

        await Assert.That(agent.Name).IsEqualTo(AgentName);
        await Assert.That(agent.Description).IsEqualTo(AgentDescription);
        await Assert.That(agentOptions).IsNotNull();
        await Assert.That(agentOptions!.ChatOptions).IsNotNull();
        await Assert.That(agentOptions.ChatOptions!.Instructions).IsEqualTo(AgentInstructions);
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.DefaultModelId).IsEqualTo(ConfiguredDefaultModel);
    }

    [Test]
    public async Task AddKeyedGeminiAIAgent_RegistersKeyedAgent()
    {
        var services = new ServiceCollection();
        services.AddKeyedGeminiAIAgent(KeyedServiceName);

        var provider = services.BuildServiceProvider();
        var agent = provider.GetKeyedService<AIAgent>(KeyedServiceName);
        var chatClient = provider.GetKeyedService<IChatClient>(KeyedServiceName);
        var resolvedAgentChatClient = agent?.GetService(typeof(IChatClient)) as IChatClient;
        var metadata = resolvedAgentChatClient?.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;

        await Assert.That(agent).IsNotNull();
        await Assert.That(chatClient).IsNotNull();
        await Assert.That(resolvedAgentChatClient).IsNotNull();
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.ProviderName).IsEqualTo("GeminiCLI");
    }

    [Test]
    public async Task AddKeyedGeminiAIAgent_ThrowsForNullServiceKey()
    {
        var services = new ServiceCollection();

        var action = () => services.AddKeyedGeminiAIAgent(serviceKey: null!);

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ArgumentNullException>();
        await Assert.That(((ArgumentNullException)exception!).ParamName).IsEqualTo("serviceKey");
    }

    [Test]
    public async Task AddKeyedGeminiAIAgent_WithConfiguration_AppliesKeyedAgentOptions()
    {
        var services = new ServiceCollection();
        services.AddKeyedGeminiAIAgent(
            KeyedServiceName,
            configureChatClient: options => options.DefaultModel = ConfiguredDefaultModel,
            configureAgent: options =>
            {
                options.Name = AgentName;
                options.ChatOptions = new ChatOptions
                {
                    Instructions = AgentInstructions,
                };
            });

        var provider = services.BuildServiceProvider();
        var agent = provider.GetRequiredKeyedService<AIAgent>(KeyedServiceName);
        var chatClient = agent.GetService<IChatClient>();
        var metadata = chatClient?.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
        var agentOptions = agent.GetService<ChatClientAgentOptions>();

        await Assert.That(agent.Name).IsEqualTo(AgentName);
        await Assert.That(agentOptions).IsNotNull();
        await Assert.That(agentOptions!.ChatOptions).IsNotNull();
        await Assert.That(agentOptions.ChatOptions!.Instructions).IsEqualTo(AgentInstructions);
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.DefaultModelId).IsEqualTo(ConfiguredDefaultModel);
    }
}
