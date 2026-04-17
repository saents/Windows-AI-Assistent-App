using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using Assistent.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Assistent.App.Tools;

/// <summary>Reads plain text from the Windows clipboard (WPF STA thread).</summary>
public sealed class ClipboardReadToolHandler : IToolHandler
{
    private const int MaxChars = 32_000;

    private static readonly JsonObject ParametersSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {},
          "required": []
        }
        """)!.AsObject();

    private readonly ILogger<ClipboardReadToolHandler> _logger;

    public ClipboardReadToolHandler(ILogger<ClipboardReadToolHandler> logger) => _logger = logger;

    public string Name => "read_clipboard_text";

    public string Description =>
        "Returns plain text from the Windows clipboard if present (truncated if very long). Requires the desktop UI.";

    public JsonObject ParametersJsonSchema => ParametersSchema;

    public async Task<ToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var app = Application.Current;
        if (app?.Dispatcher is null)
            return new ToolExecutionResult(false, "Clipboard is unavailable (no WPF application).");

        return await app.Dispatcher.InvokeAsync(
                () =>
                {
                    try
                    {
                        if (!Clipboard.ContainsText())
                            return new ToolExecutionResult(true, "(clipboard does not contain text)");

                        var text = Clipboard.GetText(TextDataFormat.UnicodeText) ?? string.Empty;
                        if (text.Length > MaxChars)
                            text = text[..MaxChars] + "\n... (truncated)";

                        _logger.LogInformation("read_clipboard_text: {Length} chars", text.Length);
                        return new ToolExecutionResult(true, text);
                    }
                    catch (Exception ex)
                    {
                        return new ToolExecutionResult(false, ex.Message);
                    }
                },
                DispatcherPriority.Send)
            .Task.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
