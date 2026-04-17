namespace Assistent.Core.Security;

public sealed class AllowAllPowerShellConfirmation : IPowerShellConfirmation
{
    public ValueTask<bool> ConfirmAsync(string commandPreview, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);
}
