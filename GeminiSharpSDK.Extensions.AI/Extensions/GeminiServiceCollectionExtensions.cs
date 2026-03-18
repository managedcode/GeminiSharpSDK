using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.GeminiSharpSDK.Extensions.AI.Extensions;

public static class GeminiServiceCollectionExtensions
{
    public static IServiceCollection AddGeminiChatClient(
        this IServiceCollection services,
        Action<GeminiChatClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new GeminiChatClientOptions();
        configure?.Invoke(options);
        services.AddSingleton<IChatClient>(new GeminiChatClient(options));
        return services;
    }

    public static IServiceCollection AddKeyedGeminiChatClient(
        this IServiceCollection services,
        object serviceKey,
        Action<GeminiChatClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceKey);

        var options = new GeminiChatClientOptions();
        configure?.Invoke(options);
        services.AddKeyedSingleton<IChatClient>(serviceKey, new GeminiChatClient(options));
        return services;
    }
}
