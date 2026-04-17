using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Assistent.App.Settings;
using Assistent.Core.Assistant;
using Assistent.Core.Chat;
using Assistent.App.Services;
using Assistent.Core.Configuration;
using Assistent.Providers.Ollama;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Assistent.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AssistantOrchestrator _orchestrator;
    private readonly OllamaHealthService _health;
    private readonly OllamaHostRecovery _ollamaHostRecovery;
    private readonly SecurityPreferences _security;
    private readonly IOptions<OllamaOptions> _ollama;
    private readonly IOptions<AppOptions> _app;
    private readonly IServiceProvider _services;
    private readonly List<ChatMessage> _conversation = new();
    private CancellationTokenSource? _sendCts;

    public MainViewModel(
        AssistantOrchestrator orchestrator,
        OllamaHealthService health,
        OllamaHostRecovery ollamaHostRecovery,
        SecurityPreferences security,
        IOptions<OllamaOptions> ollama,
        IOptions<AppOptions> app,
        IServiceProvider services)
    {
        _orchestrator = orchestrator;
        _health = health;
        _ollamaHostRecovery = ollamaHostRecovery;
        _security = security;
        _ollama = ollama;
        _app = app;
        _services = services;
        _conversation.Add(new ChatMessage("system", SystemPrompts.Build(security)));
        Messages = new ObservableCollection<ChatLine>();
    }

    public ObservableCollection<ChatLine> Messages { get; }

    [ObservableProperty] private string _inputText = "";

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _statusBanner = "";

    [ObservableProperty] private bool _statusBannerIsError;

    partial void OnIsBusyChanged(bool value) => CancelSendCommand.NotifyCanExecuteChanged();

    public async Task InitializeAsync() => await RunHealthCheckAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task CheckHealthAsync() => await RunHealthCheckAsync().ConfigureAwait(true);

    private async Task RunHealthCheckAsync()
    {
        if (_app.Value.Provider != AssistantProviderKind.Ollama)
        {
            StatusBanner = "Using LlamaSharp (local GGUF). Ollama is not required.";
            StatusBannerIsError = false;
            return;
        }

        var result = await _health.CheckAsync().ConfigureAwait(true);
        StatusBannerIsError = !result.IsHealthy;
        StatusBanner = result.IsHealthy
            ? "Connected to Ollama."
            : result.UserMessage ?? "Ollama unavailable.";
    }

    [RelayCommand(CanExecute = nameof(CanCancelSend))]
    private void CancelSend() => _sendCts?.Cancel();

    private bool CanCancelSend() => IsBusy;

    [RelayCommand]
    private void ToggleTheme()
    {
        _security.Theme = _security.Theme switch
        {
            AppTheme.Light => AppTheme.Dark,
            AppTheme.Dark => AppTheme.System,
            AppTheme.System => AppTheme.Light,
            _ => AppTheme.Light
        };
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var w = _services.GetRequiredService<SettingsWindow>();
        w.Owner = Application.Current?.MainWindow;
        if (w.ShowDialog() == true)
            RefreshSystemPrompt();
    }

    private void RefreshSystemPrompt()
    {
        if (_conversation.Count == 0)
            return;
        if (_conversation[0].Role == "system")
            _conversation[0] = new ChatMessage("system", SystemPrompts.Build(_security));
    }

    [RelayCommand]
    private void NewChat()
    {
        _conversation.Clear();
        _conversation.Add(new ChatMessage("system", SystemPrompts.Build(_security)));
        Messages.Clear();
        StatusBanner = string.Empty;
        StatusBannerIsError = false;
    }

    [RelayCommand]
    private async Task ExportChatAsync()
    {
        if (Messages.Count == 0)
            return;

        var dlg = new SaveFileDialog
        {
            Filter = "Markdown|*.md|All files|*.*",
            DefaultExt = ".md"
        };

        if (dlg.ShowDialog() != true)
            return;

        var sb = new StringBuilder();
        foreach (var m in Messages)
        {
            var title = m.Role == ChatRole.User ? "User" : "Assistant";
            sb.AppendLine("## " + title);
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(m.ThinkingText))
            {
                sb.AppendLine("### Reasoning");
                sb.AppendLine();
                sb.AppendLine(m.ThinkingText);
                sb.AppendLine();
            }

            sb.AppendLine(m.Text);
            sb.AppendLine();
        }

        await System.IO.File.WriteAllTextAsync(dlg.FileName, sb.ToString()).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsBusy)
            return;

        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        if (_app.Value.Provider == AssistantProviderKind.Ollama)
        {
            var health = await _health.CheckAsync().ConfigureAwait(true);
            if (!health.IsHealthy)
            {
                StatusBanner = health.UserMessage ?? "Ollama unavailable.";
                StatusBannerIsError = true;
                return;
            }
        }

        IsBusy = true;
        StatusBanner = string.Empty;
        StatusBannerIsError = false;
        _sendCts = new CancellationTokenSource();

        var userLine = new ChatLine(ChatRole.User, text);
        Messages.Add(userLine);
        InputText = "";

        var assistantLine = new ChatLine(ChatRole.Assistant, string.Empty);
        Messages.Add(assistantLine);

        var timeoutMs = _security.OllamaRequestTimeoutSeconds > 0
            ? _security.OllamaRequestTimeoutSeconds * 1000
            : (int)Math.Clamp(_ollama.Value.RequestTimeout.TotalMilliseconds, 1000, int.MaxValue);
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_sendCts.Token, timeoutCts.Token);

        var sync = SynchronizationContext.Current;
        var progress = new Progress<string>(delta =>
        {
            void Apply() => assistantLine.Text += delta;
            if (sync is not null)
                sync.Post(_ => Apply(), null);
            else
                Application.Current?.Dispatcher.Invoke(Apply);
        });

        var thinkingProgress = new Progress<string>(delta =>
        {
            void Apply() => assistantLine.ThinkingText = (assistantLine.ThinkingText ?? "") + delta;
            if (sync is not null)
                sync.Post(_ => Apply(), null);
            else
                Application.Current?.Dispatcher.Invoke(Apply);
        });

        try
        {
            var history = _conversation.ToList();
            var model = string.IsNullOrWhiteSpace(_security.OllamaModelOverride)
                ? _ollama.Value.Model
                : _security.OllamaModelOverride!;
            var options = new ChatCompletionOptions
            {
                Model = model,
                Temperature = 0.2f,
                StreamingContent = progress,
                StreamingThinking = thinkingProgress
            };

            var recoveryAttempted = false;
            while (true)
            {
                try
                {
                    var reply = await _orchestrator
                        .RunUserTurnAsync(history, text, options, linked.Token)
                        .ConfigureAwait(true);

                    assistantLine.Text = reply;
                    _conversation.Add(new ChatMessage("user", text));
                    _conversation.Add(new ChatMessage(
                        "assistant",
                        reply,
                        AssistantThinking: string.IsNullOrWhiteSpace(assistantLine.ThinkingText)
                            ? null
                            : assistantLine.ThinkingText));
                    break;
                }
                catch (Exception ex) when (!recoveryAttempted
                                           && _app.Value.Provider == AssistantProviderKind.Ollama
                                           && ex is not OperationCanceledException)
                {
                    recoveryAttempted = true;
                    StatusBanner = "Request failed. Checking Ollama…";
                    StatusBannerIsError = false;
                    var recovered = await _ollamaHostRecovery
                        .TryKillRunningHostAndRestartServeAsync(linked.Token)
                        .ConfigureAwait(true);
                    if (!recovered)
                        throw;

                    void ClearAssistantSurface()
                    {
                        assistantLine.Text = string.Empty;
                        assistantLine.ThinkingText = null;
                    }

                    if (sync is not null)
                        sync.Post(_ => ClearAssistantSurface(), null);
                    else
                        Application.Current?.Dispatcher.Invoke(ClearAssistantSurface);

                    StatusBanner = "Restarted Ollama. Retrying once…";
                    StatusBannerIsError = false;
                    await Task.Delay(2000, linked.Token).ConfigureAwait(true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusBanner = _sendCts.IsCancellationRequested ? "Stopped." : "Request timed out.";
            StatusBannerIsError = false;
            if (string.IsNullOrEmpty(assistantLine.Text) && string.IsNullOrWhiteSpace(assistantLine.ThinkingText))
            {
                Messages.Remove(assistantLine);
                Messages.Remove(userLine);
            }
        }
        catch (Exception ex)
        {
            StatusBanner = ex.Message;
            StatusBannerIsError = true;
            if (string.IsNullOrEmpty(assistantLine.Text) && string.IsNullOrWhiteSpace(assistantLine.ThinkingText))
            {
                Messages.Remove(assistantLine);
                Messages.Remove(userLine);
            }
        }
        finally
        {
            assistantLine.IsAwaitingResponse = false;
            IsBusy = false;
            _sendCts = null;
        }
    }
}
