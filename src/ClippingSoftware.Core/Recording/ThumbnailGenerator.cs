using System.Globalization;

namespace ClippingSoftware.Core.Recording;

/// <summary>
/// Shells out to the bundled ffmpeg.exe to grab a single mid-clip frame as a JPEG thumbnail.
/// </summary>
public class ThumbnailGenerator(string? ffmpegPath = null)
{
    private readonly string _ffmpegPath = ffmpegPath ?? FfmpegTools.FfmpegPath;

    public async Task GenerateAsync(
        string inputFilePath,
        string outputJpgPath,
        TimeSpan clipDuration,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputJpgPath)!);

        var midpointSeconds = Math.Max(0, clipDuration.TotalSeconds / 2);

        var (exitCode, stderr) = await FfmpegProcess.RunAsync(_ffmpegPath, args =>
        {
            args.Add("-y");
            args.Add("-v");
            args.Add("error");
            args.Add("-ss");
            args.Add(midpointSeconds.ToString(CultureInfo.InvariantCulture));
            args.Add("-i");
            args.Add(inputFilePath);
            args.Add("-frames:v");
            args.Add("1");
            args.Add("-vf");
            args.Add("scale=320:-1");
            args.Add(outputJpgPath);
        }, cancellationToken);

        if (exitCode != 0 || !File.Exists(outputJpgPath))
        {
            throw new InvalidOperationException($"ffmpeg thumbnail generation failed (exit {exitCode}): {stderr}");
        }
    }
}
