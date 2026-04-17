using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Assistent.Core.Tools;

public sealed class GetSystemInfoHandler : IToolHandler
{
    private static readonly JsonObject ParametersSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {},
          "required": []
        }
        """)!.AsObject();

    private readonly ILogger<GetSystemInfoHandler> _logger;

    public GetSystemInfoHandler(ILogger<GetSystemInfoHandler> logger) => _logger = logger;

    public string Name => "get_system_info";

    public string Description =>
        "Returns OS version, machine name, and .NET runtime. No shell execution.";

    public JsonObject ParametersJsonSchema => ParametersSchema;

    public Task<ToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        var lines = new[]
        {
            $"OS: {Environment.OSVersion}",
            $"Machine: {Environment.MachineName}",
            $"User: {Environment.UserName}",
            $"64-bit OS: {Environment.Is64BitOperatingSystem}",
            $"Processor count: {Environment.ProcessorCount}",
            $"CLR: {Environment.Version}"
        };
        _logger.LogInformation("get_system_info invoked");
        return Task.FromResult(new ToolExecutionResult(true, string.Join("\n", lines)));
    }
}
