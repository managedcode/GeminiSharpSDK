using ManagedCode.GeminiSharpSDK.Extensions.AI;
using ManagedCode.GeminiSharpSDK.Extensions.AI.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ManagedCode.GeminiSharpSDK.Extensions.AgentFramework.Extensions;

public static class GeminiAgentServiceCollectionExtensions
{
    public static IServiceCollection AddGeminiAIAgent(
        this IServiceCollection services,
        Action<GeminiChatClientOptions>? configureChatClient = null,
        Action<ChatClientAgentOptions>? configureAgent = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddGeminiChatClient(configureChatClient);
        services.AddSingleton<AIAgent>(serviceProvider => CreateAgent(serviceProvider, serviceKey: null, configureAgent));
        return services;
    }

    public static IServiceCollection AddKeyedGeminiAIAgent(
        this IServiceCollection services,
        object serviceKey,
        Action<GeminiChatClientOptions>? configureChatClient = null,
        Action<ChatClientAgentOptions>? configureAgent = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceKey);

        services.AddKeyedGeminiChatClient(serviceKey, configureChatClient);
        services.AddKeyedSingleton<AIAgent>(
            serviceKey,
            (serviceProvider, key) => CreateAgent(serviceProvider, key, configureAgent));
        return services;
    }

    private static ChatClientAgent CreateAgent(
        IServiceProvider serviceProvider,
        object? serviceKey,
        Action<ChatClientAgentOptions>? configureAgent)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var options = new ChatClientAgentOptions();
        configureAgent?.Invoke(options);

        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        var chatClient = serviceKey is null
            ? serviceProvider.GetRequiredService<IChatClient>()
            : serviceProvider.GetRequiredKeyedService<IChatClient>(serviceKey);

        return chatClient.AsAIAgent(options, loggerFactory, serviceProvider);
    }
}
