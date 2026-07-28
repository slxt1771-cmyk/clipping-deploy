namespace ClippingSoftware.Core.Recording;

/// <summary>
/// Locates the bundled static ffmpeg/ffprobe binaries under "tools\ffmpeg" - beside the exe in a packaged
/// install, or at the repo root when running from a bin output folder (see <see cref="BundledResources"/>
/// for how both layouts are resolved).
///
/// Resolution is deferred and cached via <see cref="Lazy{T}"/> rather than done at type-load: every ffmpeg
/// consumer (<see cref="ThumbnailGenerator"/>, <see cref="ClipTrimmer"/>, ...) defaults its path from these
/// properties in a field initializer, so an eager throw here would take down the whole ingest pipeline's
/// construction instead of just the one operation that actually needed ffmpeg.
/// </summary>
public static class FfmpegTools
{
    private static readonly Lazy<string> ToolsDirectoryLazy = new(() => BundledResources.Resolve("tools", "ffmpeg"));

    public static string ToolsDirectory => ToolsDirectoryLazy.Value;

    public static string FfmpegPath => Path.Combine(ToolsDirectory, "ffmpeg.exe");

    public static string FfprobePath => Path.Combine(ToolsDirectory, "ffprobe.exe");

    /// <summary>
    /// Whether both binaries are actually present, without throwing. The first-run setup wizard uses this
    /// to report a broken/incomplete install up front rather than letting the user discover it later as a
    /// failed clip ingest.
    /// </summary>
    public static bool AreAvailable()
    {
        try
        {
            return File.Exists(FfmpegPath) && File.Exists(FfprobePath);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }
}
