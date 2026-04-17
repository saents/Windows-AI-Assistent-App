using Assistent.Core.Tools;

namespace Assistent.Core.Chat;

public interface IChatModelClient
{
    Task<ChatCompletionResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<IToolHandler>? tools,
        ChatCompletionOptions options,
        CancellationToken cancellationToken = default);
}
