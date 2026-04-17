using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Assistent.Core.Tools;

public sealed class OpenUrlHandler : IToolHandler
{
    private static readonly JsonObject ParametersSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {
            "url": {
              "type": "string",
              "description": "HTTP or HTTPS URL to open in the default browser."
            }
          },
          "required": ["url"]
        }
        """)!.AsObject();

    public const string ToolName = "open_url";

    private readonly ILogger<OpenUrlHandler> _logger;

    public OpenUrlHandler(ILogger<OpenUrlHandler> logger) => _logger = logger;

    public string Name => ToolName;

    public string Description => "Opens a web URL in the default browser. Only http and https are allowed.";

    public JsonObject ParametersJsonSchema => ParametersSchema;

    public Task<ToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
        if (!doc.RootElement.TryGetProperty("url", out var urlEl))
            return Task.FromResult(new ToolExecutionResult(false, "Missing url."));

        var urlString = urlEl.GetString();
        if (string.IsNullOrWhiteSpace(urlString))
            return Task.FromResult(new ToolExecutionResult(false, "Invalid url."));

        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
            return Task.FromResult(new ToolExecutionResult(false, "Could not parse URL."));

        if (uri.Scheme is not ("http" or "https"))
            return Task.FromResult(new ToolExecutionResult(false, "Only http and https URLs are allowed."));

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            });
            _logger.LogInformation("Opened URL: {Url}", uri);
            return Task.FromResult(new ToolExecutionResult(true, $"Opened {uri}."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open URL");
            return Task.FromResult(new ToolExecutionResult(false, ex.Message));
        }
    }
}
