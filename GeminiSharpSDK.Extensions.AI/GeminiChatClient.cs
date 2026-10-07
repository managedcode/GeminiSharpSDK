using System.Runtime.CompilerServices;
using ManagedCode.GeminiSharpSDK.Client;
using ManagedCode.GeminiSharpSDK.Configuration;
using ManagedCode.GeminiSharpSDK.Extensions.AI.Internal;
using Microsoft.Extensions.AI;

namespace ManagedCode.GeminiSharpSDK.Extensions.AI;

public sealed class GeminiChatClient : IChatClient
{
    private readonly GeminiClient _client;
    private readonly GeminiChatClientOptions _options;

    public GeminiChatClient(GeminiChatClientOptions? options = null)
    {
        _options = options ?? new GeminiChatClientOptions();
        _client = new GeminiClient(CreateCoreClientOptions(_options));
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var (prompt, imageContents) = ChatMessageMapper.ToGeminiInput(messages);
        var threadOptions = ChatOptionsMapper.ToThreadOptions(options, _options);
        var turnOptions = ChatOptionsMapper.ToTurnOptions(options, cancellationToken);

        var thread = options?.ConversationId is { } threadId
            ? _client.ResumeThread(threadId, threadOptions)
            : _client.StartThread(threadOptions);

        using (thread)
        {
            var userInput = ChatMessageMapper.BuildUserInput(prompt, imageContents);
            var result = await thread.RunAsync(userInput, turnOptions).ConfigureAwait(false);
            return ChatResponseMapper.ToChatResponse(result, thread.Id);
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var (prompt, imageContents) = ChatMessageMapper.ToGeminiInput(messages);
        var threadOptions = ChatOptionsMapper.ToThreadOptions(options, _options);
        var turnOptions = ChatOptionsMapper.ToTurnOptions(options, cancellationToken);

        var thread = options?.ConversationId is { } threadId
            ? _client.ResumeThread(threadId, threadOptions)
            : _client.StartThread(threadOptions);

        using (thread)
        {
            var userInput = ChatMessageMapper.BuildUserInput(prompt, imageContents);
            var streamed = await thread.RunStreamedAsync(userInput, turnOptions)
                .ConfigureAwait(false);

            await foreach (var update in StreamingEventMapper.ToUpdates(
                               streamed.Events,
                               _options.GeminiOptions?.MaximumProcessOutputCharacters ?? GeminiOptions.DefaultMaximumProcessOutputCharacters,
                               cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return update;
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(ChatClientMetadata))
        {
            return new ChatClientMetadata(
                providerName: "GeminiCLI",
                providerUri: null,
                defaultModelId: _options.DefaultModel);
        }

        if (serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return null;
    }

    internal static GeminiClientOptions CreateCoreClientOptions(GeminiChatClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new GeminiClientOptions
        {
            GeminiOptions = options.GeminiOptions,
            AutoStart = true,
        };
    }

    public void Dispose() => _client.Dispose();
}
