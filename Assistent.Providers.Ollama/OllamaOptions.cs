namespace Assistent.Providers.Ollama;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; init; } = "http://127.0.0.1:11434";
    public string Model { get; init; } = "llama3.2";
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(5);
}
