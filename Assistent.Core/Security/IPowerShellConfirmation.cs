namespace Assistent.Core.Security;

/// <summary>Optional UI gate before running PowerShell from the model.</summary>
public interface IPowerShellConfirmation
{
    ValueTask<bool> ConfirmAsync(string commandPreview, CancellationToken cancellationToken);
}
