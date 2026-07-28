using System.Diagnostics;
using System.Globalization;

namespace ClippingSoftware.Core.Recording;

/// <summary>
/// One audio track to include in a trim export (M9/M11). <paramref name="FilePath"/> null means "map
/// straight from the source clip's own container" (<paramref name="StreamIndex"/> selects which audio
/// stream); non-null means "use this separately extracted per-track file instead" (AudioTrackExtractor's
/// output - StreamIndex is then just carried along for identification, not used to map). VolumeMul scales
/// that track's level in the mix (1.0 = unchanged); 0.0 isn't rejected but excluding the track from the
/// list entirely is cheaper than mixing in silence.
/// </summary>
public record AudioTrackExportSelection(int StreamIndex, string? FilePath, double VolumeMul = 1.0);

/// <summary>
/// Shells out to the bundled ffmpeg.exe to produce a trimmed copy of a source clip between two
/// timestamps.
///
/// Default mode is stream copy (`-c copy`): it runs in roughly the time it takes to copy the
/// bytes (no decode/encode pass) and loses no quality, which matters here since exporting a
/// trimmed clip is meant to be a quick "grab the highlight" action, not a render job. The
/// tradeoff is that placing `-ss` before `-i` seeks to the nearest keyframe at or before the
/// requested start - stream copy can only cut on keyframe boundaries, it never touches frame data
/// - so the actual trim start can land up to one GOP (a couple of seconds, depending on the
/// recording encoder's keyframe interval) earlier than the requested in-point.
///
/// <paramref name="frameAccurate"/> (on TrimAsync) opts into the alternative: a re-encode
/// (`-c:v libx264` / AAC audio). `-ss` still goes before `-i` (input-side) in both modes - ffmpeg's
/// default `-accurate_seek` behavior means an input-side seek into a decode/re-encode pipeline
/// still lands on the exact requested frame (it seeks to the nearest keyframe, then decodes and
/// discards forward to the exact position), it's only stream copy that has no decode pass to
/// correct the keyframe snap with. That decode-forward segment is why frame-accurate mode costs
/// export time and a small quality/bitrate hit versus the copied original - an explicit opt-in for
/// when the keyframe-snapped default isn't good enough, not the new default. (Putting `-ss` after
/// `-i` instead would also be frame-accurate, but slower - it decodes from the start of the file
/// rather than the nearest keyframe - and, when extra audio-track inputs follow the main one on the
/// command line, silently binds to the *next* `-i` instead of this one, which is why this class
/// always keeps `-ss` on the input side.)
///
/// <paramref name="audioTracks"/> (on TrimAsync) selects and levels which audio makes it into the
/// export - the Trim Editor's per-track mute/volume controls. Each selected track becomes its own
/// ffmpeg input when it has a separately extracted file (AudioTrackExtractor's output, one file per
/// source at record time - see RecordingIngestCoordinator), or maps straight from the source
/// container's own stream otherwise (older clips ingested before per-track extraction existed).
/// Every extra input gets its own input-side `-ss` too, same reasoning as the main source above.
/// Zero tracks means a silent export; exactly one at full volume maps straight through (still
/// stream-copyable, unless frameAccurate is also on); anything else - a volume adjustment or more
/// than one track - needs a decode pass and so forces audio to re-encode to AAC, mixed down with
/// `amix` when there's more than one (sharing platforms only play a file's first audio track
/// anyway, so the export should never carry more than one).
/// </summary>
public class ClipTrimmer(string? ffmpegPath = null)
{
    private readonly string _ffmpegPath = ffmpegPath ?? FfmpegTools.FfmpegPath;

    /// <summary>
    /// Trims [start, end) out of <paramref name="sourceFilePath"/> into a new file under
    /// <paramref name="outputDirectory"/> (created if missing, matching the source's extension)
    /// and returns the output path. <paramref name="frameAccurate"/> selects re-encode mode (see
    /// class doc comment) instead of the default stream-copy. <paramref name="audioTracks"/> null
    /// keeps every audio stream from the source untouched (legacy behavior); an explicit list
    /// (even empty) selects/levels/mixes down exactly those tracks - see class doc comment. Throws
    /// <see cref="ArgumentException"/> if end &lt;= start, or <see cref="InvalidOperationException"/>
    /// if ffmpeg fails or doesn't produce an output file.
    /// </summary>
    public async Task<string> TrimAsync(
        string sourceFilePath,
        TimeSpan start,
        TimeSpan end,
        string outputDirectory,
        bool frameAccurate = false,
        IReadOnlyList<AudioTrackExportSelection>? audioTracks = null,
        CancellationToken cancellationToken = default)
    {
        if (end <= start)
        {
            throw new ArgumentException($"End ({end}) must be after start ({start}).", nameof(end));
        }

        Directory.CreateDirectory(outputDirectory);

        var outputFileName =
            $"{Path.GetFileNameWithoutExtension(sourceFilePath)}_trim_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(sourceFilePath)}";
        var outputPath = Path.Combine(outputDirectory, outputFileName);

        var duration = end - start;
        var startSeconds = start.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        var durationSeconds = duration.TotalSeconds.ToString(CultureInfo.InvariantCulture);

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

        // Input-side -ss: always placed before this input's own -i, regardless of frameAccurate.
        // For stream copy this only seeks to the nearest preceding keyframe (no decode pass exists
        // to correct it); for a re-encode, ffmpeg's default accurate-seek behavior decodes-and-
        // discards forward from that keyframe to the exact requested frame, so this is frame-
        // accurate too - and cheaper than decoding from the start of the file. Keeping it on the
        // input side (vs. after all -i's as an output option) also avoids it silently binding to
        // whichever -i comes next once the extra audio-track inputs below are added - see class
        // doc comment.
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(startSeconds);

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourceFilePath);

        // Extra inputs for tracks that were extracted to their own file - same input-side -ss
        // reasoning as the main source above.
        var extraInputPaths = audioTracks?
            .Where(t => t.FilePath is not null)
            .Select(t => t.FilePath!)
            .Distinct()
            .ToList() ?? [];

        var inputIndexByFilePath = new Dictionary<string, int>();
        foreach (var path in extraInputPaths)
        {
            inputIndexByFilePath[path] = 1 + inputIndexByFilePath.Count;
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(startSeconds);
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(path);
        }

        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(durationSeconds);

        if (audioTracks is null)
        {
            // Legacy path: no explicit track selection, ffmpeg keeps every stream from the source as-is.
            if (frameAccurate)
            {
                startInfo.ArgumentList.Add("-c:v");
                startInfo.ArgumentList.Add("libx264");
                startInfo.ArgumentList.Add("-preset");
                startInfo.ArgumentList.Add("veryfast");
                startInfo.ArgumentList.Add("-crf");
                startInfo.ArgumentList.Add("18");
                startInfo.ArgumentList.Add("-c:a");
                startInfo.ArgumentList.Add("aac");
            }
            else
            {
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("copy");
            }
        }
        else
        {
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0:v:0");

            string Specifier(AudioTrackExportSelection track) => track.FilePath is null
                ? $"0:a:{track.StreamIndex}"
                : $"{inputIndexByFilePath[track.FilePath]}:a:0";

            var needsFilter = audioTracks.Count > 1 || (audioTracks.Count == 1 && audioTracks[0].VolumeMul != 1.0);

            if (needsFilter)
            {
                string filterComplex;
                if (audioTracks.Count == 1)
                {
                    var vol = audioTracks[0].VolumeMul.ToString(CultureInfo.InvariantCulture);
                    filterComplex = $"[{Specifier(audioTracks[0])}]volume={vol}[aout]";
                }
                else
                {
                    var legs = string.Concat(audioTracks.Select((t, i) =>
                        $"[{Specifier(t)}]volume={t.VolumeMul.ToString(CultureInfo.InvariantCulture)}[a{i}]"));
                    var mixInputs = string.Concat(Enumerable.Range(0, audioTracks.Count).Select(i => $"[a{i}]"));
                    filterComplex = $"{legs}{mixInputs}amix=inputs={audioTracks.Count}:duration=longest[aout]";
                }

                startInfo.ArgumentList.Add("-filter_complex");
                startInfo.ArgumentList.Add(filterComplex);
                startInfo.ArgumentList.Add("-map");
                startInfo.ArgumentList.Add("[aout]");
            }
            else if (audioTracks.Count == 1)
            {
                startInfo.ArgumentList.Add("-map");
                startInfo.ArgumentList.Add(Specifier(audioTracks[0]));
            }
            // Count == 0: no audio map at all - silent export, which is a valid ffmpeg output.

            startInfo.ArgumentList.Add("-c:v");
            startInfo.ArgumentList.Add(frameAccurate ? "libx264" : "copy");
            if (frameAccurate)
            {
                startInfo.ArgumentList.Add("-preset");
                startInfo.ArgumentList.Add("veryfast");
                startInfo.ArgumentList.Add("-crf");
                startInfo.ArgumentList.Add("18");
            }

            if (audioTracks.Count > 0)
            {
                startInfo.ArgumentList.Add("-c:a");
                // A single mapped stream at full volume can still stream-copy when the fast path is
                // selected; a filter (volume adjust or mix) always needs the decode pass it already
                // requires, and frame-accurate mode re-encodes audio too for output consistency with
                // the re-encoded video, so neither is ever copy-able.
                startInfo.ArgumentList.Add(needsFilter || frameAccurate ? "aac" : "copy");
            }
        }

        // Without this, seeking from a non-zero start can leave small negative timestamps around
        // the cut point on some containers, which confuses players' seek bars and duration
        // readouts even though the frames themselves are intact.
        startInfo.ArgumentList.Add("-avoid_negative_ts");
        startInfo.ArgumentList.Add("make_zero");
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException($"ffmpeg trim failed (exit {process.ExitCode}): {stderr}");
        }

        return outputPath;
    }
}
