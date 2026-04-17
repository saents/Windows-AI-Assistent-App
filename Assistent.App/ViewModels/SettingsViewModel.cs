using Assistent.App.Settings;
using Assistent.Core.Configuration;
using Assistent.Providers.LlamaSharp;
using Assistent.Providers.Ollama;
using Microsoft.Extensions.Options;

namespace Assistent.App.ViewModels;

public sealed class SettingsViewModel
{
    public SettingsViewModel(
        SecurityPreferences preferences,
        IOptions<AppOptions> app,
        IOptions<LlamaSharpOptions> llama,
        IOptions<OllamaOptions> ollama)
    {
        Preferences = preferences;
        ProviderDisplay = app.Value.Provider.ToString();
        DefaultOllamaTimeoutDisplay = ollama.Value.RequestTimeout.ToString();

        var probe = LlamaSharpModelPathResolver.Probe(llama.Value, AppContext.BaseDirectory);
        LlamaSharpModelsFolderDisplay = probe.ModelsRoot;
        LlamaSharpModelPathDisplay = probe.Kind switch
        {
            LlamaSharpModelProbeKind.Resolved => probe.ResolvedPath!,
            LlamaSharpModelProbeKind.MissingModelsFolder => probe.Detail,
            LlamaSharpModelProbeKind.NoGgufFiles => probe.Detail,
            LlamaSharpModelProbeKind.AmbiguousGguf => probe.Detail,
            LlamaSharpModelProbeKind.ModelPathNotFound => probe.Detail,
            _ => probe.Detail
        };
    }

    public SecurityPreferences Preferences { get; }
    public string ProviderDisplay { get; }
    /// <summary>Resolved <c>Models</c> directory (absolute).</summary>
    public string LlamaSharpModelsFolderDisplay { get; }
    /// <summary>Resolved GGUF path when valid; otherwise an explanatory message.</summary>
    public string LlamaSharpModelPathDisplay { get; }
    public string DefaultOllamaTimeoutDisplay { get; }
}
