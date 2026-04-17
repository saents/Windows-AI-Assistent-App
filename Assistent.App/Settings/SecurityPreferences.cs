using System.IO;
using System.Text.Json;
using Assistent.Core.Security;
using Microsoft.Extensions.Configuration;

namespace Assistent.App.Settings;

/// <summary>
/// User settings in AppData + appsettings defaults (theme, Ollama overrides, security).
/// </summary>
public sealed class SecurityPreferences : ISecurityPreferences
{
    private readonly IConfiguration _configuration;
    private readonly string _userSettingsPath;
    private bool _allowPowerShell;
    private bool _confirmBeforePowerShell = true;
    private string? _powerShellAllowlistPrefix;
    private AppTheme _theme = AppTheme.Light;
    private string? _ollamaBaseUrlOverride;
    private string? _ollamaModelOverride;
    private int _maxConversationMessages = 80;
    /// <summary>0 = use appsettings Ollama:RequestTimeout.</summary>
    private int _ollamaRequestTimeoutSeconds;

    /// <summary>Ollama <c>/api/chat</c> <c>think</c> flag (separate reasoning trace for supported models).</summary>
    private bool _ollamaThink;

    public SecurityPreferences(IConfiguration configuration)
    {
        _configuration = configuration;
        _userSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Assistent",
            "user-settings.json");
        Load();
    }

    public event EventHandler? ThemeChanged;

    public bool AllowPowerShellExecution
    {
        get => _allowPowerShell;
        set
        {
            if (_allowPowerShell == value)
                return;
            _allowPowerShell = value;
            Save();
        }
    }

    public bool ConfirmBeforePowerShell
    {
        get => _confirmBeforePowerShell;
        set
        {
            if (_confirmBeforePowerShell == value)
                return;
            _confirmBeforePowerShell = value;
            Save();
        }
    }

    public string? PowerShellAllowlistPrefix
    {
        get => _powerShellAllowlistPrefix;
        set
        {
            if (_powerShellAllowlistPrefix == value)
                return;
            _powerShellAllowlistPrefix = value;
            Save();
        }
    }

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            if (_theme == value)
                return;
            _theme = value;
            Save();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? OllamaBaseUrlOverride
    {
        get => _ollamaBaseUrlOverride;
        set
        {
            if (_ollamaBaseUrlOverride == value)
                return;
            _ollamaBaseUrlOverride = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            Save();
        }
    }

    public string? OllamaModelOverride
    {
        get => _ollamaModelOverride;
        set
        {
            if (_ollamaModelOverride == value)
                return;
            _ollamaModelOverride = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            Save();
        }
    }

    public int MaxConversationMessages
    {
        get => _maxConversationMessages;
        set
        {
            var v = Math.Clamp(value, 10, 500);
            if (_maxConversationMessages == v)
                return;
            _maxConversationMessages = v;
            Save();
        }
    }

    /// <summary>Per-request cap for the assistant round (0 = use appsettings).</summary>
    public int OllamaRequestTimeoutSeconds
    {
        get => _ollamaRequestTimeoutSeconds;
        set
        {
            var v = Math.Clamp(value, 0, 3600);
            if (_ollamaRequestTimeoutSeconds == v)
                return;
            _ollamaRequestTimeoutSeconds = v;
            Save();
        }
    }

    /// <summary>
    /// When true, Ollama sends <c>think: true</c> (reasoning trace). When false, <c>think: false</c> for models that support it.
    /// Default comes from <c>Ollama:Think</c> in appsettings, then user-settings.json if present.
    /// </summary>
    public bool OllamaThink
    {
        get => _ollamaThink;
        set
        {
            if (_ollamaThink == value)
                return;
            _ollamaThink = value;
            Save();
        }
    }

    private void Load()
    {
        _allowPowerShell = _configuration.GetValue("Security:AllowPowerShellExecution", false);
        _confirmBeforePowerShell = _configuration.GetValue("Security:ConfirmBeforePowerShell", true);
        _maxConversationMessages = _configuration.GetValue("Assistant:MaxConversationMessages", 80);
        _ollamaThink = _configuration.GetValue("Ollama:Think", false);

        try
        {
            if (!File.Exists(_userSettingsPath))
                return;
            var json = File.ReadAllText(_userSettingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("allowPowerShellExecution", out var ps) &&
                ps.ValueKind is JsonValueKind.True or JsonValueKind.False)
                _allowPowerShell = ps.GetBoolean();
            if (root.TryGetProperty("confirmBeforePowerShell", out var cf) &&
                cf.ValueKind is JsonValueKind.True or JsonValueKind.False)
                _confirmBeforePowerShell = cf.GetBoolean();
            if (root.TryGetProperty("powerShellAllowlistPrefix", out var al) && al.ValueKind == JsonValueKind.String)
                _powerShellAllowlistPrefix = al.GetString();
            if (root.TryGetProperty("theme", out var th) && th.ValueKind == JsonValueKind.String &&
                Enum.TryParse<AppTheme>(th.GetString(), true, out var theme))
                _theme = theme;
            if (root.TryGetProperty("ollamaBaseUrl", out var bu) && bu.ValueKind == JsonValueKind.String)
                _ollamaBaseUrlOverride = string.IsNullOrWhiteSpace(bu.GetString()) ? null : bu.GetString();
            if (root.TryGetProperty("ollamaModel", out var mo) && mo.ValueKind == JsonValueKind.String)
                _ollamaModelOverride = string.IsNullOrWhiteSpace(mo.GetString()) ? null : mo.GetString();
            if (root.TryGetProperty("maxConversationMessages", out var mx) && mx.TryGetInt32(out var m))
                _maxConversationMessages = Math.Clamp(m, 10, 500);
            if (root.TryGetProperty("ollamaRequestTimeoutSeconds", out var to) && to.TryGetInt32(out var tsec))
                _ollamaRequestTimeoutSeconds = Math.Clamp(tsec, 0, 3600);
            if (root.TryGetProperty("ollamaThink", out var think) &&
                think.ValueKind is JsonValueKind.True or JsonValueKind.False)
                _ollamaThink = think.GetBoolean();
        }
        catch
        {
            /* keep defaults */
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_userSettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var payload = new
            {
                allowPowerShellExecution = _allowPowerShell,
                confirmBeforePowerShell = _confirmBeforePowerShell,
                powerShellAllowlistPrefix = _powerShellAllowlistPrefix,
                theme = _theme.ToString(),
                ollamaBaseUrl = _ollamaBaseUrlOverride,
                ollamaModel = _ollamaModelOverride,
                maxConversationMessages = _maxConversationMessages,
                ollamaRequestTimeoutSeconds = _ollamaRequestTimeoutSeconds,
                ollamaThink = _ollamaThink
            };
            File.WriteAllText(_userSettingsPath, JsonSerializer.Serialize(payload));
        }
        catch
        {
            /* ignore */
        }
    }
}
