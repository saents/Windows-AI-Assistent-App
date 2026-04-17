using System.Text.Json;
using System.Text.Json.Nodes;
using Assistent.Core.Json;
using Microsoft.Extensions.Logging;

namespace Assistent.Core.Tools;

public sealed class GetKnownFolderPathHandler : IToolHandler
{
    private static readonly JsonObject ParametersSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {
            "folder": {
              "type": "string",
              "enum": [
                "Desktop",
                "Documents",
                "Downloads",
                "Pictures",
                "Music",
                "Videos",
                "UserProfile",
                "LocalApplicationData",
                "ApplicationData",
                "Temp"
              ],
              "description": "Which well-known user folder to resolve."
            }
          },
          "required": ["folder"]
        }
        """)!.AsObject();

    private readonly ILogger<GetKnownFolderPathHandler> _logger;

    public GetKnownFolderPathHandler(ILogger<GetKnownFolderPathHandler> logger) => _logger = logger;

    public string Name => "get_known_folder_path";

    public string Description =>
        "Returns the absolute path of a standard user folder (Desktop, Documents, Downloads, etc.).";

    public JsonObject ParametersJsonSchema => ParametersSchema;

    public Task<ToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        if (!doc.RootElement.TryGetProperty("folder", out var fEl))
            return Task.FromResult(new ToolExecutionResult(false, "Missing folder."));

        var key = JsonElementText.ReadLooseString(fEl)?.Trim();
        if (string.IsNullOrEmpty(key))
            return Task.FromResult(new ToolExecutionResult(false, "Invalid folder."));

        try
        {
            var path = Resolve(key);
            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult(new ToolExecutionResult(false, "Folder path is not available on this system."));

            _logger.LogInformation("get_known_folder_path: {Folder} -> {Path}", key, path);
            return Task.FromResult(new ToolExecutionResult(true, path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "get_known_folder_path failed");
            return Task.FromResult(new ToolExecutionResult(false, ex.Message));
        }
    }

    private static string Resolve(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "userprofile" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "localapplicationdata" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "applicationdata" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "temp" => Path.GetTempPath(),
            _ => string.Empty
        };
    }
}
