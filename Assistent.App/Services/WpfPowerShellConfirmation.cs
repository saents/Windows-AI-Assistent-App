using System.Windows;
using Assistent.App.Settings;
using Assistent.Core.Security;

namespace Assistent.App.Services;

public sealed class WpfPowerShellConfirmation : IPowerShellConfirmation
{
    private readonly SecurityPreferences _prefs;

    public WpfPowerShellConfirmation(SecurityPreferences prefs) => _prefs = prefs;

    public ValueTask<bool> ConfirmAsync(string commandPreview, CancellationToken cancellationToken)
    {
        if (!_prefs.ConfirmBeforePowerShell)
            return ValueTask.FromResult(true);

        var preview = commandPreview.Length > 400 ? commandPreview[..400] + "…" : commandPreview;
        var r = MessageBox.Show(
            $"Run this PowerShell command?\n\n{preview}",
            "Confirm PowerShell",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        return ValueTask.FromResult(r == MessageBoxResult.Yes);
    }
}
