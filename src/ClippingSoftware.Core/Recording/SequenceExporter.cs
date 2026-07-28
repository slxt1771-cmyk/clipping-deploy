using System.Diagnostics;

namespace ClippingSoftware.Core.Recording;

/// <summary>
/// Combines several trimmed clips into one output file (M16's simple multi-clip "sequence" editor - pick
/// clips, trim each, export as one). Each segment is trimmed with <see cref="ClipTrimmer"/> in
/// frame-accurate mode first (concatenation needs every segment to share identical codec/params, which
/// only the re-encode path guarantees - the fast stream-copy path can't promise that across clips
/// recorded with different settings), then ffmpeg's concat demuxer stitches the now-uniform segments
/// together with a final `-c copy` pass (cheap, since nothing needs re-encoding a second time).
/// </summary>
public class SequenceExporter(ClipTrimmer? clipTrimmer = null, string? ffmpegPath = null)
{
    private readonly ClipTrimmer _clipTrimmer = clipTrimmer ?? new ClipTrimmer();
    private readonly string _ffmpegPath = ffmpegPath ?? FfmpegTools.FfmpegPath;

    /// <summary>Trims each segment in order, concatenates them into one file under
    /// <paramref name="outputDirectory"/>, and returns its path. Intermediate per-segment files are
    /// cleaned up afterward regardless of success or failure.</summary>
    public async Task<string> ExportAsync(
        IReadOnlyList<(string SourceFilePath, TimeSpan Start, TimeSpan End)> segments,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        if (segments.Count == 0)
        {
            throw new ArgumentException("A sequence needs at least one clip to export.", nameof(segments));
        }

        Directory.CreateDirectory(outputDirectory);
        var segmentPaths = new List<string>();
        try
        {
            foreach (var segment in segments)
            {
                var segmentPath = await _clipTrimmer.TrimAsync(
                    segment.SourceFilePath, segment.Start, segment.End, outputDirectory,
                    frameAccurate: true, cancellationToken: cancellationToken);
                segmentPaths.Add(segmentPath);
            }

            var outputPath = Path.Combine(outputDirectory,
                $"sequence_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(segmentPaths[0])}");
            await ConcatAsync(segmentPaths, outputPath, cancellationToken);
            return outputPath;
        }
        finally
        {
            foreach (var path in segmentPaths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                    // Best-effort cleanup - a leftover temp segment file isn't worth failing the export over.
                }
            }
        }
    }

    private async Task ConcatAsync(IReadOnlyList<string> segmentPaths, string outputPath, CancellationToken cancellationToken)
    {
        var listFilePath = Path.Combine(Path.GetDirectoryName(outputPath)!, $"concat_{Guid.NewGuid():N}.txt");
        // These are our own just-created temp segment paths, never user-controlled content, so naive
        // single-quote wrapping (the concat demuxer's documented format) is safe here.
        await File.WriteAllLinesAsync(listFilePath, segmentPaths.Select(p => $"file '{p}'"), cancellationToken);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("concat");
            startInfo.ArgumentList.Add("-safe");
            startInfo.ArgumentList.Add("0");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(listFilePath);
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("copy");
            startInfo.ArgumentList.Add(outputPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start ffmpeg.");

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stderr = await stderrTask;

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new InvalidOperationException($"ffmpeg concat failed (exit {process.ExitCode}): {stderr}");
            }
        }
        finally
        {
            try
            {
                File.Delete(listFilePath);
            }
            catch
            {
                // Best-effort cleanup, same reasoning as the segment files above.
            }
        }
    }
}
