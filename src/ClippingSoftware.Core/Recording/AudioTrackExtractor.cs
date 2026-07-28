namespace ClippingSoftware.Core.Recording;

/// <summary>
/// Shells out to the bundled ffmpeg.exe to split every audio stream in a recorded clip's container into
/// its own file - one ffmpeg invocation with multiple -map/output pairs (a single demux pass) rather than
/// one process per stream. RecordingIngestCoordinator calls this at ingest time so the Trim Editor can
/// load and mix each recorded source's audio independently (per-track volume/mute, M11) instead of only
/// being able to include/exclude whole streams from the still-muxed original.
///
/// AAC at 192kbps rather than a lossless format: these are derived files (the original recording's own
/// container keeps every stream untouched, so nothing is lost if extraction is skipped or fails), and
/// this app already accepts an AAC re-encode as the cost of any mixdown (see ClipTrimmer) - extracting to
/// AAC just moves that same cost earlier rather than introducing a new one.
/// </summary>
public class AudioTrackExtractor(string? ffmpegPath = null)
{
    private readonly string _ffmpegPath = ffmpegPath ?? FfmpegTools.FfmpegPath;

    /// <summary>
    /// Extracts audio streams [0, streamCount) from <paramref name="sourceFilePath"/> into
    /// "{outputBasePath}.track{N}.m4a" files (directory created if missing) and returns their paths in
    /// stream order. Returns an empty list without shelling out at all when streamCount is 0.
    /// </summary>
    public async Task<List<string>> ExtractAsync(
        string sourceFilePath, int streamCount, string outputBasePath, CancellationToken cancellationToken = default)
    {
        if (streamCount <= 0)
        {
            return [];
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputBasePath)!);

        var outputPaths = Enumerable.Range(0, streamCount)
            .Select(i => $"{outputBasePath}.track{i}.m4a")
            .ToList();

        var (exitCode, stderr) = await FfmpegProcess.RunAsync(_ffmpegPath, args =>
        {
            args.Add("-y");
            args.Add("-v");
            args.Add("error");
            args.Add("-i");
            args.Add(sourceFilePath);

            for (var i = 0; i < streamCount; i++)
            {
                args.Add("-map");
                args.Add($"0:a:{i}");
                args.Add("-c:a");
                args.Add("aac");
                args.Add("-b:a");
                args.Add("192k");
                args.Add(outputPaths[i]);
            }
        }, cancellationToken);

        if (exitCode != 0 || outputPaths.Any(p => !File.Exists(p)))
        {
            throw new InvalidOperationException($"ffmpeg audio track extraction failed (exit {exitCode}): {stderr}");
        }

        return outputPaths;
    }
}
