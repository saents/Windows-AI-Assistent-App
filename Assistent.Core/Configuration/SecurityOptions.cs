namespace Assistent.Core.Configuration;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public bool AllowPowerShellExecution { get; init; }
}
