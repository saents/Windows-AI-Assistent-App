namespace Assistent.Core.Tools;

/// <summary>Normalizes tool paths to full local paths without restricting to a specific root.</summary>
internal static class ToolFilesystemPaths
{
    internal static bool TryGetFullPath(string? inputPath, out string fullPath, out string? errorMessage)
    {
        fullPath = string.Empty;
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            errorMessage = "Invalid path.";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(inputPath.Trim());
        }
        catch
        {
            errorMessage = "Invalid path.";
            return false;
        }

        return true;
    }
}
