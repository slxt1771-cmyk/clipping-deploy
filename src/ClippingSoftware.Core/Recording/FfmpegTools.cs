namespace ClippingSoftware.Core.Recording;

/// <summary>
/// Locates the bundled static ffmpeg/ffprobe binaries. Walks up from the running app's base
/// directory looking for a "tools\ffmpeg" folder (works no matter how deep the app's bin output
/// is nested under the repo root), falling back to the known absolute path for this single-machine,
/// personal-use install if the walk-up fails (e.g. when run from a scratch/test harness located
/// entirely outside the repo tree).
/// </summary>
public static class FfmpegTools
{
    private const string FallbackToolsDirectory = @"D:\claude stuff\clipping software\tools\ffmpeg";

    private static readonly Lazy<string> ToolsDirectoryLazy = new(LocateToolsDirectory);

    public static string ToolsDirectory => ToolsDirectoryLazy.Value;

    public static string FfmpegPath => Path.Combine(ToolsDirectory, "ffmpeg.exe");

    public static string FfprobePath => Path.Combine(ToolsDirectory, "ffprobe.exe");

    private static string LocateToolsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "ffmpeg");
            if (File.Exists(Path.Combine(candidate, "ffmpeg.exe")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        if (File.Exists(Path.Combine(FallbackToolsDirectory, "ffmpeg.exe")))
        {
            return FallbackToolsDirectory;
        }

        throw new FileNotFoundException(
            "Could not locate bundled ffmpeg/ffprobe tools directory (searched up from " +
            $"{AppContext.BaseDirectory} and fell back to {FallbackToolsDirectory}).");
    }
}
