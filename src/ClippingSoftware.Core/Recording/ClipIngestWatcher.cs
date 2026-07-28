namespace ClippingSoftware.Core.Recording;

/// <summary>
/// Confirms that a just-finished OBS recording/replay-buffer file is fully flushed and closed
/// before the rest of the ingest pipeline (ffprobe/ffmpeg) touches it.
///
/// Design note: this is deliberately NOT a raw <see cref="FileSystemWatcher"/> on the clip storage
/// folder. A folder watcher would (a) fire spurious Changed events throughout an in-progress
/// recording as OBS continuously writes the growing .mkv, requiring its own fragile
/// debounce/dedupe logic, and (b) have no reliable way to know whether a given new file is a
/// replay-buffer save or a manual recording stop - that information only exists at the moment
/// ObsController raises RecordingStopped/ReplayBufferSaved. Driving ingestion directly off those
/// two events (see RecordingIngestCoordinator) gives us the exact file path and the correct
/// ClipSourceType with no duplicate-detection race between a watcher and the event. This class's
/// only job is the "is OBS really done with this file yet" readiness check.
/// </summary>
public class ClipIngestWatcher(TimeSpan? pollInterval = null, TimeSpan? timeout = null, int requiredStableReadings = 2)
{
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);
    private readonly int _requiredStableReadings = requiredStableReadings;

    /// <summary>
    /// Waits until <paramref name="filePath"/> exists, its size has stopped changing across
    /// consecutive polls, and it can be opened for shared-read (i.e. OBS no longer holds an
    /// exclusive/write handle on it). Throws <see cref="TimeoutException"/> if the file never
    /// settles within the configured timeout.
    /// </summary>
    public async Task WaitForFileReadyAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + _timeout;
        long lastLength = -1;
        var stableReadings = 0;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
            {
                stableReadings = 0;
                await Task.Delay(_pollInterval, cancellationToken);
                continue;
            }

            try
            {
                var length = new FileInfo(filePath).Length;

                // Try to open with FileShare.Read only (no FileShare.Write): this throws IOException
                // while OBS still holds the file open for writing/finalizing.
                using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // Opened successfully - no other process currently has a write handle on it.
                }

                if (length > 0 && length == lastLength)
                {
                    stableReadings++;
                    if (stableReadings >= _requiredStableReadings)
                    {
                        return;
                    }
                }
                else
                {
                    stableReadings = 0;
                }

                lastLength = length;
            }
            catch (IOException)
            {
                // Still locked by OBS (finalizing/flushing the MKV). Reset and retry after a delay.
                stableReadings = 0;
            }

            await Task.Delay(_pollInterval, cancellationToken);
        }

        throw new TimeoutException($"Timed out waiting for clip file to become ready: {filePath}");
    }
}
