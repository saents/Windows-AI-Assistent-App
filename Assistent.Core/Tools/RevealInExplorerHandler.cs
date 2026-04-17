using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Assistent.Core.Tools;

public sealed class RevealInExplorerHandler : IToolHandler
{
    private static readonly JsonObject ParametersSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "Absolute file or directory path to show in File Explorer."
            }
          },
          "required": ["path"]
        }
        """)!.AsObject();

    private readonly ILogger<RevealInExplorerHandler> _logger;

    public RevealInExplorerHandler(ILogger<RevealInExplorerHandler> logger) => _logger = logger;

    public string Name => "reveal_in_explorer";

    public string Description =>
        "Opens File Explorer focused on a file (selects it) or opens a folder at an absolute path.";

    public JsonObject ParametersJsonSchema => ParametersSchema;

    public Task<ToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        if (!doc.RootElement.TryGetProperty("path", out var pathEl))
            return Task.FromResult(new ToolExecutionResult(false, "Missing path."));

        var path = pathEl.GetString();
        if (!ToolFilesystemPaths.TryGetFullPath(path, out var full, out var err))
            return Task.FromResult(new ToolExecutionResult(false, err!));

        if (!File.Exists(full) && !Directory.Exists(full))
            return Task.FromResult(new ToolExecutionResult(false, "Path does not exist."));

        try
        {
            var args = File.Exists(full) ? $"/select,\"{full}\"" : $"\"{full}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = args,
                UseShellExecute = true
            });
            _logger.LogInformation("reveal_in_explorer: {Path}", full);
            return Task.FromResult(new ToolExecutionResult(true, $"Opened Explorer for: {full}"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "reveal_in_explorer failed");
            return Task.FromResult(new ToolExecutionResult(false, ex.Message));
        }
    }
}
