using System.Linq;
using System.Text.Json;

namespace Assistent.Core.Json;

/// <summary>
/// Ollama / model outputs sometimes use objects or arrays where callers expect strings.
/// </summary>
public static class JsonElementText
{
    public static string ReadAssistantMessageContent(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Null => "",
            JsonValueKind.Array => string.Concat(el.EnumerateArray().Select(ReadAssistantMessageContent)),
            JsonValueKind.Object when el.TryGetProperty("text", out var t) => ReadAssistantMessageContent(t),
            JsonValueKind.Object => el.GetRawText(),
            _ => el.GetRawText()
        };

    /// <summary>Tool / function argument fields that should be a string but may be encoded as an object.</summary>
    public static string? ReadLooseString(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Object => ReadFirstStringInObject(el),
            _ => el.GetRawText()
        };

    private static string? ReadFirstStringInObject(JsonElement o)
    {
        foreach (var p in o.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String)
                return p.Value.GetString();
        }

        foreach (var p in o.EnumerateObject())
        {
            var s = ReadLooseString(p.Value);
            if (!string.IsNullOrWhiteSpace(s))
                return s;
        }

        return null;
    }
}
