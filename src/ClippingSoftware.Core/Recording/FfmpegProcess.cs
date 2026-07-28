using System.Diagnostics;

namespace ClippingSoftware.Core.Recording;

/// <summary>
/// Runs one bundled ffmpeg/ffprobe invocation to completion and hands back its exit code and captured
/// stderr. Every ffmpeg caller in this namespace goes through here rather than driving Process directly.
///
/// The reason this exists as a shared helper rather than being inlined per caller: both pipes have to be
/// drained *concurrently* with waiting for exit. A redirected pipe that nobody reads fills its OS buffer
/// (~4KB) and then blocks the child's next write forever, which deadlocks the WaitForExit alongside it -
/// and since these run on the ingest/export path, that deadlock would hang a clip save with no error and
/// no timeout. The original per-caller copies redirected stdout, then read only stderr, leaving exactly
/// that trap armed on four separate code paths.
/// </summary>
internal static class FfmpegProcess
{
    /// <param name="configureArguments">Populates ProcessStartInfo.ArgumentList - the list form, not a
    /// single command-line string, so paths containing spaces/quotes never need manual escaping.</param>
    /// <returns>The process exit code and everything it wrote to stderr (where ffmpeg reports errors;
    /// stdout is drained and discarded, since every caller writes its real output to a file).</returns>
    public static async Task<(int ExitCode, string StandardError)> RunAsync(
        string executablePath,
        Action<System.Collections.ObjectModel.Collection<string>> configureArguments,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        configureArguments(startInfo.ArgumentList);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {Path.GetFileName(executablePath)}.");

        // Both reads started before the wait, so neither pipe can back up while the other is being read.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        await stdoutTask;
        var stderr = await stderrTask;

        return (process.ExitCode, stderr);
    }

    /// <summary>
    /// <see cref="RunAsync"/> plus the stdout capture that ffprobe (unlike every ffmpeg caller) actually
    /// needs, since its JSON report *is* its output.
    /// </summary>
    public static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCapturingStdoutAsync(
        string executablePath,
        Action<System.Collections.ObjectModel.Collection<string>> configureArguments,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        configureArguments(startInfo.ArgumentList);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {Path.GetFileName(executablePath)}.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
