namespace Assistent.Core.Chat;

public sealed record ChatMessage(
    string Role,
    string? Content,
    string? ToolCallId = null,
    string? Name = null,
    IReadOnlyList<ToolCall>? AssistantToolCalls = null,
    string? AssistantThinking = null);
