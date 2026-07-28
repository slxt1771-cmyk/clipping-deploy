using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ClippingSoftware.Core.Recording;
using ClippingSoftware.Data.ClipLibrary;
using ClippingSoftware.Data.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClippingSoftware.App.ViewModels;

/// <summary>
/// Backs the embedded clip editor (M16 - formerly a separate Trim Editor window): pick in/out points on a
/// source clip and export the trimmed range as a new clip. Trimming itself is delegated to
/// <see cref="ClipTrimmer"/> (ffmpeg stream copy); the resulting file is then run back through the same
/// <see cref="RecordingIngestCoordinator"/> instance the rest of the app uses for freshly recorded clips,
/// so the new row gets probed, thumbnailed, and inserted identically, and the clip browser (already
/// subscribed to that coordinator's ClipIngested event) picks it up live without any extra wiring here.
///
/// In-progress edits (in/out points, frame-accurate toggle, per-track enable/volume) are written through
/// to <see cref="ClipEditDraftRepository"/> on every change (M16) - not a debounced autosave, just "save
/// on change", matching AudioSourceManager's existing pattern - so switching to another clip or closing
/// the app never loses work. The draft is deleted once the user actually exports.
/// </summary>
public partial class TrimEditorViewModel : ObservableObject
{
    private readonly string _exportStorageFolder;
    private readonly RecordingIngestCoordinator _ingestCoordinator;
    private readonly ClipTrimmer _clipTrimmer;
    private readonly ClipEditDraftRepository _draftRepository;
    private bool _isLoadingDraft;

    /// <summary>The clip being trimmed. Exposed so the view can bind its file path/duration/game info.</summary>
    public ClipItemViewModel SourceClip { get; }

    /// <summary>
    /// Total length of the source clip in seconds, taken from its already-probed ClipMetadata.Duration
    /// rather than MediaElement.NaturalDuration - it's known immediately (before the MediaElement has
    /// even opened the file) and is what the slider/out-point bounds should be based on.
    /// </summary>
    public double TotalSeconds => SourceClip.Clip.Duration.TotalSeconds;

    [ObservableProperty]
    private double _inPointSeconds;

    [ObservableProperty]
    private double _outPointSeconds;

    /// <summary>
    /// Mirrors the MediaElement playhead. Updated by the view's playback timer (MediaElement.Position
    /// is a plain CLR property, not bindable) so "set in/out from playhead" has something to read.
    /// </summary>
    [ObservableProperty]
    private double _currentPositionSeconds;

    [ObservableProperty]
    private bool _isExporting;

    /// <summary>
    /// When true, ExportAsync asks <see cref="ClipTrimmer"/> for a re-encoded, frame-accurate cut
    /// instead of the default keyframe-snapped stream copy. Off by default: the fast path is right
    /// for the common case, this is an opt-in for when the keyframe snap actually matters.
    /// </summary>
    [ObservableProperty]
    private bool _isFrameAccurate;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>
    /// One checkbox+volume slider per audio stream the source clip's AudioTracksJson recorded (M9/M11),
    /// all enabled at 100% by default. Empty for clips ingested before AudioTracksJson existed (their
    /// AudioTracks list is just "[]") - ExportAsync treats an empty list as "no track info available" and
    /// falls back to the legacy keep-everything behavior rather than exporting a silent clip.
    /// </summary>
    public ObservableCollection<AudioTrackSelectionItem> AudioTrackOptions { get; } = [];

    public TrimEditorViewModel(
        ClipItemViewModel sourceClip,
        string exportStorageFolder,
        RecordingIngestCoordinator ingestCoordinator,
        ClipEditDraftRepository draftRepository,
        ClipTrimmer? clipTrimmer = null)
    {
        SourceClip = sourceClip;
        _exportStorageFolder = exportStorageFolder;
        _ingestCoordinator = ingestCoordinator;
        _draftRepository = draftRepository;
        _clipTrimmer = clipTrimmer ?? new ClipTrimmer();

        _isLoadingDraft = true;

        var draft = _draftRepository.Find(sourceClip.Id);

        _outPointSeconds = draft?.OutPointSeconds ?? TotalSeconds;
        _inPointSeconds = draft?.InPointSeconds ?? 0;
        _isFrameAccurate = draft?.IsFrameAccurate ?? false;

        foreach (var track in SourceClip.Clip.AudioTracks)
        {
            var savedTrack = draft?.AudioTracks.FirstOrDefault(t => t.StreamIndex == track.StreamIndex);
            var item = new AudioTrackSelectionItem(track.StreamIndex, track.Label, track.FilePath)
            {
                IsEnabled = savedTrack?.IsEnabled ?? true,
                VolumePercent = savedTrack?.VolumePercent ?? 100,
            };
            item.PropertyChanged += (_, _) => SaveDraft();
            AudioTrackOptions.Add(item);
        }

        _isLoadingDraft = false;
    }

    /// <summary>Upserts the current in-progress state to ClipEditDraftRepository - called from every
    /// In/Out/FrameAccurate/track property-changed handler below. Skipped while the constructor is still
    /// populating fields from a just-loaded draft, so loading a draft doesn't immediately re-save it.</summary>
    private void SaveDraft()
    {
        if (_isLoadingDraft)
        {
            return;
        }

        _draftRepository.Save(new ClipEditDraft
        {
            ClipId = SourceClip.Id,
            InPointSeconds = InPointSeconds,
            OutPointSeconds = OutPointSeconds,
            IsFrameAccurate = IsFrameAccurate,
            AudioTracks = AudioTrackOptions
                .Select(t => new DraftAudioTrackState(t.StreamIndex, t.IsEnabled, t.VolumePercent))
                .ToList(),
        });
    }

    partial void OnInPointSecondsChanged(double value) => SaveDraft();

    partial void OnOutPointSecondsChanged(double value) => SaveDraft();

    partial void OnIsFrameAccurateChanged(bool value) => SaveDraft();

    /// <summary>Copies the current playhead into the in-point, clamped so in stays before out.</summary>
    public void SetInPointFromPlayhead() =>
        InPointSeconds = Math.Clamp(CurrentPositionSeconds, 0, Math.Max(0, OutPointSeconds - 0.01));

    /// <summary>Copies the current playhead into the out-point, clamped so out stays after in.</summary>
    public void SetOutPointFromPlayhead() =>
        OutPointSeconds = Math.Clamp(CurrentPositionSeconds, InPointSeconds + 0.01, TotalSeconds);

    private bool CanExport() => !IsExporting;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (OutPointSeconds <= InPointSeconds)
        {
            StatusMessage = "Out point must be after the in point.";
            return;
        }

        IsExporting = true;
        StatusMessage = IsFrameAccurate ? "Trimming (frame-accurate, re-encoding)..." : "Trimming...";
        try
        {
            // Only pass an explicit selection when we actually have per-track info to select from -
            // AudioTrackOptions is empty for pre-M9 clips, and TrimAsync's null default means "keep
            // everything" rather than "export silent", which is the right fallback for those.
            var audioTracks = AudioTrackOptions.Count > 0
                ? AudioTrackOptions.Where(t => t.IsEnabled)
                    .Select(t => new AudioTrackExportSelection(t.StreamIndex, t.FilePath, t.VolumePercent / 100.0))
                    .ToList()
                : null;

            var outputPath = await _clipTrimmer.TrimAsync(
                SourceClip.FilePath,
                TimeSpan.FromSeconds(InPointSeconds),
                TimeSpan.FromSeconds(OutPointSeconds),
                _exportStorageFolder,
                frameAccurate: IsFrameAccurate,
                audioTracks: audioTracks);

            StatusMessage = "Trim complete, adding to library...";

            // RecordingIngestCoordinator.IngestAsync deliberately swallows its own exceptions (it must
            // survive as a long-lived background flow for real recordings) and reports success/failure
            // via events instead of a return value or a throw. It's still fully awaited to completion
            // before returning, though - no detached tasks inside it - so subscribing for the duration
            // of this one call and matching on the output path reliably captures the outcome without
            // racing any other file routed through the same coordinator.
            ClipMetadata? insertedClip = null;
            Exception? ingestError = null;

            void OnIngested(ClipMetadata clip)
            {
                if (clip.FilePath == outputPath)
                {
                    insertedClip = clip;
                }
            }

            void OnFailed(string path, Exception ex)
            {
                if (path == outputPath)
                {
                    ingestError = ex;
                }
            }

            _ingestCoordinator.ClipIngested += OnIngested;
            _ingestCoordinator.IngestFailed += OnFailed;
            try
            {
                await _ingestCoordinator.IngestAsync(
                    outputPath,
                    SourceClip.Clip.Source,
                    SourceClip.Clip.DetectedGameProfileId,
                    SourceClip.Clip.DetectedGameDisplayName,
                    isTrimmedCopy: true,
                    sourceClipId: SourceClip.Id);
            }
            finally
            {
                _ingestCoordinator.ClipIngested -= OnIngested;
                _ingestCoordinator.IngestFailed -= OnFailed;
            }

            if (ingestError is not null)
            {
                throw ingestError;
            }

            // The draft's job (preserving in-progress work) is done now that the edit actually shipped.
            _draftRepository.Delete(SourceClip.Id);

            StatusMessage = insertedClip is not null
                ? $"Exported: {Path.GetFileName(outputPath)}"
                : $"Exported: {Path.GetFileName(outputPath)} (added to library)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    partial void OnIsExportingChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();
}
