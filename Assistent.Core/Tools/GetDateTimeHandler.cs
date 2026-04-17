using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Assistent.Core.Tools;

public sealed class GetDateTimeHandler : IToolHandler
{
    private static readonly JsonObject ParametersSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {},
          "required": []
        }
        """)!.AsObject();

    private readonly ILogger<GetDateTimeHandler> _logger;

    public GetDateTimeHandler(ILogger<GetDateTimeHandler> logger) => _logger = logger;

    public string Name => "get_datetime";

    public string Description =>
        "Returns the current local and UTC date/time and the local time zone id (no network access).";

    public JsonObject ParametersJsonSchema => ParametersSchema;

    public Task<ToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var local = DateTimeOffset.Now;
        var utc = DateTimeOffset.UtcNow;
        var tz = TimeZoneInfo.Local;
        var lines = new[]
        {
            $"Local: {local:O}",
            $"Time zone: {tz.Id} (base UTC offset {tz.BaseUtcOffset})",
            $"UTC:   {utc:O}",
            $"Culture: {System.Globalization.CultureInfo.CurrentCulture.Name}"
        };
        _logger.LogInformation("get_datetime invoked");
        return Task.FromResult(new ToolExecutionResult(true, string.Join("\n", lines)));
    }
}
