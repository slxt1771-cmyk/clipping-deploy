using CommunityToolkit.Mvvm.ComponentModel;

namespace ClippingSoftware.App.ViewModels;

/// <summary>
/// Binding-friendly per-track checkbox+slider for the Trim Editor's audio-track selection (M9/M11): wraps
/// one <see cref="Data.Models.AudioTrackInfo"/> from the source clip with settable IsEnabled/VolumePercent
/// the view can bind a CheckBox/Slider to. TrimEditorViewModel reads the checked subset (with each one's
/// level) at export time and builds ClipTrimmer.TrimAsync's audioTracks selection from it - FilePath is
/// carried straight through from AudioTrackInfo so the export can use the separately extracted per-track
/// file when one exists (null for clips ingested before per-track extraction existed).
/// </summary>
public partial class AudioTrackSelectionItem(int streamIndex, string label, string? filePath) : ObservableObject
{
    public int StreamIndex { get; } = streamIndex;

    public string Label { get; } = label;

    public string? FilePath { get; } = filePath;

    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>0-100, same convention as AudioSourceRowViewModel.VolumePercent - sent to ClipTrimmer as a
    /// linear multiplier (VolumePercent / 100.0).</summary>
    [ObservableProperty]
    private int _volumePercent = 100;
}
