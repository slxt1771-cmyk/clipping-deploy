using System.Globalization;
using System.Text.Json;

namespace ClippingSoftware.Core.Recording;

public record ClipProbeResult(
    TimeSpan Duration,
    int VideoWidth,
    int VideoHeight,
    int AudioStreamCount);

/// <summary>
/// Shells out to the bundled ffprobe.exe to read duration, video resolution, and audio-stream count from a
/// finished recording. Doesn't know or care which source fed which stream - RecordingIngestCoordinator
/// matches AudioStreamCount against AudioSourceManager's current track layout (by position: OBS's
/// AdvOut/RecTracks bitmask order is stable, so stream 0 is always the lowest enabled track number, etc.)
/// to attach real labels.
/// </summary>
public class ClipMetadataExtractor(string? ffprobePath = null)
{
    private readonly string _ffprobePath = ffprobePath ?? FfmpegTools.FfprobePath;

    public async Task<ClipProbeResult> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var json = await RunFfprobeAsync(filePath, cancellationToken);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var durationSeconds = double.Parse(
            root.GetProperty("format").GetProperty("duration").GetString()!,
            CultureInfo.InvariantCulture);

        var width = 0;
        var height = 0;
        var audioStreamCount = 0;

        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            var codecType = stream.GetProperty("codec_type").GetString();
            if (codecType == "video" && width == 0)
            {
                width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
            }
            else if (codecType == "audio")
            {
                audioStreamCount++;
            }
        }

        return new ClipProbeResult(
            Duration: TimeSpan.FromSeconds(durationSeconds),
            VideoWidth: width,
            VideoHeight: height,
            AudioStreamCount: audioStreamCount);
    }

    private async Task<string> RunFfprobeAsync(string filePath, CancellationToken cancellationToken)
    {
        var (exitCode, stdout, stderr) = await FfmpegProcess.RunCapturingStdoutAsync(_ffprobePath, args =>
        {
            args.Add("-v");
            args.Add("error");
            args.Add("-print_format");
            args.Add("json");
            args.Add("-show_format");
            args.Add("-show_streams");
            args.Add(filePath);
        }, cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"ffprobe exited with code {exitCode}: {stderr}");
        }

        return stdout;
    }
}
