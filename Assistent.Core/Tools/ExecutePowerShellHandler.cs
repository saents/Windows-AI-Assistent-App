using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Assistent.Core.Json;
using Assistent.Core.Security;
using Microsoft.Extensions.Logging;

namespace Assistent.Core.Tools;

public sealed class ExecutePowerShellHandler : IToolHandler
{
    public const string ToolName = "execute_powershell";

    private static readonly JsonObject ParametersSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {
            "command": {
              "type": "string",
              "description": "A single PowerShell command or small script to run non-interactively."
            }
          },
          "required": ["command"]
        }
        """)!.AsObject();

    private const int MaxCommandLength = 8000;
    private const int MaxOutputLength = 12000;
    private const int ExitTimeoutMs = 120_000;

    private readonly ISecurityPreferences _security;
    private readonly IPowerShellConfirmation _confirmation;
    private readonly ILogger<ExecutePowerShellHandler> _logger;

    public ExecutePowerShellHandler(
        ISecurityPreferences security,
        IPowerShellConfirmation confirmation,
        ILogger<ExecutePowerShellHandler> logger)
    {
        _security = security;
        _confirmation = confirmation;
        _logger = logger;
    }

    public string Name => ToolName;

    public string Description =>
        "Runs one PowerShell command and returns stdout/stderr. Only when enabled in settings; may prompt for confirmation.";

    public JsonObject ParametersJsonSchema => ParametersSchema;

    public async Task<ToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        if (!_security.AllowPowerShellExecution)
            return new ToolExecutionResult(false, "PowerShell execution is disabled in settings.");

        using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
        if (!doc.RootElement.TryGetProperty("command", out var cmdEl))
            return new ToolExecutionResult(false, "Missing command.");

        var command = JsonElementText.ReadLooseString(cmdEl)?.Trim();
        if (string.IsNullOrEmpty(command))
            return new ToolExecutionResult(false, "Invalid command.");
        if (command.Length > MaxCommandLength)
            return new ToolExecutionResult(false, $"Command exceeds max length ({MaxCommandLength}).");

        var prefix = _security.PowerShellAllowlistPrefix;
        if (!string.IsNullOrWhiteSpace(prefix) &&
            !command.StartsWith(prefix.Trim(), StringComparison.OrdinalIgnoreCase))
            return new ToolExecutionResult(false, $"Command must start with allowlist prefix: \"{prefix.Trim()}\".");

        if (!await _confirmation.ConfirmAsync(command, cancellationToken).ConfigureAwait(false))
            return new ToolExecutionResult(false, "Cancelled by user.");

        return await Task.Run(() => RunProcess(command, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private ToolExecutionResult RunProcess(string command, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(command);

            using var proc = Process.Start(psi);
            if (proc is null)
                return new ToolExecutionResult(false, "Could not start powershell.exe.");

            using (cancellationToken.Register(() =>
                   {
                       try
                       {
                           if (!proc.HasExited)
                               proc.Kill(entireProcessTree: true);
                       }
                       catch
                       {
                           /* ignore */
                       }
                   }))
            {
                if (!proc.WaitForExit(ExitTimeoutMs))
                {
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        /* ignore */
                    }

                    return new ToolExecutionResult(false, "Command timed out.");
                }
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            var exit = proc.ExitCode;

            var combined = new StringBuilder();
            combined.AppendLine($"exitCode={exit}");
            if (!string.IsNullOrEmpty(stdout))
                combined.AppendLine(stdout);
            if (!string.IsNullOrEmpty(stderr))
                combined.AppendLine("stderr:");
            combined.Append(stderr);

            var text = combined.ToString();
            if (text.Length > MaxOutputLength)
                text = text[..MaxOutputLength] + "\n...[truncated]";

            _logger.LogWarning("execute_powershell ran (exit {Exit}): {Preview}", exit,
                command.Length > 120 ? command[..120] + "..." : command);

            return new ToolExecutionResult(true, text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "execute_powershell failed");
            return new ToolExecutionResult(false, ex.Message);
        }
    }
}
