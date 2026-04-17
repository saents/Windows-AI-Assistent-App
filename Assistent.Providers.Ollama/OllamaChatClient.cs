using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Assistent.Core.Chat;
using Assistent.Core.Json;
using Assistent.Core.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistent.Providers.Ollama;

public sealed class OllamaChatClient : IChatModelClient
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly IOllamaRuntimeOverride _runtime;
    private readonly ILogger<OllamaChatClient> _logger;

    public OllamaChatClient(
        HttpClient http,
        IOptions<OllamaOptions> options,
        IOllamaRuntimeOverride runtime,
        ILogger<OllamaChatClient> logger)
    {
        _http = http;
        _options = options.Value;
        _runtime = runtime;
        _logger = logger;
        if (_http.BaseAddress is null && Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri))
            _http.BaseAddress = baseUri;
        _http.Timeout = _options.RequestTimeout;
    }

    public async Task<ChatCompletionResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<IToolHandler>? tools,
        ChatCompletionOptions options,
        CancellationToken cancellationToken = default)
    {
        var useStream = options.StreamingContent is not null && (tools is null || tools.Count == 0);
        var body = BuildRequestBody(messages, tools, options, stream: useStream);
        var json = body.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

        if (useStream)
            return await CompleteStreamingAsync(json, options, cancellationToken).ConfigureAwait(false);

        return await CompleteNonStreamingWithRetriesAsync(json, options, cancellationToken).ConfigureAwait(false);
    }

    private JsonObject BuildRequestBody(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<IToolHandler>? tools,
        ChatCompletionOptions options,
        bool stream)
    {
        var model = string.IsNullOrWhiteSpace(_runtime.ModelOverride) ? options.Model : _runtime.ModelOverride!;
        var body = new JsonObject
        {
            ["model"] = model,
            ["stream"] = stream,
            ["messages"] = BuildMessages(messages),
            ["options"] = new JsonObject { ["temperature"] = options.Temperature }
        };

        if (tools is { Count: > 0 })
            body["tools"] = BuildToolDefinitions(tools);

        body["think"] = _runtime.OllamaThink;

        return body;
    }

    private async Task<ChatCompletionResult> CompleteNonStreamingWithRetriesAsync(
        string jsonBody,
        ChatCompletionOptions options,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                using var response = await SendPostAsync(content, cancellationToken).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                var statusCode = (int)response.StatusCode;
                if ((statusCode == 429 || statusCode >= 500) && attempt < maxAttempts)
                {
                    await Task.Delay(250 * attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Ollama error {Status}: {Body}", response.StatusCode, json);
                    throw new InvalidOperationException($"Ollama request failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                _logger.LogInformation("Ollama /api/chat response: {Body}", json);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("message", out var message))
                    return new ChatCompletionResult { MessageContent = string.Empty };

                var parsed = ParseMessage(message);
                ReportThinkingOnce(options, parsed);
                return parsed;
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is HttpRequestException or TaskCanceledException or SocketException)
            {
                last = ex;
                await Task.Delay(250 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        throw last ?? new InvalidOperationException("Ollama request failed after retries.");
    }

    private async Task<HttpResponseMessage> SendPostAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var uri = BuildChatUri();
        return await _http.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
    }

    private Uri BuildChatUri()
    {
        var o = _runtime.BaseUriOverride ?? _http.BaseAddress;
        if (o is null && Uri.TryCreate(_options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var fb))
            o = fb;
        return new Uri(o!, "/api/chat");
    }

    private async Task<ChatCompletionResult> CompleteStreamingAsync(
        string jsonBody,
        ChatCompletionOptions options,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildChatUri()) { Content = content };
        using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("Ollama stream error {Status}: {Body}", response.StatusCode, err);
            throw new InvalidOperationException($"Ollama request failed: {(int)response.StatusCode}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        string? lastLine = null;
        var thinkingSeenInStream = false;
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                continue;
            lastLine = line;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msg))
            {
                if (msg.TryGetProperty("content", out var ce))
                {
                    var delta = JsonElementText.ReadAssistantMessageContent(ce);
                    if (!string.IsNullOrEmpty(delta))
                        options.StreamingContent!.Report(delta);
                }

                if (msg.TryGetProperty("thinking", out var th))
                {
                    var tDelta = JsonElementText.ReadAssistantMessageContent(th);
                    if (!string.IsNullOrEmpty(tDelta))
                    {
                        options.StreamingThinking?.Report(tDelta);
                        thinkingSeenInStream = true;
                    }
                }
            }
        }

        if (lastLine is null)
            return new ChatCompletionResult { MessageContent = string.Empty };

        using var finalDoc = JsonDocument.Parse(lastLine);
        if (!finalDoc.RootElement.TryGetProperty("message", out var finalMsg))
            return new ChatCompletionResult { MessageContent = string.Empty };

        var final = ParseMessage(finalMsg);
        if (options.StreamingThinking is not null
            && !thinkingSeenInStream
            && !string.IsNullOrEmpty(final.AssistantThinking))
            options.StreamingThinking.Report(final.AssistantThinking!);

        return final;
    }

    private static void ReportThinkingOnce(ChatCompletionOptions options, ChatCompletionResult result)
    {
        if (options.StreamingThinking is null || string.IsNullOrEmpty(result.AssistantThinking))
            return;
        options.StreamingThinking.Report(result.AssistantThinking);
    }

    internal static JsonArray BuildMessages(IReadOnlyList<ChatMessage> messages)
    {
        var arr = new JsonArray();
        foreach (var m in messages)
        {
            var obj = new JsonObject { ["role"] = m.Role };

            if (m.Role == "assistant" && m.AssistantToolCalls is { Count: > 0 })
            {
                if (!string.IsNullOrEmpty(m.Content))
                    obj["content"] = m.Content;
                else
                    obj["content"] = string.Empty;

                var calls = new JsonArray();
                foreach (var (tc, idx) in m.AssistantToolCalls.Select((tc, i) => (tc, i)))
                {
                    var fn = new JsonObject
                    {
                        ["index"] = idx,
                        ["name"] = tc.Name,
                        ["arguments"] = ParseArgumentsNode(tc.ArgumentsJson)
                    };
                    var callObj = new JsonObject
                    {
                        ["type"] = "function",
                        ["function"] = fn
                    };
                    if (!string.IsNullOrEmpty(tc.Id))
                        callObj["id"] = tc.Id;
                    calls.Add(callObj);
                }

                obj["tool_calls"] = calls;
            }
            else if (m.Role == "tool")
            {
                obj["content"] = m.Content ?? string.Empty;
                if (!string.IsNullOrEmpty(m.ToolCallId))
                    obj["tool_call_id"] = m.ToolCallId;
                if (!string.IsNullOrEmpty(m.Name))
                    obj["name"] = m.Name;
            }
            else
            {
                obj["content"] = m.Content ?? string.Empty;
            }

            arr.Add(obj);
        }

        return arr;
    }

    private static JsonNode ParseArgumentsNode(string? argumentsJson)
    {
        var raw = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson.Trim();
        try
        {
            return JsonNode.Parse(raw) ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static JsonArray BuildToolDefinitions(IReadOnlyList<IToolHandler> tools)
    {
        var arr = new JsonArray();
        foreach (var t in tools)
        {
            arr.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = t.ParametersJsonSchema.DeepClone()
                }
            });
        }

        return arr;
    }

    internal static ChatCompletionResult ParseMessage(JsonElement message)
    {
        string? content = null;
        if (message.TryGetProperty("content", out var contentEl))
            content = JsonElementText.ReadAssistantMessageContent(contentEl);

        string? assistantThinking = null;
        if (message.TryGetProperty("thinking", out var thinkingEl))
        {
            var raw = JsonElementText.ReadAssistantMessageContent(thinkingEl);
            assistantThinking = string.IsNullOrEmpty(raw) ? null : raw;
        }

        if (!message.TryGetProperty("tool_calls", out var toolCallsEl) || toolCallsEl.ValueKind != JsonValueKind.Array)
            return new ChatCompletionResult
            {
                MessageContent = content ?? string.Empty,
                AssistantThinking = assistantThinking
            };

        var calls = new List<ToolCall>();
        foreach (var call in toolCallsEl.EnumerateArray())
        {
            string? id = null;
            if (call.TryGetProperty("id", out var idEl))
                id = JsonElementText.ReadLooseString(idEl);

            if (!call.TryGetProperty("function", out var fn))
                continue;

            if (!fn.TryGetProperty("name", out var nameEl))
                continue;
            var name = JsonElementText.ReadLooseString(nameEl) ?? string.Empty;

            string argsJson = "{}";
            if (fn.TryGetProperty("arguments", out var argsEl))
            {
                argsJson = argsEl.ValueKind switch
                {
                    JsonValueKind.String => argsEl.GetString() ?? "{}",
                    JsonValueKind.Object => argsEl.GetRawText(),
                    _ => "{}"
                };
            }

            calls.Add(new ToolCall(id ?? Guid.NewGuid().ToString("N"), name, argsJson));
        }

        return new ChatCompletionResult
        {
            MessageContent = content ?? string.Empty,
            AssistantThinking = assistantThinking,
            ToolCalls = calls.Count > 0 ? calls : null
        };
    }
}
