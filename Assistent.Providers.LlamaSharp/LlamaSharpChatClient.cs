using System.Linq;
using System.Text;
using System.Text.Json;
using Assistent.Core.Chat;
using Assistent.Core.Tools;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistent.Providers.LlamaSharp;

/// <summary>
/// Local GGUF inference via LLamaSharp. Tool calls use a JSON envelope in the model output (no native tool API).
/// </summary>
public sealed class LlamaSharpChatClient : IChatModelClient, IDisposable
{
    private readonly LlamaSharpOptions _options;
    private readonly ILogger<LlamaSharpChatClient> _logger;
    private readonly object _gate = new();
    private LLamaWeights? _weights;
    private StatelessExecutor? _executor;

    public LlamaSharpChatClient(IOptions<LlamaSharpOptions> options, ILogger<LlamaSharpChatClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _executor = null;
            _weights?.Dispose();
            _weights = null;
        }
    }

    public async Task<ChatCompletionResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<IToolHandler>? tools,
        ChatCompletionOptions options,
        CancellationToken cancellationToken = default)
    {
        EnsureLoaded();
        var prompt = BuildPrompt(messages, tools);
        var inference = new InferenceParams
        {
            MaxTokens = _options.MaxTokens,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = options.Temperature
            }
        };

        var sb = new StringBuilder();
        var canStream = options.StreamingContent is not null && (tools is null || tools.Count == 0);
        await foreach (var chunk in _executor!.InferAsync(prompt, inference, cancellationToken).ConfigureAwait(false))
        {
            sb.Append(chunk);
            if (canStream)
                options.StreamingContent!.Report(chunk);
        }

        var raw = sb.ToString().Trim();
        return ParseLocalEnvelope(raw, tools);
    }

    private void EnsureLoaded()
    {
        lock (_gate)
        {
            if (_executor is not null)
                return;

            var resolvedPath = LlamaSharpModelPathResolver.ResolveForLoad(_options, AppContext.BaseDirectory);

            var modelParams = new ModelParams(resolvedPath)
            {
                ContextSize = _options.ContextSize,
                GpuLayerCount = _options.GpuLayerCount
            };

            try
            {
                _weights = LLamaWeights.LoadFromFile(modelParams);
            }
            catch (Exception ex) when (ex.Message.Contains("unknown model architecture", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "This GGUF uses a model architecture that the bundled LLamaSharp / llama.cpp build does not support yet " +
                    "(for example Qwen 3.5 / qwen35). Try: update LLamaSharp when a newer release ships, run the same model through Ollama instead, " +
                    "or use a GGUF built for an architecture your LLamaSharp version already supports. " +
                    $"Native error: {ex.Message}",
                    ex);
            }

            _executor = new StatelessExecutor(_weights, modelParams, logger: null);
            _logger.LogInformation("LlamaSharp loaded model from {Path}", resolvedPath);
        }
    }

    private static string BuildPrompt(IReadOnlyList<ChatMessage> messages, IReadOnlyList<IToolHandler>? tools)
    {
        var sb = new StringBuilder();
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case "system":
                    sb.AppendLine($"System:\n{m.Content}\n");
                    break;
                case "user":
                    sb.AppendLine($"User:\n{m.Content}\n");
                    break;
                case "assistant":
                    if (m.AssistantToolCalls is { Count: > 0 })
                    {
                        sb.AppendLine("Assistant requested tools:");
                        foreach (var c in m.AssistantToolCalls)
                            sb.AppendLine($"  - {c.Name} {c.ArgumentsJson}");
                    }
                    else
                    {
                        sb.AppendLine($"Assistant:\n{m.Content}\n");
                    }

                    break;
                case "tool":
                    sb.AppendLine($"Tool ({m.Name}):\n{m.Content}\n");
                    break;
            }
        }

        if (tools is { Count: > 0 })
        {
            sb.AppendLine(
                """
                
                Respond with ONLY one JSON object (no markdown fences, no extra text). Schema:
                {"message":"your reply to the user","tool_calls":[{"id":"1","name":"tool_name","arguments":{}}]}
                Use "tool_calls":[] if no tool is needed. Arguments must match the tool schema.
                Valid tool names:
                
                """);
            sb.AppendLine(string.Join(", ", tools.Select(t => t.Name)));
        }

        sb.AppendLine("JSON:");
        return sb.ToString();
    }

    private static ChatCompletionResult ParseLocalEnvelope(string text, IReadOnlyList<IToolHandler>? tools)
    {
        if (tools is null || tools.Count == 0)
            return new ChatCompletionResult { MessageContent = text };

        var json = TryExtractJsonObject(text);
        if (json is null)
            return new ChatCompletionResult { MessageContent = text };

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() ?? "" : text;

            var calls = new List<ToolCall>();
            if (root.TryGetProperty("tool_calls", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var el in arr.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : $"local{i++}";
                    if (!el.TryGetProperty("name", out var nameEl))
                        continue;
                    var name = nameEl.GetString() ?? "";
                    string argsJson = "{}";
                    if (el.TryGetProperty("arguments", out var argsEl))
                    {
                        argsJson = argsEl.ValueKind == JsonValueKind.String
                            ? argsEl.GetString() ?? "{}"
                            : argsEl.GetRawText();
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                        calls.Add(new ToolCall(id ?? Guid.NewGuid().ToString("N"), name, argsJson));
                }
            }

            return new ChatCompletionResult
            {
                MessageContent = message,
                ToolCalls = calls.Count > 0 ? calls : null
            };
        }
        catch
        {
            return new ChatCompletionResult { MessageContent = text };
        }
    }

    private static string? TryExtractJsonObject(string text)
    {
        var s = text.Trim();
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        return s[start..(end + 1)];
    }
}
