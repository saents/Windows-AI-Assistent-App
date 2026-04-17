namespace Assistent.Core.Configuration;

public enum AssistantProviderKind
{
    Ollama,
    LlamaSharp
}

public sealed class AppOptions
{
    public const string SectionName = "Assistant";

    public AssistantProviderKind Provider { get; init; } = AssistantProviderKind.Ollama;
}
