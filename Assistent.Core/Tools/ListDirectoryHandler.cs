using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Assistent.Core.Tools;

public sealed class ListDirectoryHandler : IToolHandler
{
    private const int DefaultMaxEntries = 200;
    private const int HardMaxEntries = 500;

    private static readonly JsonObject ParametersSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "Absolute directory path on this machine (any accessible folder)."
            },
            "max_entries": {
              "type": "integer",
              "description": "Maximum entries to return (default 200, max 500)."
            },
            "include_hidden": {
              "type": "boolean",
              "description": "If true, include hidden/system entries (default false)."
            }
          },
          "required": ["path"]
        }
        """)!.AsObject();

    private readonly ILogger<ListDirectoryHandler> _logger;

    public ListDirectoryHandler(ILogger<ListDirectoryHandler> logger) => _logger = logger;

    public string Name => "list_directory";

    public string Description =>
        "Lists files and subdirectories in a folder (non-recursive, capped). Path must be absolute.";

    public JsonObject ParametersJsonSchema => ParametersSchema;

    public Task<ToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("path", out var pathEl))
            return Task.FromResult(new ToolExecutionResult(false, "Missing path."));

        var path = pathEl.GetString();
        if (!ToolFilesystemPaths.TryGetFullPath(path, out var full, out var err))
            return Task.FromResult(new ToolExecutionResult(false, err!));

        if (!Directory.Exists(full))
            return Task.FromResult(new ToolExecutionResult(false, "Directory not found."));

        var max = DefaultMaxEntries;
        if (root.TryGetProperty("max_entries", out var maxEl) && maxEl.TryGetInt32(out var m))
            max = Math.Clamp(m, 1, HardMaxEntries);

        var includeHidden = root.TryGetProperty("include_hidden", out var hidEl) && hidEl.ValueKind == JsonValueKind.True;

        try
        {
            var lines = new List<string>();
            var opts = new EnumerationOptions { IgnoreInaccessible = true };

            foreach (var entry in Directory.EnumerateFileSystemEntries(full, "*", opts))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (lines.Count >= max)
                {
                    lines.Add($"... truncated after {max} entries.");
                    break;
                }

                var name = Path.GetFileName(entry);
                if (!includeHidden && name.StartsWith('.'))
                    continue;

                if (!includeHidden && (File.GetAttributes(entry) & FileAttributes.Hidden) != 0)
                    continue;

                if (Directory.Exists(entry))
                {
                    var di = new DirectoryInfo(entry);
                    lines.Add($"[DIR]  {name}\t{di.LastWriteTimeUtc:O}");
                }
                else
                {
                    var fi = new FileInfo(entry);
                    lines.Add($"[FILE] {name}\t{fi.Length} bytes\t{fi.LastWriteTimeUtc:O}");
                }
            }

            _logger.LogInformation("list_directory: {Path}", full);
            return Task.FromResult(new ToolExecutionResult(true, string.Join("\n", lines)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "list_directory failed");
            return Task.FromResult(new ToolExecutionResult(false, ex.Message));
        }
    }
}
