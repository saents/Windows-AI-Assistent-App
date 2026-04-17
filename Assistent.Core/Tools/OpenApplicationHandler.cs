using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Assistent.Core.Json;
using Microsoft.Extensions.Logging;

namespace Assistent.Core.Tools;

public sealed class OpenApplicationHandler : IToolHandler
{
    private static readonly JsonObject ParametersSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {
            "application": {
              "type": "string",
              "enum": [
                "edge", "chrome", "firefox", "default_browser",
                "explorer", "file_explorer", "settings", "store", "notepad", "calculator",
                "task_manager", "control_panel", "paint", "terminal"
              ],
              "description": "Which built-in Windows app or browser to open."
            }
          },
          "required": ["application"]
        }
        """)!.AsObject();

    public const string ToolName = "open_application";

    private readonly ILogger<OpenApplicationHandler> _logger;

    public OpenApplicationHandler(ILogger<OpenApplicationHandler> logger) => _logger = logger;

    public string Name => ToolName;

    public string Description =>
        "Opens a browser or common Windows shell targets (File Explorer, Settings, Store, Notepad, Calculator, Task Manager, Control Panel, Paint, Terminal).";

    public JsonObject ParametersJsonSchema => ParametersSchema;

    public Task<ToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("application", out var appEl))
            return Task.FromResult(new ToolExecutionResult(false, "Missing application."));

        var app = JsonElementText.ReadLooseString(appEl)?.ToLowerInvariant().Trim();
        if (string.IsNullOrEmpty(app))
            return Task.FromResult(new ToolExecutionResult(false, "Invalid application."));

        try
        {
            switch (app)
            {
                case "edge":
                    StartUri("microsoft-edge:");
                    break;
                case "chrome":
                    StartFirstExisting(
                        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe");
                    break;
                case "firefox":
                    StartFirstExisting(
                        @"C:\Program Files\Mozilla Firefox\firefox.exe",
                        @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe");
                    break;
                case "default_browser":
                    StartUri("http://");
                    break;
                case "explorer":
                case "file_explorer":
                    StartShell("explorer.exe");
                    break;
                case "settings":
                    StartUri("ms-settings:");
                    break;
                case "store":
                    StartUri("ms-windows-store:");
                    break;
                case "notepad":
                    StartShell("notepad.exe");
                    break;
                case "calculator":
                    StartUri("ms-calculator:");
                    break;
                case "task_manager":
                    StartShell("taskmgr.exe");
                    break;
                case "control_panel":
                    StartShell("control.exe");
                    break;
                case "paint":
                    StartShell("mspaint.exe");
                    break;
                case "terminal":
                    OpenTerminal();
                    break;
                default:
                    return Task.FromResult(new ToolExecutionResult(false, "Application not allowed."));
            }

            _logger.LogInformation("Opened application: {App}", app);
            return Task.FromResult(new ToolExecutionResult(true, $"Opened {app}."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open {App}", app);
            return Task.FromResult(new ToolExecutionResult(false, ex.Message));
        }
    }

    private static void StartUri(string uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });
    }

    private static void StartShell(string fileName)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true
        });
    }

    private static void StartFirstExisting(params string?[] candidates)
    {
        foreach (var path in candidates)
        {
            if (string.IsNullOrEmpty(path))
                continue;
            if (File.Exists(path))
            {
                StartShell(path);
                return;
            }
        }

        StartUri("http://");
    }

    private static void OpenTerminal()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "wt.exe", UseShellExecute = true });
            return;
        }
        catch
        {
            /* fall back */
        }

        var localWt = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows Terminal", "wt.exe");
        if (File.Exists(localWt))
        {
            StartShell(localWt);
            return;
        }

        var programWt = @"C:\Program Files\Windows Terminal\wt.exe";
        if (File.Exists(programWt))
        {
            StartShell(programWt);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true
        });
    }
}
