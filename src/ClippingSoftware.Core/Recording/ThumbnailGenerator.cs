using System.Diagnostics;
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
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(midpointSeconds.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputFilePath);
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add("scale=320:-1");
        startInfo.ArgumentList.Add(outputJpgPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;

        if (process.ExitCode != 0 || !File.Exists(outputJpgPath))
        {
            throw new InvalidOperationException($"ffmpeg thumbnail generation failed (exit {process.ExitCode}): {stderr}");
        }
    }
}
