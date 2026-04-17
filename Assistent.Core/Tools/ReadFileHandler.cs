using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Assistent.Core.Tools;

public sealed class ReadFileHandler : IToolHandler
{
    private const int MaxBytes = 65_536;

    private static readonly JsonObject ParametersSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "Absolute path to a file on this machine (any drive or share the app can access)."
            }
          },
          "required": ["path"]
        }
        """)!.AsObject();

    private readonly ILogger<ReadFileHandler> _logger;

    public ReadFileHandler(ILogger<ReadFileHandler> logger) => _logger = logger;

    public string Name => "read_file";

    public string Description =>
        "Reads a UTF-8 text file from an absolute path on this machine (max 64 KiB).";

    public JsonObject ParametersJsonSchema => ParametersSchema;

    public Task<ToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        if (!doc.RootElement.TryGetProperty("path", out var pathEl))
            return Task.FromResult(new ToolExecutionResult(false, "Missing path."));
        var path = pathEl.GetString();
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult(new ToolExecutionResult(false, "Invalid path."));

        if (!ToolFilesystemPaths.TryGetFullPath(path, out var full, out var guardErr))
            return Task.FromResult(new ToolExecutionResult(false, guardErr!));

        if (!File.Exists(full))
            return Task.FromResult(new ToolExecutionResult(false, "File not found."));

        try
        {
            var fi = new FileInfo(full);
            if (fi.Length > MaxBytes)
                return Task.FromResult(new ToolExecutionResult(false, $"File too large (max {MaxBytes} bytes)."));

            var text = File.ReadAllText(full);
            _logger.LogInformation("read_file: {Path}", full);
            return Task.FromResult(new ToolExecutionResult(true, text));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "read_file failed");
            return Task.FromResult(new ToolExecutionResult(false, ex.Message));
        }
    }
}
