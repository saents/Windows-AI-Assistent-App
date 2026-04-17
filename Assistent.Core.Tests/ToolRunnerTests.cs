using Assistent.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Assistent.Core.Tests;

public sealed class ToolRunnerTests
{
    [Fact]
    public async Task RunAsync_unknown_tool_returns_failure()
    {
        var runner = new ToolRunner(Array.Empty<IToolHandler>(), NullLogger<ToolRunner>.Instance);
        var r = await runner.RunAsync("missing_tool", "{}", CancellationToken.None);
        Assert.False(r.Success);
        Assert.Contains("Unknown tool", r.Content, StringComparison.Ordinal);
    }
}
