using System.Text.Json.Nodes;

namespace Assistent.Core.Tools;

public interface IToolHandler
{
    string Name { get; }
    string Description { get; }
    JsonObject ParametersJsonSchema { get; }
    Task<ToolExecutionResult> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken);
}
