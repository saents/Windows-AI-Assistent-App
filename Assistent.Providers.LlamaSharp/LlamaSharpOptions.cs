namespace Assistent.Providers.LlamaSharp;

public sealed class LlamaSharpOptions
{
    public const string SectionName = "LlamaSharp";

    /// <summary>
    /// Path to a local .gguf file (absolute), or relative to the app base directory, or a file name only (resolved under <see cref="ModelsDirectory"/>).
    /// Leave empty to auto-pick the single <c>*.gguf</c> in <see cref="ModelsDirectory"/> next to the executable.
    /// </summary>
    public string ModelPath { get; init; } = "";

    /// <summary>Folder under the app base directory where you can place <c>.gguf</c> models (copied with the app from the project <c>Models</c> folder).</summary>
    public string ModelsDirectory { get; init; } = "Models";

    public uint ContextSize { get; init; } = 4096;
    public int GpuLayerCount { get; init; } = 0;
    public int MaxTokens { get; init; } = 512;
}
