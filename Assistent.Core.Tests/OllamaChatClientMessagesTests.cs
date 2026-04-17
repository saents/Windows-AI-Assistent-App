using System.Text.Json;
using System.Text.Json.Nodes;
using Assistent.Core.Chat;
using Assistent.Providers.Ollama;
using Xunit;

namespace Assistent.Core.Tests;

public sealed class OllamaChatClientMessagesTests
{
    [Fact]
    public void BuildMessages_serializes_user_assistant_and_tool_roles()
    {
        var messages = new List<ChatMessage>
        {
            new("system", "sys"),
            new("user", "hi"),
            new("assistant", "hello"),
            new("tool", "tool out", ToolCallId: "call-1", Name: "open_url")
        };

        var arr = OllamaChatClient.BuildMessages(messages);
        Assert.Equal(4, arr.Count);
        Assert.Equal("system", arr[0]!["role"]!.GetValue<string>());
        Assert.Equal("user", arr[1]!["role"]!.GetValue<string>());
        Assert.Equal("assistant", arr[2]!["role"]!.GetValue<string>());
        Assert.Equal("tool", arr[3]!["role"]!.GetValue<string>());
        Assert.Equal("call-1", arr[3]!["tool_call_id"]!.GetValue<string>());
        Assert.Equal("open_url", arr[3]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void ParseMessage_reads_text_reply()
    {
        using var doc = JsonDocument.Parse("""{"content":"ok","role":"assistant"}""");
        var result = OllamaChatClient.ParseMessage(doc.RootElement);
        Assert.Null(result.ToolCalls);
        Assert.Equal("ok", result.MessageContent);
    }

    [Fact]
    public void ParseMessage_reads_tool_calls_array()
    {
        var json = """
                   {"content":"","tool_calls":[{"id":"a","type":"function","function":{"name":"open_url","arguments":"{\"url\":\"https://x\"}"}}]}
                   """;
        using var doc = JsonDocument.Parse(json);
        var result = OllamaChatClient.ParseMessage(doc.RootElement);
        Assert.NotNull(result.ToolCalls);
        var call = Assert.Single(result.ToolCalls);
        Assert.Equal("open_url", call.Name);
        Assert.Contains("https://x", call.ArgumentsJson);
    }

    [Fact]
    public void ParseMessage_reads_thinking_with_tool_calls()
    {
        var json = """
                   {"content":"","thinking":"Pick Replit.","tool_calls":[{"id":"a","type":"function","function":{"name":"open_url","arguments":"{\"url\":\"https://replit.com\"}"}}]}
                   """;
        using var doc = JsonDocument.Parse(json);
        var result = OllamaChatClient.ParseMessage(doc.RootElement);
        Assert.Equal("Pick Replit.", result.AssistantThinking);
        Assert.NotNull(result.ToolCalls);
        Assert.Single(result.ToolCalls);
    }
}
