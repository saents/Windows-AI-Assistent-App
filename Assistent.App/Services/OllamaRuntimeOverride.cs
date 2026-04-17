using Assistent.App.Settings;
using Assistent.Providers.Ollama;

namespace Assistent.App.Services;

public sealed class OllamaRuntimeOverride : IOllamaRuntimeOverride
{
    private readonly SecurityPreferences _prefs;

    public OllamaRuntimeOverride(SecurityPreferences prefs) => _prefs = prefs;

    public Uri? BaseUriOverride
    {
        get
        {
            var u = _prefs.OllamaBaseUrlOverride;
            if (string.IsNullOrWhiteSpace(u))
                return null;
            var trimmed = u.TrimEnd('/');
            return Uri.TryCreate(trimmed + "/", UriKind.Absolute, out var uri) ? uri : null;
        }
    }

    public string? ModelOverride => _prefs.OllamaModelOverride;

    public bool OllamaThink => _prefs.OllamaThink;
}
