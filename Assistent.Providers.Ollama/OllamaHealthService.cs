using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistent.Providers.Ollama;

public sealed class OllamaHealthService
{
    private readonly IHttpClientFactory _factory;
    private readonly OllamaOptions _options;
    private readonly IOllamaRuntimeOverride _runtime;
    private readonly ILogger<OllamaHealthService> _logger;

    public OllamaHealthService(
        IHttpClientFactory factory,
        IOptions<OllamaOptions> options,
        IOllamaRuntimeOverride runtime,
        ILogger<OllamaHealthService> logger)
    {
        _factory = factory;
        _options = options.Value;
        _runtime = runtime;
        _logger = logger;
    }

    public async Task<OllamaHealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var client = _factory.CreateClient("Ollama");
        client.Timeout = TimeSpan.FromSeconds(5);
        if (client.BaseAddress is null && Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri))
            client.BaseAddress = baseUri;

        var baseUrl = _runtime.BaseUriOverride ?? client.BaseAddress;
        if (baseUrl is null && Uri.TryCreate(_options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var fb))
            baseUrl = fb;
        var tagsUri = new Uri(baseUrl!, "/api/tags");

        try
        {
            using var response = await client.GetAsync(tagsUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new OllamaHealthResult(
                    false,
                    $"Ollama returned {(int)response.StatusCode}. Is Ollama running?");
            }

            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new OllamaHealthResult(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama health check failed");
            return new OllamaHealthResult(
                false,
                "Cannot reach Ollama. Install from https://ollama.com and run: ollama pull llama3.2");
        }
    }
}

public sealed record OllamaHealthResult(bool IsHealthy, string? UserMessage);
