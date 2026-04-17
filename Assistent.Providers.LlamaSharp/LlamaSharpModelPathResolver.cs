namespace Assistent.Providers.LlamaSharp;

public enum LlamaSharpModelProbeKind
{
    Resolved,
    MissingModelsFolder,
    NoGgufFiles,
    AmbiguousGguf,
    ModelPathNotFound
}

/// <summary>Result of locating a GGUF for LlamaSharp (settings UI and runtime).</summary>
public readonly record struct LlamaSharpModelProbe(
    LlamaSharpModelProbeKind Kind,
    string? ResolvedPath,
    string ModelsRoot,
    string Detail);

public static class LlamaSharpModelPathResolver
{
    public static string GetModelsRoot(LlamaSharpOptions options, string applicationBaseDirectory)
    {
        var dir = options.ModelsDirectory.Trim();
        if (string.IsNullOrEmpty(dir))
            dir = "Models";
        return Path.IsPathRooted(dir)
            ? Path.GetFullPath(dir)
            : Path.GetFullPath(Path.Combine(applicationBaseDirectory, dir));
    }

    /// <summary>Non-throwing probe for UI; see <see cref="ResolveForLoad"/> for runtime (throws on errors).</summary>
    public static LlamaSharpModelProbe Probe(LlamaSharpOptions options, string applicationBaseDirectory)
    {
        var modelsRoot = GetModelsRoot(options, applicationBaseDirectory);
        var mp = options.ModelPath?.Trim() ?? string.Empty;

        if (!string.IsNullOrEmpty(mp))
        {
            if (Path.IsPathRooted(mp) && File.Exists(mp))
                return new LlamaSharpModelProbe(LlamaSharpModelProbeKind.Resolved, Path.GetFullPath(mp), modelsRoot, string.Empty);

            var relativeToApp = Path.GetFullPath(Path.Combine(applicationBaseDirectory, mp));
            if (File.Exists(relativeToApp))
                return new LlamaSharpModelProbe(LlamaSharpModelProbeKind.Resolved, relativeToApp, modelsRoot, string.Empty);

            var fileNameOnly = Path.GetFileName(mp);
            if (!string.IsNullOrEmpty(fileNameOnly))
            {
                var underModels = Path.GetFullPath(Path.Combine(modelsRoot, fileNameOnly));
                if (File.Exists(underModels))
                    return new LlamaSharpModelProbe(LlamaSharpModelProbeKind.Resolved, underModels, modelsRoot, string.Empty);
            }

            return new LlamaSharpModelProbe(
                LlamaSharpModelProbeKind.ModelPathNotFound,
                null,
                modelsRoot,
                $"LlamaSharp:ModelPath \"{mp}\" was not found (checked absolute path, app folder, and \"{modelsRoot}\").");
        }

        if (!Directory.Exists(modelsRoot))
            return new LlamaSharpModelProbe(
                LlamaSharpModelProbeKind.MissingModelsFolder,
                null,
                modelsRoot,
                $"Folder does not exist yet: \"{modelsRoot}\". Add a .gguf file there or set LlamaSharp:ModelPath.");

        var ggufs = Directory.GetFiles(modelsRoot, "*.gguf", SearchOption.TopDirectoryOnly);
        if (ggufs.Length == 0)
            return new LlamaSharpModelProbe(
                LlamaSharpModelProbeKind.NoGgufFiles,
                null,
                modelsRoot,
                $"No .gguf files in \"{modelsRoot}\".");

        if (ggufs.Length == 1)
            return new LlamaSharpModelProbe(LlamaSharpModelProbeKind.Resolved, ggufs[0], modelsRoot, string.Empty);

        Array.Sort(ggufs, StringComparer.OrdinalIgnoreCase);
        var names = string.Join(", ", ggufs.Select(Path.GetFileName));
        return new LlamaSharpModelProbe(
            LlamaSharpModelProbeKind.AmbiguousGguf,
            null,
            modelsRoot,
            $"Multiple .gguf files: {names}. Set LlamaSharp:ModelPath to one file name.");
    }

    /// <summary>Resolves path for loading; throws with the same messages the chat client used previously.</summary>
    public static string ResolveForLoad(LlamaSharpOptions options, string applicationBaseDirectory)
    {
        var p = Probe(options, applicationBaseDirectory);
        return p.Kind switch
        {
            LlamaSharpModelProbeKind.Resolved => p.ResolvedPath!,
            LlamaSharpModelProbeKind.MissingModelsFolder or LlamaSharpModelProbeKind.NoGgufFiles =>
                throw new InvalidOperationException(
                    $"LlamaSharp: add exactly one .gguf file under \"{p.ModelsRoot}\" (next to the app), or set LlamaSharp:ModelPath in appsettings.json. ({p.Detail})"),
            LlamaSharpModelProbeKind.AmbiguousGguf =>
                throw new InvalidOperationException(p.Detail),
            LlamaSharpModelProbeKind.ModelPathNotFound =>
                throw new FileNotFoundException(p.Detail),
            _ => throw new InvalidOperationException(p.Detail)
        };
    }
}
