using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Assistent.Core.Tools;

public sealed class FindFilesHandler : IToolHandler
{
    private const int DefaultMaxResults = 50;
    private const int HardMaxResults = 200;
    private const int DefaultMaxDepth = 6;

    private static readonly JsonObject ParametersSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {
            "root_directory": {
              "type": "string",
              "description": "Optional absolute path of the folder to search (any drive). Defaults to the current user's profile folder."
            },
            "name_contains": {
              "type": "string",
              "description": "Optional case-insensitive substring that the file name must contain."
            },
            "extension": {
              "type": "string",
              "description": "Optional extension filter including dot, e.g. .md or .txt"
            },
            "max_depth": {
              "type": "integer",
              "description": "Maximum directory depth from root (default 6)."
            },
            "max_results": {
              "type": "integer",
              "description": "Maximum file paths to return (default 50, max 200)."
            }
          },
          "required": []
        }
        """)!.AsObject();

    private readonly ILogger<FindFilesHandler> _logger;

    public FindFilesHandler(ILogger<FindFilesHandler> logger) => _logger = logger;

    public string Name => "find_files";

    public string Description =>
        "Recursively finds files from an absolute root path (depth-limited). Optional name substring and extension filter.";

    public JsonObject ParametersJsonSchema => ParametersSchema;

    public Task<ToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        var rootArg = root.TryGetProperty("root_directory", out var rd) ? rd.GetString() : null;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var start = string.IsNullOrWhiteSpace(rootArg) ? profile : rootArg;
        if (!ToolFilesystemPaths.TryGetFullPath(start, out var rootDir, out var err))
            return Task.FromResult(new ToolExecutionResult(false, err!));

        if (!Directory.Exists(rootDir))
            return Task.FromResult(new ToolExecutionResult(false, "root_directory is not a directory."));

        var maxResults = DefaultMaxResults;
        if (root.TryGetProperty("max_results", out var mrEl) && mrEl.TryGetInt32(out var mr))
            maxResults = Math.Clamp(mr, 1, HardMaxResults);

        var maxDepth = DefaultMaxDepth;
        if (root.TryGetProperty("max_depth", out var mdEl) && mdEl.TryGetInt32(out var md))
            maxDepth = Math.Clamp(md, 1, 20);

        string? contains = null;
        if (root.TryGetProperty("name_contains", out var ncEl))
            contains = ncEl.GetString();

        string? ext = null;
        if (root.TryGetProperty("extension", out var exEl))
        {
            ext = exEl.GetString();
            if (!string.IsNullOrEmpty(ext) && !ext.StartsWith('.'))
                ext = "." + ext;
        }

        var results = new List<string>();
        try
        {
            Walk(rootDir, 0, maxDepth, contains, ext, results, maxResults, cancellationToken);
            _logger.LogInformation("find_files: root={Root} matches={Count}", rootDir, results.Count);
            return Task.FromResult(results.Count == 0
                ? new ToolExecutionResult(true, "(no files matched)")
                : new ToolExecutionResult(true, string.Join("\n", results)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "find_files failed");
            return Task.FromResult(new ToolExecutionResult(false, ex.Message));
        }
    }

    private static void Walk(
        string directory,
        int depth,
        int maxDepth,
        string? nameContains,
        string? extension,
        List<string> results,
        int maxResults,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (results.Count >= maxResults || depth > maxDepth)
            return;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*", new EnumerationOptions { IgnoreInaccessible = true });
        }
        catch
        {
            return;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= maxResults)
                return;

            if (!ToolFilesystemPaths.TryGetFullPath(file, out var full, out _))
                continue;

            var name = Path.GetFileName(full);
            if (!string.IsNullOrEmpty(nameContains)
                && name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (!string.IsNullOrEmpty(extension)
                && !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(full);
        }

        if (depth >= maxDepth || results.Count >= maxResults)
            return;

        IEnumerable<string> subdirs;
        try
        {
            subdirs = Directory.EnumerateDirectories(directory, "*", new EnumerationOptions { IgnoreInaccessible = true });
        }
        catch
        {
            return;
        }

        foreach (var sub in subdirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Walk(sub, depth + 1, maxDepth, nameContains, extension, results, maxResults, cancellationToken);
            if (results.Count >= maxResults)
                return;
        }
    }
}
