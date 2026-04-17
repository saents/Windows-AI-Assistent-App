namespace Assistent.Core.Security;

/// <summary>User-controlled security and conversation limits.</summary>
public interface ISecurityPreferences
{
    bool AllowPowerShellExecution { get; set; }
    bool ConfirmBeforePowerShell { get; }
    string? PowerShellAllowlistPrefix { get; }
    int MaxConversationMessages { get; }
}
