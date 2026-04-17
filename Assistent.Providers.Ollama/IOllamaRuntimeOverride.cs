namespace Assistent.Providers.Ollama;

public interface IOllamaRuntimeOverride
{
    Uri? BaseUriOverride { get; }
    string? ModelOverride { get; }

    /// <summary>Ollama <c>/api/chat</c> <c>think</c> flag (extended reasoning trace). When false, thinking-capable models skip it.</summary>
    bool OllamaThink { get; }
}
