using System.Diagnostics;

namespace ClippingSoftware.Core.Recording;

/// <summary>
/// Shared "kill an ffmpeg/ffprobe child process if it's still running" cleanup, used by every class in
/// this namespace that shells out to one via `using var process = Process.Start(...)`. That `using` alone
/// only releases the .NET Process wrapper if an exception (cancellation, a stream-read failure) unwinds
/// past it - it never terminates the still-running OS process, which otherwise keeps running (and keeps
/// the output file open/being written) in the background indefinitely.
/// </summary>
internal static class ProcessCleanup
{
    public static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort: the process may have already exited by the time HasExited was checked (a
            // race, not a bug), or Kill() itself can fail - either way there's nothing more to do here.
        }
    }
}
