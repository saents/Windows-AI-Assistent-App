using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Assistent.Core.Tools;

public sealed class ToolRunner
{
    private readonly IReadOnlyDictionary<string, IToolHandler> _handlers;
    private readonly ILogger<ToolRunner> _logger;

    public ToolRunner(IEnumerable<IToolHandler> handlers, ILogger<ToolRunner> logger)
    {
        _handlers = handlers.ToDictionary(h => h.Name, StringComparer.Ordinal);
        _logger = logger;
    }

    public IReadOnlyList<IToolHandler> Handlers => _handlers.Values.ToList();

    public async Task<ToolExecutionResult> RunAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(name, out var handler))
        {
            _logger.LogWarning("Unknown tool: {Tool}", name);
            return new ToolExecutionResult(false, $"Unknown tool: {name}");
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            return await handler.ExecuteAsync(doc.RootElement.GetRawText(), cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON for tool {Tool}", name);
            return new ToolExecutionResult(false, "Invalid tool arguments JSON.");
        }
    }
}
