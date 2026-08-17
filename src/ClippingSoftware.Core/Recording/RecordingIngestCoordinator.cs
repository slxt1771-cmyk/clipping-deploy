using ClippingSoftware.Core.Obs;
using ClippingSoftware.Data.ClipLibrary;
using ClippingSoftware.Data.Models;

namespace ClippingSoftware.Core.Recording;

/// <summary>
/// Wires ObsController's RecordingStopped/ReplayBufferSaved events to the ingest pipeline:
/// wait for the file to be fully flushed (ClipIngestWatcher), extract duration/resolution/track
/// info (ClipMetadataExtractor), generate a thumbnail (ThumbnailGenerator), and persist a
/// ClipMetadata row (ClipRepository). Construct one instance and keep it alive for the app's
/// lifetime; dispose it to unsubscribe from ObsController.
/// </summary>
public class RecordingIngestCoordinator : IDisposable
{
    private readonly ObsController _obsController;
    private readonly ClipRepository _clipRepository;
    private readonly AudioSourceManager? _audioSourceManager;
    private readonly TagRepository? _tagRepository;
    private readonly ClipIngestWatcher _ingestWatcher;
    private readonly ClipMetadataExtractor _metadataExtractor;
    private readonly ThumbnailGenerator _thumbnailGenerator;
    private readonly AudioTrackExtractor _audioTrackExtractor;
    private readonly string _thumbnailDirectory;
    private readonly string _audioTracksDirectory;

    /// <summary>Raised on a successful ingest, with the persisted clip row.</summary>
    public event Action<ClipMetadata>? ClipIngested;

    /// <summary>Raised when ingesting a given file path fails (file never stabilized, ffprobe/ffmpeg error, etc).</summary>
    public event Action<string, Exception>? IngestFailed;

    /// <param name="audioSourceManager">Optional so this stays constructible/testable without a live audio
    /// source list; MainViewModel passes its real instance so ingested clips get accurate per-track labels
    /// (see BuildAudioTracks). Null just means every ingested clip's AudioTracks list is empty.</param>
    /// <param name="tagRepository">Optional so this stays constructible/testable without a tag store; when
    /// supplied, a clip with a recognized (non-null) detected game/app display name is auto-tagged with a
    /// tag of that same name at ingest time (see AutoTagByGame) - creating the tag on first use, reusing it
    /// on every later clip from that same game.</param>
    public RecordingIngestCoordinator(
        ObsController obsController,
        ClipRepository clipRepository,
        AudioSourceManager? audioSourceManager = null,
        TagRepository? tagRepository = null,
        ClipIngestWatcher? ingestWatcher = null,
        ClipMetadataExtractor? metadataExtractor = null,
        ThumbnailGenerator? thumbnailGenerator = null,
        AudioTrackExtractor? audioTrackExtractor = null,
        string? thumbnailDirectory = null,
        string? audioTracksDirectory = null)
    {
        _obsController = obsController;
        _clipRepository = clipRepository;
        _audioSourceManager = audioSourceManager;
        _tagRepository = tagRepository;
        _ingestWatcher = ingestWatcher ?? new ClipIngestWatcher();
        _metadataExtractor = metadataExtractor ?? new ClipMetadataExtractor();
        _thumbnailGenerator = thumbnailGenerator ?? new ThumbnailGenerator();
        _audioTrackExtractor = audioTrackExtractor ?? new AudioTrackExtractor();
        _thumbnailDirectory = thumbnailDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClippingSoftware", "Thumbnails");
        _audioTracksDirectory = audioTracksDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClippingSoftware", "AudioTracks");

        _obsController.RecordingStopped += OnRecordingStopped;
        _obsController.ReplayBufferSaved += OnReplayBufferSaved;
    }

    private void OnRecordingStopped(string? outputPath)
    {
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        _ = IngestAsync(outputPath, ClipSourceType.ManualRecording);
    }

    private void OnReplayBufferSaved(string outputPath)
    {
        _ = IngestAsync(outputPath, ClipSourceType.ReplayBuffer);
    }

    /// <summary>
    /// Runs the full ingest pipeline for one finished clip file. Public so callers (and the
    /// two event handlers above) can drive it directly: game-detection state is threaded through
    /// as gameProfileId/gameDisplayName, and the Trim Editor drives this same pipeline for
    /// exported trimmed copies via isTrimmedCopy/sourceClipId, so a trimmed export gets identical
    /// probing/thumbnailing/DB-insert/live-append treatment to a freshly recorded clip instead of
    /// duplicating that logic.
    /// </summary>
    public async Task IngestAsync(
        string filePath,
        ClipSourceType source,
        Guid? gameProfileId = null,
        string? gameDisplayName = null,
        bool isTrimmedCopy = false,
        Guid? sourceClipId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _ingestWatcher.WaitForFileReadyAsync(filePath, cancellationToken);

            var probe = await _metadataExtractor.ExtractAsync(filePath, cancellationToken);
            var clipId = Guid.NewGuid();

            List<string> trackFilePaths = [];
            try
            {
                trackFilePaths = await _audioTrackExtractor.ExtractAsync(
                    filePath, probe.AudioStreamCount, Path.Combine(_audioTracksDirectory, clipId.ToString()), cancellationToken);
            }
            catch
            {
                // Best-effort: extraction failing shouldn't block ingesting the clip itself - the Trim
                // Editor falls back to mapping straight from the still-intact source container for any
                // track with no extracted file (see AudioTrackInfo.FilePath / ClipTrimmer).
            }

            var clip = new ClipMetadata
            {
                Id = clipId,
                FilePath = filePath,
                RecordedAtUtc = DateTime.UtcNow,
                Duration = probe.Duration,
                DetectedGameProfileId = gameProfileId,
                DetectedGameDisplayName = gameDisplayName,
                Source = source,
                VideoWidth = probe.VideoWidth,
                VideoHeight = probe.VideoHeight,
                AudioTracks = BuildAudioTracks(probe.AudioStreamCount, trackFilePaths),
                IsTrimmedCopy = isTrimmedCopy,
                SourceClipId = sourceClipId,
            };

            var thumbnailPath = Path.Combine(_thumbnailDirectory, $"{clip.Id}.jpg");
            await _thumbnailGenerator.GenerateAsync(filePath, thumbnailPath, probe.Duration, cancellationToken);
            clip.ThumbnailPath = thumbnailPath;

            _clipRepository.Insert(clip);
            AutoTagByGame(clip);

            ClipIngested?.Invoke(clip);
        }
        catch (Exception ex)
        {
            IngestFailed?.Invoke(filePath, ex);
        }
    }

    /// <summary>Tags a freshly-ingested clip with its detected game/app display name, e.g. clips from
    /// "Rust" auto-get a "Rust" tag and clips from "Apex Legends" auto-get an "Apex Legends" tag - no-op
    /// when there's no tag repository, no detected game (the unmatched/Default case), or an empty name.
    /// GetOrCreateByName reuses the same tag across every clip from that game rather than creating a
    /// duplicate each time. The auto-assigned color is picked deterministically from the game name (not
    /// user-configurable at creation time) purely so different games default to visually distinct colors -
    /// the user can still recolor the tag afterward like any other, from the TAGS manager.</summary>
    private void AutoTagByGame(ClipMetadata clip)
    {
        if (_tagRepository is null || string.IsNullOrWhiteSpace(clip.DetectedGameDisplayName))
        {
            return;
        }

        var tag = _tagRepository.GetOrCreateByName(clip.DetectedGameDisplayName, PickAutoTagColor(clip.DetectedGameDisplayName));
        _tagRepository.AddClipTag(clip.Id, tag.Id);
    }

    private static readonly string[] AutoTagPalette =
        ["#E0665A", "#E0B15A", "#DDE05A", "#8CE05A", "#5AE0B1", "#5AB1E0", "#8C5AE0", "#E05AB1"];

    /// <summary>
    /// Picks a palette slot from the game name with an inlined FNV-1a hash rather than string.GetHashCode().
    /// GetHashCode is randomly seeded per process on .NET Core, so it would have given the same game a
    /// different color on every app launch - which for a tag whose color is written once at creation time
    /// means "Rust" ends up whatever color it happened to hash to on the run that first saw it, and two
    /// machines (or two runs) disagree. FNV-1a is stable across processes and machines, which is what the
    /// "deterministically from the game name" intent actually required.
    /// </summary>
    private static string PickAutoTagColor(string name)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var c in name)
        {
            hash = (hash ^ c) * prime;
        }

        return AutoTagPalette[hash % AutoTagPalette.Length];
    }

    /// <summary>
    /// Zips ffprobe's audio stream count against AudioSourceManager's current track layout (already ordered
    /// by track number) to attach real labels: stream 0 is whichever source has the lowest track number, and
    /// so on. This is positional, not identity-based - if tracks were reassigned between recording and
    /// ingest (a live edge case, not the common path) the labels could be briefly stale, but there's no way
    /// to recover "who fed which stream" after the fact other than this convention, which is exactly what
    /// ClipMetadataExtractor's own doc comment already relies on.
    /// </summary>
    private List<AudioTrackInfo> BuildAudioTracks(int audioStreamCount, IReadOnlyList<string> trackFilePaths)
    {
        if (_audioSourceManager is null)
        {
            return [];
        }

        return _audioSourceManager.Sources
            .Take(audioStreamCount)
            .Select((source, index) => new AudioTrackInfo(
                index,
                source.DisplayLabel,
                index < trackFilePaths.Count ? trackFilePaths[index] : null))
            .ToList();
    }

    public void Dispose()
    {
        _obsController.RecordingStopped -= OnRecordingStopped;
        _obsController.ReplayBufferSaved -= OnReplayBufferSaved;
        GC.SuppressFinalize(this);
    }
}
