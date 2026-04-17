namespace Assistent.Core.Chat;

public sealed class ChatCompletionResult
{
    public string? MessageContent { get; init; }

    /// <summary>Optional model reasoning (e.g. Ollama message.thinking when think is enabled).</summary>
    public string? AssistantThinking { get; init; }

    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }
}
