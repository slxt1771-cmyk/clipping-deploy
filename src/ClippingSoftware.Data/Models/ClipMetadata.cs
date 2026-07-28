namespace ClippingSoftware.Data.Models;

public enum ClipSourceType
{
    ReplayBuffer,
    ManualRecording
}

/// <summary>One audio stream in a recorded clip's container, in ffprobe stream order (StreamIndex 0 is the
/// first audio stream, etc). Label is whatever AudioSourceManager's track layout said fed that position at
/// record time (e.g. "Desktop Audio", "Mic/Aux", or an app's display label) - see RecordingIngestCoordinator.
/// FilePath points at this track's own extracted audio file (AudioTrackExtractor's output, M11) so the
/// Trim Editor can load and level it independently instead of only being able to include/exclude the whole
/// stream from the still-muxed original; null for clips ingested before per-track extraction existed, or
/// if extraction failed for this clip (best-effort - see RecordingIngestCoordinator).</summary>
public record AudioTrackInfo(int StreamIndex, string Label, string? FilePath = null);

public class ClipMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FilePath { get; set; } = string.Empty;
    public string? ThumbnailPath { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public TimeSpan Duration { get; set; }
    public Guid? DetectedGameProfileId { get; set; }
    public string? DetectedGameDisplayName { get; set; }
    public ClipSourceType Source { get; set; }
    public int VideoWidth { get; set; }
    public int VideoHeight { get; set; }
    public List<AudioTrackInfo> AudioTracks { get; set; } = [];
    public bool IsTrimmedCopy { get; set; }
    public Guid? SourceClipId { get; set; }
}
