namespace ClippingSoftware.Core;

/// <summary>
/// Resolves files that ship *with* the app (the curated game database, the sound cues, the static
/// ffmpeg/ffprobe binaries) rather than files the user creates.
///
/// Two layouts have to work, and they differ only in how deep the exe sits relative to the content:
///   Installed  - the installer lays "assets\" and "tools\" down beside ClippingSoftware.App.exe, so
///                AppContext.BaseDirectory is already the content root. This is the packaged layout
///                the csproj reproduces by copying both trees into the build output.
///   Repo/dev   - running from bin\Debug\net8.0-windows\ puts the exe several levels below the repo
///                root that actually holds "assets\"/"tools\", so the walk-up finds them.
///
/// Checking BaseDirectory first matters beyond just being the common case: an installed copy sitting
/// anywhere under a folder that happens to contain an "assets" directory would otherwise walk past its
/// own bundled content and bind to the unrelated one.
///
/// A missing file is reported as a plain not-found rather than silently substituting some other
/// machine's path - see <see cref="TryResolve"/> for the callers that treat absence as recoverable.
/// </summary>
public static class BundledResources
{
    /// <summary>
    /// Locates a bundled file/directory given its path relative to the content root (e.g.
    /// "assets", "GameDatabase", "known-games.json"). Returns false, with <paramref name="fullPath"/>
    /// set to the preferred (BaseDirectory-relative) candidate, when nothing exists at either location -
    /// callers that can degrade gracefully use that path for the error message.
    /// </summary>
    public static bool TryResolve(out string fullPath, params string[] relativeSegments)
    {
        var relative = Path.Combine(relativeSegments);
        fullPath = Path.Combine(AppContext.BaseDirectory, relative);

        if (Exists(fullPath))
        {
            return true;
        }

        // Skip BaseDirectory itself - already checked above.
        var dir = new DirectoryInfo(AppContext.BaseDirectory).Parent;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Exists(candidate))
            {
                fullPath = candidate;
                return true;
            }

            dir = dir.Parent;
        }

        return false;
    }

    /// <summary>
    /// <see cref="TryResolve"/> for callers that can't proceed without the file. Throws
    /// <see cref="FileNotFoundException"/> naming both the expected install location and the fact that
    /// the repo walk-up also came up empty, since "which of the two layouts did you expect" is the
    /// first thing worth knowing when this fires.
    /// </summary>
    public static string Resolve(params string[] relativeSegments)
    {
        if (TryResolve(out var fullPath, relativeSegments))
        {
            return fullPath;
        }

        var relative = Path.Combine(relativeSegments);
        throw new FileNotFoundException(
            $"Bundled resource '{relative}' was not found. Looked beside the application at " +
            $"'{Path.Combine(AppContext.BaseDirectory, relative)}' and in every parent directory above it. " +
            "A packaged install should carry this file; if you are running from source, make sure the " +
            "repository's assets/ and tools/ folders are present.",
            fullPath);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
