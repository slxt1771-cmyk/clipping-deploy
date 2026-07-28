namespace ClippingSoftware.Data.Models;

/// <summary>One audio track's checkbox/slider state within a saved edit draft (M16) - mirrors the App
/// project's AudioTrackSelectionItem but lives here since Data can't reference App.</summary>
public record DraftAudioTrackState(int StreamIndex, bool IsEnabled, int VolumePercent);

/// <summary>
/// In-progress trim state for the embedded single-clip editor (M16), one row per clip (ClipId is the
/// primary key - a clip has at most one draft). Written through to the repository on every change rather
/// than a debounced autosave, so an app restart never loses progress; the row is deleted once the user
/// actually exports, since the draft's job is done at that point.
/// </summary>
public class ClipEditDraft
{
    public Guid ClipId { get; set; }
    public double InPointSeconds { get; set; }
    public double OutPointSeconds { get; set; }
    public bool IsFrameAccurate { get; set; }
    public List<DraftAudioTrackState> AudioTracks { get; set; } = [];
}
